using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFmpegVideoPlayer.Core;
using Path = Avalonia.Controls.Shapes.Path;
using OpenTKAudioFactory = FFmpegVideoPlayer.Audio.OpenTK.AudioPlayerFactory;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>
/// Video rendering mode.
/// </summary>
public enum VideoRenderingMode
{
    /// <summary>
    /// CPU-based rendering using WriteableBitmap (default, backward compatible).
    /// </summary>
    Cpu,

    /// <summary>
    /// Hardware-accelerated OpenGL rendering (requires OpenGL support).
    /// </summary>
    OpenGL
}

/// <summary>
/// A self-contained video player control with playback controls, seek bar, and volume control.
/// Uses FFmpeg for cross-platform media playback including ARM64 macOS.
/// Requires FFmpeg 8.x libraries (libavcodec.62) to be available.
/// </summary>
public partial class VideoPlayerControl : UserControl
{
    private FFmpegMediaPlayer? _mediaPlayer;
    private Slider? _seekBar;
    private Slider? _volumeSlider;
    private TextBlock? _currentTimeText;
    private TextBlock? _totalTimeText;
    private Path? _playPauseIcon;
    private TextBlock? _playPauseText; // null when using icon-only layout
    private Path? _volumeIcon;
    private bool _isDraggingSeekBar;
    private bool _isMuted;
    private int _previousVolume = 100;
    private bool _isInitialized;
    private Border? _controlPanelBorder;
    private Border? _videoBorder;
    private Button? _openButton;
    private IVideoRenderer? _videoRenderer;
    private string? _currentMediaPath;
    private bool _hasMediaLoaded;
    private CancellationTokenSource? _sourceOpenCancellation;
    private long _openGeneration;
    private MediaSource? _requestedSource;

    /// <summary>
    /// Defines the Volume property.
    /// </summary>
    public static readonly StyledProperty<int> VolumeProperty =
        AvaloniaProperty.Register<VideoPlayerControl, int>(nameof(Volume), 100);

    /// <summary>
    /// Defines the AutoPlay property.
    /// </summary>
    public static readonly StyledProperty<bool> AutoPlayProperty =
        AvaloniaProperty.Register<VideoPlayerControl, bool>(nameof(AutoPlay), false);

    /// <summary>
    /// Defines the ShowControls property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowControlsProperty =
        AvaloniaProperty.Register<VideoPlayerControl, bool>(nameof(ShowControls), true);

    /// <summary>
    /// Defines the ShowOpenButton property.
    /// </summary>
    public static readonly StyledProperty<bool> ShowOpenButtonProperty =
        AvaloniaProperty.Register<VideoPlayerControl, bool>(nameof(ShowOpenButton), true);

    /// <summary>
    /// Defines the Source property for setting video path directly.
    /// </summary>
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<VideoPlayerControl, string?>(nameof(Source), null);

    /// <summary>Defines the typed Media property.</summary>
    public static readonly StyledProperty<MediaSource?> MediaProperty =
        AvaloniaProperty.Register<VideoPlayerControl, MediaSource?>(nameof(Media), null);

    /// <summary>
    /// Defines the ControlPanelBackground property.
    /// </summary>
    public static readonly StyledProperty<global::Avalonia.Media.IBrush?> ControlPanelBackgroundProperty =
        AvaloniaProperty.Register<VideoPlayerControl, global::Avalonia.Media.IBrush?>(nameof(ControlPanelBackground), null);

    /// <summary>
    /// Defines the VideoBackground property.
    /// </summary>
    public static readonly StyledProperty<global::Avalonia.Media.IBrush?> VideoBackgroundProperty =
        AvaloniaProperty.Register<VideoPlayerControl, global::Avalonia.Media.IBrush?>(nameof(VideoBackground), null);

    /// <summary>
    /// Defines the VideoStretch property.
    /// </summary>
    public static readonly StyledProperty<global::Avalonia.Media.Stretch> VideoStretchProperty =
        AvaloniaProperty.Register<VideoPlayerControl, global::Avalonia.Media.Stretch>(nameof(VideoStretch), global::Avalonia.Media.Stretch.Uniform);

    /// <summary>
    /// Defines the EnableKeyboardShortcuts property.
    /// </summary>
    public static readonly StyledProperty<bool> EnableKeyboardShortcutsProperty =
        AvaloniaProperty.Register<VideoPlayerControl, bool>(nameof(EnableKeyboardShortcuts), true);

    /// <summary>
    /// Gets or sets the volume (0-100).
    /// </summary>
    public int Volume
    {
        get => GetValue(VolumeProperty);
        set
        {
            SetValue(VolumeProperty, Math.Clamp(value, 0, 100));
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = value;
            }
            if (_volumeSlider != null)
            {
                _volumeSlider.Value = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the video should auto-play when opened.
    /// </summary>
    public bool AutoPlay
    {
        get => GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the playback controls are visible.
    /// </summary>
    public bool ShowControls
    {
        get => GetValue(ShowControlsProperty);
        set
        {
            SetValue(ShowControlsProperty, value);
            if (_controlPanelBorder != null)
            {
                _controlPanelBorder.IsVisible = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the Open button is visible.
    /// When false, the Open button is hidden (useful for embedded players with programmatic source).
    /// </summary>
    public bool ShowOpenButton
    {
        get => GetValue(ShowOpenButtonProperty);
        set => SetValue(ShowOpenButtonProperty, value);
    }

    /// <summary>
    /// Gets or sets the video source path. Setting this will automatically load and play the video.
    /// </summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets a reusable typed media source. This takes precedence over
    /// <see cref="Source"/> and supports custom headers, streams and manifests.
    /// </summary>
    public MediaSource? Media
    {
        get => GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for the control panel.
    /// Default is White. Set to any brush to customize the appearance.
    /// </summary>
    public global::Avalonia.Media.IBrush? ControlPanelBackground
    {
        get => GetValue(ControlPanelBackgroundProperty);
        set => SetValue(ControlPanelBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for the video display area.
    /// Default is null (transparent). Set to a brush (e.g., Brushes.Black) to show a background color.
    /// When null or Transparent, the background will be transparent, allowing the parent control's background to show through.
    /// This is especially useful when playing videos with transparency or when no video is loaded.
    /// </summary>
    public global::Avalonia.Media.IBrush? VideoBackground
    {
        get => GetValue(VideoBackgroundProperty);
        set => SetValue(VideoBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the stretch mode for the video.
    /// Default is Uniform. Options: None, Fill, Uniform, UniformToFill.
    /// </summary>
    public global::Avalonia.Media.Stretch VideoStretch
    {
        get => GetValue(VideoStretchProperty);
        set => SetValue(VideoStretchProperty, value);
    }

    /// <summary>
    /// Gets or sets whether keyboard shortcuts are enabled.
    /// Default is true.
    /// </summary>
    public bool EnableKeyboardShortcuts
    {
        get => GetValue(EnableKeyboardShortcutsProperty);
        set => SetValue(EnableKeyboardShortcutsProperty, value);
    }

    /// <summary>
    /// Defines the AudioPlayerFactory property for injecting custom audio player implementations.
    /// If null, audio playback will be disabled (video-only mode).
    /// </summary>
    public static readonly StyledProperty<Func<int, int, IAudioPlayer?>?> AudioPlayerFactoryProperty =
        AvaloniaProperty.Register<VideoPlayerControl, Func<int, int, IAudioPlayer?>?>(nameof(AudioPlayerFactory), null);

    /// <summary>
    /// Gets or sets the factory function for creating audio players.
    /// Signature: (sampleRate, channels) => IAudioPlayer?
    /// If null, audio playback will be disabled (video-only mode).
    /// </summary>
    public Func<int, int, IAudioPlayer?>? AudioPlayerFactory
    {
        get => GetValue(AudioPlayerFactoryProperty);
        set => SetValue(AudioPlayerFactoryProperty, value);
    }

    /// <summary>
    /// Defines the IconProvider property for injecting custom icon geometries.
    /// If null, default Avalonia shapes will be used.
    /// </summary>
    public static readonly StyledProperty<IIconProvider?> IconProviderProperty =
        AvaloniaProperty.Register<VideoPlayerControl, IIconProvider?>(nameof(IconProvider), null);

    /// <summary>
    /// Gets or sets the icon provider for custom icon geometries.
    /// If null, default Avalonia shapes will be used.
    /// </summary>
    public IIconProvider? IconProvider
    {
        get => GetValue(IconProviderProperty);
        set => SetValue(IconProviderProperty, value);
    }

    /// <summary>
    /// Defines the RenderingMode property.
    /// </summary>
    public static readonly StyledProperty<VideoRenderingMode> RenderingModeProperty =
        AvaloniaProperty.Register<VideoPlayerControl, VideoRenderingMode>(nameof(RenderingMode), VideoRenderingMode.Cpu);

    /// <summary>
    /// Gets or sets the video rendering mode.
    /// Cpu: Uses WriteableBitmap (default, backward compatible).
    /// OpenGL: Uses hardware-accelerated OpenGL rendering (requires OpenGL support).
    /// </summary>
    public VideoRenderingMode RenderingMode
    {
        get => GetValue(RenderingModeProperty);
        set => SetValue(RenderingModeProperty, value);
    }

    // ── Control panel styling properties ──

    /// <summary>Defines the IconSize property.</summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<VideoPlayerControl, double>(nameof(IconSize), 14);

    /// <summary>Defines the ControlFontSize property.</summary>
    public static readonly StyledProperty<double> ControlFontSizeProperty =
        AvaloniaProperty.Register<VideoPlayerControl, double>(nameof(ControlFontSize), 11);

    /// <summary>Defines the ButtonPadding property.</summary>
    public static readonly StyledProperty<Thickness> ButtonPaddingProperty =
        AvaloniaProperty.Register<VideoPlayerControl, Thickness>(nameof(ButtonPadding), new Thickness(6, 3));

    /// <summary>Defines the ControlPanelPadding property.</summary>
    public static readonly StyledProperty<Thickness> ControlPanelPaddingProperty =
        AvaloniaProperty.Register<VideoPlayerControl, Thickness>(nameof(ControlPanelPadding), new Thickness(6, 4));

    /// <summary>Defines the ButtonCornerRadius property.</summary>
    public static readonly StyledProperty<CornerRadius> ButtonCornerRadiusProperty =
        AvaloniaProperty.Register<VideoPlayerControl, CornerRadius>(nameof(ButtonCornerRadius), new CornerRadius(3));

    /// <summary>Defines the ControlForeground property.</summary>
    public static readonly StyledProperty<global::Avalonia.Media.IBrush?> ControlForegroundProperty =
        AvaloniaProperty.Register<VideoPlayerControl, global::Avalonia.Media.IBrush?>(nameof(ControlForeground), null);

    /// <summary>Defines the ButtonBackground property.</summary>
    public static readonly StyledProperty<global::Avalonia.Media.IBrush?> ButtonBackgroundProperty =
        AvaloniaProperty.Register<VideoPlayerControl, global::Avalonia.Media.IBrush?>(nameof(ButtonBackground), null);

    /// <summary>
    /// Gets or sets the size of playback control icons. Default is 14.
    /// </summary>
    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size for control panel text. Default is 11.
    /// </summary>
    public double ControlFontSize
    {
        get => GetValue(ControlFontSizeProperty);
        set => SetValue(ControlFontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding inside playback buttons. Default is 6,3.
    /// </summary>
    public Thickness ButtonPadding
    {
        get => GetValue(ButtonPaddingProperty);
        set => SetValue(ButtonPaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding of the entire control panel. Default is 6,4.
    /// </summary>
    public Thickness ControlPanelPadding
    {
        get => GetValue(ControlPanelPaddingProperty);
        set => SetValue(ControlPanelPaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius for playback buttons. Default is 3.
    /// </summary>
    public CornerRadius ButtonCornerRadius
    {
        get => GetValue(ButtonCornerRadiusProperty);
        set => SetValue(ButtonCornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush for control text and icons.
    /// If null, defaults to #333333.
    /// </summary>
    public global::Avalonia.Media.IBrush? ControlForeground
    {
        get => GetValue(ControlForegroundProperty);
        set => SetValue(ControlForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for playback buttons.
    /// If null, defaults to #e8e8e8.
    /// </summary>
    public global::Avalonia.Media.IBrush? ButtonBackground
    {
        get => GetValue(ButtonBackgroundProperty);
        set => SetValue(ButtonBackgroundProperty, value);
    }

    /// <summary>
    /// Gets the full path of the currently loaded media file, if any.
    /// </summary>
    public string? CurrentMediaPath => _currentMediaPath;

    /// <summary>
    /// Gets whether the control currently has a media resource loaded.
    /// </summary>
    public bool HasMediaLoaded => _hasMediaLoaded;

    /// <summary>Gets the current standalone player lifecycle state.</summary>
    public PlaybackState PlaybackState => _mediaPlayer?.State ?? PlaybackState.Closed;

    /// <summary>Gets the latest structured media error.</summary>
    public MediaError? LastError => _mediaPlayer?.LastError;

    /// <summary>
    /// Gets whether a video is currently playing.
    /// </summary>
    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;

    /// <summary>
    /// Gets the current playback position in milliseconds.
    /// </summary>
    public long Position => _mediaPlayer != null ? (long)(_mediaPlayer.Position * _mediaPlayer.Length) : 0;

    /// <summary>
    /// Gets the total duration of the current media in milliseconds.
    /// </summary>
    public long Duration => _mediaPlayer?.Length ?? 0;

    // ── Fullscreen support ──

    /// <summary>Defines the ShowFullscreenButton property.</summary>
    public static readonly StyledProperty<bool> ShowFullscreenButtonProperty =
        AvaloniaProperty.Register<VideoPlayerControl, bool>(nameof(ShowFullscreenButton), false);

    /// <summary>
    /// Gets or sets whether the fullscreen toggle button is visible. Default is false.
    /// </summary>
    public bool ShowFullscreenButton
    {
        get => GetValue(ShowFullscreenButtonProperty);
        set => SetValue(ShowFullscreenButtonProperty, value);
    }

    /// <summary>Defines the IsFullscreen property.</summary>
    public static readonly StyledProperty<bool> IsFullscreenProperty =
        AvaloniaProperty.Register<VideoPlayerControl, bool>(nameof(IsFullscreen), false);

    /// <summary>
    /// Gets or sets whether the player is in fullscreen mode. Controls the fullscreen button icon.
    /// </summary>
    public bool IsFullscreen
    {
        get => GetValue(IsFullscreenProperty);
        set => SetValue(IsFullscreenProperty, value);
    }

    /// <summary>
    /// Occurs when the fullscreen button is clicked. The consuming app handles the actual resize.
    /// </summary>
    public event EventHandler? FullscreenToggle;

    /// <summary>
    /// Occurs when playback starts.
    /// </summary>
    public event EventHandler? PlaybackStarted;

    /// <summary>
    /// Occurs when media is successfully opened.
    /// </summary>
    public event EventHandler<MediaOpenedEventArgs>? MediaOpened;

    /// <summary>Occurs immediately before source resolution starts.</summary>
    public event EventHandler<MediaOpeningEventArgs>? MediaOpening;

    /// <summary>Occurs when source resolution, opening, or playback fails.</summary>
    public event EventHandler<MediaFailedEventArgs>? MediaFailed;

    /// <summary>Occurs whenever the standalone player lifecycle state changes.</summary>
    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;

    /// <summary>
    /// Occurs when playback is paused.
    /// </summary>
    public event EventHandler? PlaybackPaused;

    /// <summary>
    /// Occurs when playback is stopped.
    /// </summary>
    public event EventHandler? PlaybackStopped;

    /// <summary>
    /// Occurs when the media ends.
    /// </summary>
    public event EventHandler? MediaEnded;

    /// <summary>
    /// Creates a new instance of the VideoPlayerControl.
    /// </summary>
    public VideoPlayerControl()
    {
        InitializeComponent();

        // Enable focus so we can receive keyboard events
        Focusable = true;

        _seekBar = this.FindControl<Slider>("SeekBar");
        _volumeSlider = this.FindControl<Slider>("VolumeSlider");
        _currentTimeText = this.FindControl<TextBlock>("CurrentTimeText");
        _totalTimeText = this.FindControl<TextBlock>("TotalTimeText");
        _playPauseIcon = this.FindControl<Path>("PlayPauseIcon");
        _playPauseText = this.FindControl<TextBlock>("PlayPauseText");
        _volumeIcon = this.FindControl<Path>("VolumeIcon");
        _controlPanelBorder = this.FindControl<Border>("ControlPanelBorder");
        _videoBorder = this.FindControl<Border>("VideoBorder");
        _openButton = this.FindControl<Button>("OpenButton");

        // Apply initial visibility based on properties
        if (_controlPanelBorder != null)
        {
            _controlPanelBorder.IsVisible = ShowControls;
        }
        if (_openButton != null)
        {
            _openButton.IsVisible = ShowOpenButton;
        }

        // Setup seek bar events
        if (_seekBar != null)
        {
            _seekBar.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnSeekBarPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _seekBar.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, OnSeekBarPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _seekBar.AddHandler(Avalonia.Input.InputElement.PointerCaptureLostEvent, OnSeekBarPointerCaptureLost, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        // Setup volume slider
        if (_volumeSlider != null)
        {
            _volumeSlider.ValueChanged += OnVolumeChanged;
        }

        // Initialize when attached to visual tree
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        
        // Handle property changes
        PropertyChanged += OnPropertyChanged;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!EnableKeyboardShortcuts) return;

        var modifiers = e.KeyModifiers;
        bool isCtrl = modifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Space:
                TogglePlayPause();
                e.Handled = true;
                break;
            
            case Key.Left:
                if (_mediaPlayer != null && _mediaPlayer.Length > 0)
                {
                    // Seek backward: 5s normal, 30s with Ctrl
                    double currentPos = _mediaPlayer.Position * _mediaPlayer.Length; // ms
                    double jump = isCtrl ? 30000 : 5000; // ms
                    double newPos = Math.Max(0, currentPos - jump);
                    float newPercent = (float)(newPos / _mediaPlayer.Length);
                    Seek(newPercent);
                }
                e.Handled = true;
                break;

            case Key.Right:
                if (_mediaPlayer != null && _mediaPlayer.Length > 0)
                {
                    // Seek forward: 5s normal, 30s with Ctrl
                    double currentPos = _mediaPlayer.Position * _mediaPlayer.Length; // ms
                    double jump = isCtrl ? 30000 : 5000; // ms
                    double newPos = Math.Min(_mediaPlayer.Length, currentPos + jump);
                    float newPercent = (float)(newPos / _mediaPlayer.Length);
                    Seek(newPercent);
                }
                e.Handled = true;
                break;

            case Key.Up:
                Volume = Math.Min(100, Volume + 5);
                e.Handled = true;
                break;

            case Key.Down:
                Volume = Math.Max(0, Volume - 5);
                e.Handled = true;
                break;

            case Key.M:
                ToggleMute();
                e.Handled = true;
                break;
        }
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SourceProperty)
        {
            var newSource = e.NewValue as string;
            if (Media is null && !string.IsNullOrWhiteSpace(newSource) && _isInitialized)
                _ = OpenFromPropertyAsync(MediaSource.FromLocation(newSource));
            else if (Media is null && string.IsNullOrWhiteSpace(newSource))
                _ = CloseAsync();
        }
        else if (e.Property == MediaProperty)
        {
            if (e.NewValue is MediaSource mediaSource && _isInitialized)
                _ = OpenFromPropertyAsync(mediaSource);
            else if (e.NewValue is null && string.IsNullOrWhiteSpace(Source))
                _ = CloseAsync();
        }
        else if (e.Property == ShowOpenButtonProperty)
        {
            if (_openButton != null)
            {
                _openButton.IsVisible = (bool)(e.NewValue ?? true);
            }
        }
        else if (e.Property == ControlPanelBackgroundProperty)
        {
            if (_controlPanelBorder != null && e.NewValue is global::Avalonia.Media.IBrush brush)
            {
                _controlPanelBorder.Background = brush;
            }
        }
        else if (e.Property == VideoBackgroundProperty)
        {
            if (_videoBorder != null)
            {
                _videoBorder.Background = e.NewValue as global::Avalonia.Media.IBrush; // Can be null for transparency
            }
        }
        else if (e.Property == VideoStretchProperty)
        {
            // Apply to renderer
            if (e.NewValue is global::Avalonia.Media.Stretch rendererStretch)
            {
                if (_videoRenderer is CpuVideoRenderer cpuRenderer)
                {
                    cpuRenderer.Stretch = rendererStretch;
                }
                else if (_videoRenderer is OpenGLVideoRenderer glRenderer)
                {
                    glRenderer.Stretch = rendererStretch;
                }
            }
        }
        else if (e.Property == ShowControlsProperty)
        {
            if (_controlPanelBorder != null)
            {
                _controlPanelBorder.IsVisible = (bool)(e.NewValue ?? true);
            }
        }
        else if (e.Property == RenderingModeProperty)
        {
            SetupVideoRenderer();
        }
        else if (e.Property == ShowFullscreenButtonProperty)
        {
            var btn = this.FindControl<Button>("FullscreenButton");
            if (btn != null) btn.IsVisible = (bool)(e.NewValue ?? false);
        }
        else if (e.Property == IsFullscreenProperty)
        {
            UpdateFullscreenIcon();
        }
        else if (e.Property == ControlForegroundProperty)
        {
            ApplyControlForeground(e.NewValue as global::Avalonia.Media.IBrush);
        }
        else if (e.Property == ButtonBackgroundProperty)
        {
            ApplyButtonBackground(e.NewValue as global::Avalonia.Media.IBrush);
        }
    }

    private void ApplyControlForeground(global::Avalonia.Media.IBrush? brush)
    {
        if (brush == null) return;
        // Apply to all icon Path elements
        var icons = new[] {
            this.FindControl<Path>("FolderIcon"),
            _playPauseIcon,
            this.FindControl<Path>("StopIcon"),
            _volumeIcon
        };
        foreach (var icon in icons)
        {
            if (icon != null) icon.Fill = brush;
        }
        // Apply to time text
        if (_currentTimeText != null) _currentTimeText.Foreground = brush;
        if (_totalTimeText != null) _totalTimeText.Foreground = brush;
    }

    private void ApplyButtonBackground(global::Avalonia.Media.IBrush? brush)
    {
        if (brush == null) return;
        var buttons = new[] {
            _openButton,
            this.FindControl<Button>("PlayPauseButton"),
            this.FindControl<Button>("StopButton")
        };
        foreach (var btn in buttons)
        {
            if (btn != null) btn.Background = brush;
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        InitializePlayer();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Cleanup();
    }

    private void InitializePlayer()
    {
        if (_isInitialized) return;

        try
        {
            // Ensure FFmpeg is initialized globally
            if (!FFmpegInitializer.IsInitialized)
            {
                FFmpegInitializer.Initialize();
            }

            // Use StyledProperty AudioPlayerFactory when set; otherwise default to OpenAL via OpenTK.
            // Note: do not pass `AudioPlayerFactory` bare — that is the instance property and is null by default,
            // which disabled all audio (video-only mode).
            Func<int, int, IAudioPlayer?> effectiveAudioFactory = AudioPlayerFactory
                ?? ((sr, ch) => OpenTKAudioFactory.Create(sr, ch));

            _mediaPlayer = new FFmpegMediaPlayer(
                synchronizationCallback: action => Dispatcher.UIThread.Post(action),
                audioPlayerFactory: effectiveAudioFactory);

            // Subscribe to media player events
            _mediaPlayer.PositionChanged += OnPositionChanged;
            _mediaPlayer.LengthChanged += OnLengthChanged;
            _mediaPlayer.Playing += OnPlaying;
            _mediaPlayer.Paused += OnPaused;
            _mediaPlayer.Stopped += OnStopped;
            _mediaPlayer.EndReached += OnEndReached;
            _mediaPlayer.FrameReady += OnFrameReady;
            _mediaPlayer.MediaFailed += OnCoreMediaFailed;
            _mediaPlayer.StateChanged += OnCoreStateChanged;

            _isInitialized = true;
            
            // Setup video renderer based on rendering mode
            SetupVideoRenderer();
            
            // Apply initial property values
            if (_openButton != null)
            {
                _openButton.IsVisible = ShowOpenButton;
            }
            var fullscreenBtn = this.FindControl<Button>("FullscreenButton");
            if (fullscreenBtn != null)
            {
                fullscreenBtn.IsVisible = ShowFullscreenButton;
            }
            if (_controlPanelBorder != null)
            {
                _controlPanelBorder.IsVisible = ShowControls;
                if (ControlPanelBackground != null)
                {
                    _controlPanelBorder.Background = ControlPanelBackground;
                }
            }
            if (_videoBorder != null)
            {
                _videoBorder.Background = VideoBackground; // Can be null for transparency
            }
            // Apply VideoStretch to renderer
            if (_videoRenderer is CpuVideoRenderer cpuRenderer)
            {
                cpuRenderer.Stretch = VideoStretch;
            }
            else if (_videoRenderer is OpenGLVideoRenderer glRenderer)
            {
                glRenderer.Stretch = VideoStretch;
            }
            
            // Load source if set (Open() handles AutoPlay internally)
            var initialMedia = Media ?? (!string.IsNullOrWhiteSpace(Source)
                ? MediaSource.FromLocation(Source)
                : null);
            if (initialMedia is not null)
                _ = OpenFromPropertyAsync(initialMedia);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoPlayerControl] Failed to initialize FFmpeg: {ex.Message}");
            MediaFailed?.Invoke(this, new MediaFailedEventArgs(new MediaError(
                MediaErrorCode.NativeLibraryUnavailable,
                "FFmpeg or its audio output could not be initialized.",
                Exception: ex)));
        }
    }

    private void SetupVideoRenderer()
    {
        if (_videoBorder == null) return;
        _videoBorder.MinWidth = 0;

        // Dispose old renderer
        if (_videoRenderer != null)
        {
            _videoRenderer.Dispose();
            _videoRenderer = null;
        }

        // Remove old renderer from visual tree
        _videoBorder.Child = null;

        // Create new renderer based on mode
        Control? rendererControl = null;
        try
        {
            switch (RenderingMode)
            {
                case VideoRenderingMode.Cpu:
                    _videoRenderer = new CpuVideoRenderer();
                    rendererControl = (Control)_videoRenderer;
                    break;

                case VideoRenderingMode.OpenGL:
                    // Try to create OpenGL renderer, fallback to CPU if not available
                    try
                    {
                        _videoRenderer = new OpenGLVideoRenderer();
                        rendererControl = (Control)_videoRenderer;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VideoPlayerControl] OpenGL renderer not available, falling back to CPU: {ex.Message}");
                        _videoRenderer = new CpuVideoRenderer();
                        rendererControl = (Control)_videoRenderer;
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoPlayerControl] Failed to create renderer: {ex.Message}");
            // Fallback to CPU renderer
            _videoRenderer = new CpuVideoRenderer();
            rendererControl = (Control)_videoRenderer;
        }

        if (rendererControl != null)
        {
            rendererControl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            rendererControl.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            
            _videoBorder.Child = rendererControl;
            
            // Apply VideoStretch to the renderer (cast to concrete type to access Stretch property)
            if (_videoRenderer is CpuVideoRenderer cpuRenderer)
            {
                cpuRenderer.Stretch = VideoStretch;
            }
            else if (_videoRenderer is OpenGLVideoRenderer glRenderer)
            {
                glRenderer.Stretch = VideoStretch;
            }
        }
    }

    private void OnFrameReady(object? sender, FrameEventArgs e)
    {
        // Note: This is already called on the UI thread via Dispatcher.UIThread.Post in FFmpegMediaPlayer
        try
        {
            if (_videoRenderer != null)
            {
                // Use the renderer interface
                _videoRenderer.RenderFrame(e.Data, e.Width, e.Height, e.Stride);
            }
            else
            {
                Debug.WriteLine("[VideoPlayerControl] Warning: No video renderer available, frame dropped.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoPlayerControl] Frame render error: {ex.Message}");
        }
        finally
        {
            e.Dispose();
        }
    }

    /// <summary>Opens a local path, direct URI, or supported provider URL.</summary>
    /// <remarks>
    /// This compatibility method is synchronous. Bind <see cref="Source"/> or use
    /// <see cref="OpenAsync(MediaSource, MediaOpenOptions?, CancellationToken)"/>
    /// to keep the UI responsive for network sources.
    /// </remarks>
    public void Open(string path) => Open(MediaSource.FromLocation(path));

    /// <summary>Synchronously opens a typed source.</summary>
    public MediaOpenResult Open(MediaSource source, MediaOpenOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsurePlayerInitialized();
        if (_mediaPlayer is null)
            return CreateInitializationFailure();

        CancelControlOpen();
        Volatile.Write(ref _requestedSource, source);
        _hasMediaLoaded = false;
        MediaOpening?.Invoke(this, new MediaOpeningEventArgs(source));
        var result = _mediaPlayer.Open(source, options);
        if (result.Succeeded && result.MediaInfo is not null)
            CompleteMediaOpen(source, result.MediaInfo);
        return result;
    }

    /// <summary>Asynchronously resolves, opens and optionally plays a typed source.</summary>
    public async Task<MediaOpenResult> OpenAsync(
        MediaSource source,
        MediaOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsurePlayerInitialized();
        if (_mediaPlayer is null)
            return CreateInitializationFailure();

        var generation = Interlocked.Increment(ref _openGeneration);
        Volatile.Write(ref _requestedSource, source);
        var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previousCancellation = Interlocked.Exchange(ref _sourceOpenCancellation, operationCancellation);
        TryCancel(previousCancellation);

        _hasMediaLoaded = false;
        MediaOpening?.Invoke(this, new MediaOpeningEventArgs(source));
        try
        {
            var result = await _mediaPlayer
                .OpenAsync(source, options, operationCancellation.Token)
                .ConfigureAwait(false);

            if (generation == Volatile.Read(ref _openGeneration)
                && result.Succeeded
                && result.MediaInfo is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation == Volatile.Read(ref _openGeneration) &&
                        ReferenceEquals(source, Volatile.Read(ref _requestedSource)))
                    {
                        CompleteMediaOpen(source, result.MediaInfo);
                    }
                });
            }

            return result;
        }
        finally
        {
            Interlocked.CompareExchange(ref _sourceOpenCancellation, null, operationCancellation);
            operationCancellation.Dispose();
        }
    }

    /// <summary>Asynchronously opens a local path, URI, or supported provider URL.</summary>
    public Task<MediaOpenResult> OpenAsync(
        string source,
        MediaOpenOptions? options = null,
        CancellationToken cancellationToken = default) =>
        OpenAsync(MediaSource.FromLocation(source), options, cancellationToken);

    private async Task OpenFromPropertyAsync(MediaSource source)
    {
        try
        {
            await OpenAsync(source).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Source replacement and visual-tree detach are expected cancellation paths.
        }
        catch (Exception ex)
        {
            var error = new MediaError(
                MediaErrorCode.PlaybackFailed,
                $"Could not open '{source.DisplayName}'.",
                Exception: ex);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(source, Volatile.Read(ref _requestedSource)))
                    MediaFailed?.Invoke(this, new MediaFailedEventArgs(error, source));
            });
        }
    }

    private void CompleteMediaOpen(MediaSource source, MediaInfo mediaInfo)
    {
        if (_mediaPlayer is null)
            return;

        _currentMediaPath = source.DisplayName;
        _hasMediaLoaded = true;
        MediaOpened?.Invoke(this, new MediaOpenedEventArgs(source, mediaInfo));

        if (mediaInfo.HasVideo)
        {
            SetupVideoRenderer();
            _mediaPlayer.DecodeFirstFrame();
        }
        else
        {
            ShowAudioOnlyPlaceholder();
        }

        if (AutoPlay)
            _mediaPlayer.Play();
    }

    private void EnsurePlayerInitialized()
    {
        if (!_isInitialized)
            InitializePlayer();
    }

    private MediaOpenResult CreateInitializationFailure()
    {
        var error = new MediaError(
            MediaErrorCode.NativeLibraryUnavailable,
            "The FFmpeg media player is not initialized.");
        MediaFailed?.Invoke(this, new MediaFailedEventArgs(error));
        return MediaOpenResult.Failure(error);
    }

    /// <summary>
    /// Opens and optionally plays a media from a URI.
    /// </summary>
    /// <param name="uri">The URI of the media.</param>
    public void OpenUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Open(uri.IsFile ? MediaSource.FromPath(uri.LocalPath) : MediaSource.FromUri(uri));
    }

    /// <summary>
    /// Starts or resumes playback.
    /// </summary>
    public void Play()
    {
        _mediaPlayer?.Play();
    }

    /// <summary>
    /// Pauses playback.
    /// </summary>
    public void Pause()
    {
        _mediaPlayer?.Pause();
    }

    /// <summary>
    /// Stops playback.
    /// </summary>
    public void Stop()
    {
        CancelControlOpen();
        _mediaPlayer?.Stop();
        if (_seekBar != null) _seekBar.Value = 0;
        if (_currentTimeText != null) _currentTimeText.Text = "00:00";
        // Clear the renderer to show background when stopped
        _videoRenderer?.Clear();
    }

    /// <summary>Cancels pending resolution and releases the current media session.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        CancelControlOpen();
        var player = _mediaPlayer;
        if (player is null)
            return;

        try
        {
            await player.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _currentMediaPath = null;
            _hasMediaLoaded = false;
            if (_seekBar != null) _seekBar.Value = 0;
            if (_currentTimeText != null) _currentTimeText.Text = "00:00";
            if (_totalTimeText != null) _totalTimeText.Text = "00:00";
            _videoRenderer?.Clear();
        });
    }

    private void CancelControlOpen()
    {
        Interlocked.Increment(ref _openGeneration);
        Volatile.Write(ref _requestedSource, null);
        var cancellation = Interlocked.Exchange(ref _sourceOpenCancellation, null);
        TryCancel(cancellation);
        _mediaPlayer?.CancelPendingOpen();
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The owning open completed between the atomic exchange and cancellation.
        }
    }

    /// <summary>
    /// Steps forward exactly one frame. Pauses playback after displaying the frame.
    /// </summary>
    /// <returns>True if a frame was successfully decoded and displayed, false otherwise.</returns>
    public bool StepForward()
    {
        return _mediaPlayer?.StepForward() ?? false;
    }

    /// <summary>
    /// Steps backward one frame. Uses cached frames when available, otherwise seeks to previous keyframe and decodes forward.
    /// </summary>
    /// <returns>True if a frame was successfully displayed, false otherwise.</returns>
    public bool StepBackward()
    {
        return _mediaPlayer?.StepBackward() ?? false;
    }

    /// <summary>
    /// Toggles between play and pause.
    /// </summary>
    public void TogglePlayPause()
    {
        if (_mediaPlayer == null) return;

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
        }
    }

    /// <summary>
    /// Seeks to a specific position.
    /// </summary>
    /// <param name="positionPercent">Position as a percentage (0.0 to 1.0).</param>
    public void Seek(float positionPercent)
    {
        _mediaPlayer?.Seek(positionPercent);
    }

    /// <summary>
    /// Toggles mute state.
    /// </summary>
    public void ToggleMute()
    {
        OnMuteClick(null, null!);
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        UpdatePlayPauseButton(true);
        PlaybackStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnCoreMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        var requestedSource = Volatile.Read(ref _requestedSource);
        if (e.Source is not null && !ReferenceEquals(e.Source, requestedSource))
            return;

        _hasMediaLoaded = false;
        MediaFailed?.Invoke(this, e);
    }

    private void OnCoreStateChanged(object? sender, PlaybackStateChangedEventArgs e) =>
        PlaybackStateChanged?.Invoke(this, e);

    private void ShowAudioOnlyPlaceholder()
    {
        if (_videoBorder == null)
            return;

        _videoRenderer?.Dispose();
        _videoRenderer = null;
        _videoBorder.MinWidth = 520;

        var fileName = !string.IsNullOrWhiteSpace(_currentMediaPath)
            ? System.IO.Path.GetFileName(_currentMediaPath)
            : "Audio";

        _videoBorder.Child = new Grid
        {
            MinWidth = 520,
            MinHeight = 160,
            Children =
            {
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(fileName) ? "Audio" : fileName,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24, 24, 24, 56),
                    FontSize = 18,
                    Foreground = ControlForeground ?? Brushes.White
                }
            }
        };
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        UpdatePlayPauseButton(false);
        PlaybackPaused?.Invoke(this, EventArgs.Empty);
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        UpdatePlayPauseButton(false);
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
        // Clear the renderer to show background when stopped
        _videoRenderer?.Clear();
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdatePlayPauseButton(false);

            // Set seek bar to end and show total time
            if (_seekBar != null) _seekBar.Value = 100;
            if (_currentTimeText != null && _totalTimeText != null)
                _currentTimeText.Text = _totalTimeText.Text;

            MediaEnded?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        if (_isDraggingSeekBar || _mediaPlayer == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_seekBar != null)
            {
                _seekBar.Value = e.Position * 100;
            }

            if (_currentTimeText != null && _mediaPlayer.Length > 0)
            {
                var currentTime = TimeSpan.FromMilliseconds(_mediaPlayer.Length * e.Position);
                _currentTimeText.Text = FormatTime(currentTime);
            }
        });
    }

    private void OnLengthChanged(object? sender, LengthChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_totalTimeText != null)
            {
                var totalTime = TimeSpan.FromMilliseconds(e.Length);
                _totalTimeText.Text = FormatTime(totalTime);
            }
        });
    }

    private void OnSeekBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _isDraggingSeekBar = true;
    }

    private void OnSeekBarPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_isDraggingSeekBar)
        {
            _isDraggingSeekBar = false;
            SeekToCurrentSliderPosition();
        }
    }

    private void OnSeekBarPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (_isDraggingSeekBar)
        {
            _isDraggingSeekBar = false;
            SeekToCurrentSliderPosition();
        }
    }

    private void SeekToCurrentSliderPosition()
    {
        if (_mediaPlayer == null || _seekBar == null) return;

        var position = (float)(_seekBar.Value / 100);
        _mediaPlayer.Seek(position);

        // If paused or stopped, decode and show the frame at the new position
        if (!_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.ShowFrameAtCurrentPosition();
        }
    }

    private void OnVolumeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Volume = (int)e.NewValue;
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Video File",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Video Files") { Patterns = new[] { "*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv", "*.flv", "*.webm", "*.m4v", "*.ts" } },
                    new("Audio Files") { Patterns = new[] { "*.mp3", "*.wav", "*.flac", "*.aac", "*.ogg", "*.m4a" } },
                    new("All Files") { Patterns = new[] { "*" } }
                }
            });

            if (files.Count > 0)
            {
                var file = files[0];
                var path = file.Path.LocalPath;
                Debug.WriteLine($"[VideoPlayerControl] File selected from picker: {path}");
                Open(path);
                Debug.WriteLine($"[VideoPlayerControl] After Open: _hasMediaLoaded={_hasMediaLoaded}, IsPlaying={_mediaPlayer?.IsPlaying}");
                if (_hasMediaLoaded && _mediaPlayer != null && !_mediaPlayer.IsPlaying)
                {
                    Debug.WriteLine("[VideoPlayerControl] Calling Play after successful open");
                    _mediaPlayer.Play();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoPlayerControl] Error opening file: {ex}");
        }
    }

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        Stop();
    }

    private void OnMuteClick(object? sender, RoutedEventArgs e)
    {
        if (_mediaPlayer == null || _volumeIcon == null) return;

        _isMuted = !_isMuted;

        if (_isMuted)
        {
            _previousVolume = _mediaPlayer.Volume;
            _mediaPlayer.Volume = 0;
            if (_volumeSlider != null) _volumeSlider.Value = 0;
            _volumeIcon.Data = GetIconProvider().CreateVolumeOffIcon();
        }
        else
        {
            _mediaPlayer.Volume = _previousVolume;
            if (_volumeSlider != null) _volumeSlider.Value = _previousVolume;
            _volumeIcon.Data = GetIconProvider().CreateVolumeHighIcon();
        }
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e)
    {
        IsFullscreen = !IsFullscreen;
        UpdateFullscreenIcon();
        FullscreenToggle?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateFullscreenIcon()
    {
        var icon = this.FindControl<Path>("FullscreenIcon");
        if (icon != null)
        {
            var provider = GetIconProvider();
            icon.Data = IsFullscreen
                ? provider.CreateFullscreenExitIcon()
                : provider.CreateFullscreenIcon();
        }
    }

    private void UpdatePlayPauseButton(bool isPlaying)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var iconProvider = GetIconProvider();
            if (_playPauseIcon != null)
            {
                _playPauseIcon.Data = isPlaying ? iconProvider.CreatePauseIcon() : iconProvider.CreatePlayIcon();
            }
            if (_playPauseText != null)
            {
                _playPauseText.Text = isPlaying ? "Pause" : "Play";
            }
        });
    }

    private IIconProvider GetIconProvider()
    {
        return IconProvider ?? DefaultIconProvider.Instance;
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.Hours > 0
            ? $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    private void Cleanup()
    {
        CancelControlOpen();
        var mediaPlayer = Interlocked.Exchange(ref _mediaPlayer, null);
        if (mediaPlayer != null)
        {
            mediaPlayer.PositionChanged -= OnPositionChanged;
            mediaPlayer.LengthChanged -= OnLengthChanged;
            mediaPlayer.Playing -= OnPlaying;
            mediaPlayer.Paused -= OnPaused;
            mediaPlayer.Stopped -= OnStopped;
            mediaPlayer.EndReached -= OnEndReached;
            mediaPlayer.FrameReady -= OnFrameReady;
            mediaPlayer.MediaFailed -= OnCoreMediaFailed;
            mediaPlayer.StateChanged -= OnCoreStateChanged;

            // Source providers are extensible and may take time to observe
            // cancellation. Never block Avalonia's visual-tree detach path.
            _ = DisposeDetachedPlayerAsync(mediaPlayer);
        }
        
        if (_videoRenderer != null)
        {
            _videoRenderer.Dispose();
            _videoRenderer = null;
        }
        
        _isInitialized = false;
        _currentMediaPath = null;
        _hasMediaLoaded = false;
    }

    private static Task DisposeDetachedPlayerAsync(FFmpegMediaPlayer mediaPlayer) =>
        Task.Run(async () =>
        {
            try
            {
                await mediaPlayer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VideoPlayerControl] Background cleanup failed: {ex.Message}");
            }
        });
}

/// <summary>
/// Provides data for the MediaOpened event.
/// </summary>
public sealed class MediaOpenedEventArgs : EventArgs
{
    public MediaOpenedEventArgs(string path)
    {
        Path = path;
    }

    public MediaOpenedEventArgs(MediaSource source, MediaInfo mediaInfo)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        MediaInfo = mediaInfo ?? throw new ArgumentNullException(nameof(mediaInfo));
        Path = source.DisplayName;
    }

    /// <summary>
    /// Gets the full path of the media that was opened.
    /// </summary>
    public string Path { get; }

    /// <summary>Gets the typed source when opened through the standalone API.</summary>
    public MediaSource? Source { get; }

    /// <summary>Gets discovered stream information.</summary>
    public MediaInfo? MediaInfo { get; }
}

/// <summary>Provides data for the MediaOpening event.</summary>
public sealed class MediaOpeningEventArgs : EventArgs
{
    public MediaOpeningEventArgs(MediaSource source) =>
        Source = source ?? throw new ArgumentNullException(nameof(source));

    public MediaSource Source { get; }
}

/// <summary>
/// Interface for providing custom icon geometries.
/// Implement this interface to provide custom icons for the video player control.
/// </summary>
public interface IIconProvider
{
    /// <summary>
    /// Creates a play icon geometry (triangle pointing right).
    /// </summary>
    Geometry CreatePlayIcon();

    /// <summary>
    /// Creates a pause icon geometry (two vertical bars).
    /// </summary>
    Geometry CreatePauseIcon();

    /// <summary>
    /// Creates a stop icon geometry (square).
    /// </summary>
    Geometry CreateStopIcon();

    /// <summary>
    /// Creates a folder open icon geometry.
    /// </summary>
    Geometry CreateFolderOpenIcon();

    /// <summary>
    /// Creates a volume high icon geometry (speaker with sound waves).
    /// </summary>
    Geometry CreateVolumeHighIcon();

    /// <summary>
    /// Creates a volume off icon geometry (speaker with X).
    /// </summary>
    Geometry CreateVolumeOffIcon();

    /// <summary>
    /// Creates a fullscreen icon geometry (expand arrows).
    /// </summary>
    Geometry CreateFullscreenIcon() => Geometry.Parse("M 5,5 L 5,10 L 7,10 L 7,7 L 10,7 L 10,5 Z M 14,5 L 14,7 L 17,7 L 17,10 L 19,10 L 19,5 Z M 7,14 L 5,14 L 5,19 L 10,19 L 10,17 L 7,17 Z M 17,17 L 14,17 L 14,19 L 19,19 L 19,14 L 17,14 Z");

    /// <summary>
    /// Creates a fullscreen exit icon geometry (collapse arrows).
    /// </summary>
    Geometry CreateFullscreenExitIcon() => Geometry.Parse("M 14,14 L 14,19 L 16,19 L 16,16 L 19,16 L 19,14 Z M 5,14 L 5,16 L 8,16 L 8,19 L 10,19 L 10,14 Z M 16,5 L 14,5 L 14,10 L 19,10 L 19,8 L 16,8 Z M 8,8 L 5,8 L 5,10 L 10,10 L 10,5 L 8,5 Z");
}

/// <summary>
/// Default icon provider using standard Avalonia shapes.
/// </summary>
public sealed class DefaultIconProvider : IIconProvider
{
    /// <summary>
    /// Gets the singleton instance of the default icon provider.
    /// </summary>
    public static DefaultIconProvider Instance { get; } = new DefaultIconProvider();

    private DefaultIconProvider() { }

    /// <summary>
    /// Creates a play icon geometry (triangle pointing right).
    /// </summary>
    public Geometry CreatePlayIcon()
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(6, 4) };
        figure.Segments!.Add(new LineSegment { Point = new Point(6, 16) });
        figure.Segments!.Add(new LineSegment { Point = new Point(18, 10) });
        figure.Segments!.Add(new LineSegment { Point = new Point(6, 4) });
        geometry.Figures!.Add(figure);
        return geometry;
    }

    /// <summary>
    /// Creates a pause icon geometry (two vertical bars).
    /// </summary>
    public Geometry CreatePauseIcon()
    {
        var geometry = new PathGeometry();
        
        // Left bar
        var leftFigure = new PathFigure { StartPoint = new Point(6, 4) };
        leftFigure.Segments!.Add(new LineSegment { Point = new Point(10, 4) });
        leftFigure.Segments!.Add(new LineSegment { Point = new Point(10, 16) });
        leftFigure.Segments!.Add(new LineSegment { Point = new Point(6, 16) });
        leftFigure.IsClosed = true;
        geometry.Figures!.Add(leftFigure);
        
        // Right bar
        var rightFigure = new PathFigure { StartPoint = new Point(14, 4) };
        rightFigure.Segments!.Add(new LineSegment { Point = new Point(18, 4) });
        rightFigure.Segments!.Add(new LineSegment { Point = new Point(18, 16) });
        rightFigure.Segments!.Add(new LineSegment { Point = new Point(14, 16) });
        rightFigure.IsClosed = true;
        geometry.Figures!.Add(rightFigure);
        
        return geometry;
    }

    /// <summary>
    /// Creates a stop icon geometry (square).
    /// </summary>
    public Geometry CreateStopIcon()
    {
        return new RectangleGeometry { Rect = new Rect(6, 6, 12, 12) };
    }

    /// <summary>
    /// Creates a folder open icon geometry.
    /// </summary>
    public Geometry CreateFolderOpenIcon()
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(4, 6) };
        figure.Segments!.Add(new LineSegment { Point = new Point(4, 8) });
        figure.Segments!.Add(new LineSegment { Point = new Point(6, 8) });
        figure.Segments!.Add(new LineSegment { Point = new Point(8, 6) });
        figure.Segments!.Add(new LineSegment { Point = new Point(16, 6) });
        figure.Segments!.Add(new LineSegment { Point = new Point(18, 8) });
        figure.Segments!.Add(new LineSegment { Point = new Point(18, 16) });
        figure.Segments!.Add(new LineSegment { Point = new Point(4, 16) });
        figure.Segments!.Add(new LineSegment { Point = new Point(4, 6) });
        geometry.Figures!.Add(figure);
        
        // Add open flap
        var flap = new PathFigure { StartPoint = new Point(4, 8) };
        flap.Segments!.Add(new LineSegment { Point = new Point(6, 10) });
        flap.Segments!.Add(new LineSegment { Point = new Point(18, 10) });
        flap.Segments!.Add(new LineSegment { Point = new Point(18, 8) });
        geometry.Figures!.Add(flap);
        
        return geometry;
    }

    /// <summary>
    /// Creates a volume high icon geometry (speaker with sound waves).
    /// </summary>
    public Geometry CreateVolumeHighIcon()
    {
        var geometry = new PathGeometry();
        
        // Speaker cone
        var speaker = new PathFigure { StartPoint = new Point(5, 6) };
        speaker.Segments!.Add(new LineSegment { Point = new Point(5, 14) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(9, 14) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(13, 18) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(13, 2) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(9, 6) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(5, 6) });
        geometry.Figures!.Add(speaker);
        
        // Sound waves
        var wave1 = new PathFigure { StartPoint = new Point(15, 8) };
        wave1.Segments!.Add(new ArcSegment { Point = new Point(15, 12), Size = new Size(2, 2), SweepDirection = SweepDirection.Clockwise });
        geometry.Figures!.Add(wave1);
        
        var wave2 = new PathFigure { StartPoint = new Point(17, 6) };
        wave2.Segments!.Add(new ArcSegment { Point = new Point(17, 14), Size = new Size(3, 3), SweepDirection = SweepDirection.Clockwise });
        geometry.Figures!.Add(wave2);
        
        return geometry;
    }

    /// <summary>
    /// Creates a volume off icon geometry (speaker with X).
    /// </summary>
    public Geometry CreateVolumeOffIcon()
    {
        var geometry = new PathGeometry();
        
        // Speaker cone
        var speaker = new PathFigure { StartPoint = new Point(5, 6) };
        speaker.Segments!.Add(new LineSegment { Point = new Point(5, 14) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(9, 14) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(13, 18) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(13, 2) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(9, 6) });
        speaker.Segments!.Add(new LineSegment { Point = new Point(5, 6) });
        geometry.Figures!.Add(speaker);
        
        // X mark
        var x1 = new PathFigure { StartPoint = new Point(15, 5) };
        x1.Segments!.Add(new LineSegment { Point = new Point(19, 9) });
        geometry.Figures!.Add(x1);
        
        var x2 = new PathFigure { StartPoint = new Point(15, 9) };
        x2.Segments!.Add(new LineSegment { Point = new Point(19, 5) });
        geometry.Figures!.Add(x2);
        
        return geometry;
    }
}
