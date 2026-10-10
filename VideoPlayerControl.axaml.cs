using System;
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
    /// Legacy OpenGL mode, currently using CPU bitmap rendering.
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
    private static int _nextPlayerId;
    private readonly int _playerId = Interlocked.Increment(ref _nextPlayerId);
    private MediaInfo? _diagnosticMediaInfo;
    private FFmpegMediaPlayer? _mediaPlayer;
    private Task<Exception?> _detachedDisposal = Task.FromResult<Exception?>(null);
    private bool _isDraggingSeekBar;
    private bool _isMuted;
    private int _previousVolume = 100;
    private bool _isInitialized;
    private IVideoRenderer? _videoRenderer;
    private string? _currentMediaPath;
    private bool _hasMediaLoaded;
    private CancellationTokenSource? _sourceOpenCancellation;
    private long _openGeneration;
    private MediaSource? _requestedSource;
    private readonly FirstFramePresentationTracker _firstFramePresentation = new();

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
            if (VolumeSlider != null)
            {
                VolumeSlider.Value = value;
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
        set => SetValue(ShowControlsProperty, value);
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
    /// If null, the default OpenTK/OpenAL audio output is used.
    /// </summary>
    public static readonly StyledProperty<Func<int, int, IAudioPlayer?>?> AudioPlayerFactoryProperty =
        AvaloniaProperty.Register<VideoPlayerControl, Func<int, int, IAudioPlayer?>?>(nameof(AudioPlayerFactory), null);

    /// <summary>
    /// Gets or sets the factory function for creating audio players.
    /// Signature: (sampleRate, channels) => IAudioPlayer?
    /// A null property uses the default OpenTK/OpenAL output. A custom factory
    /// returning null disables audio output. Configure this before the native
    /// player is initialized; changes apply after detach and reinitialization.
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
    /// OpenGL: Uses the legacy renderer with CPU bitmap rendering.
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

    /// <summary>
    /// Occurs once after the first video frame for the active source has been rendered successfully.
    /// </summary>
    public event EventHandler? FirstFramePresented;

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

    /// <summary>Occurs when an optional lifecycle or audio-output diagnostic is available.</summary>
    /// <remarks>
    /// Callbacks may run on a worker thread. Observer exceptions are isolated from
    /// playback. The library does not automatically persist diagnostics; sinks must
    /// redact any source details contained in media information or exceptions.
    /// </remarks>
    public event EventHandler<VideoPlayerDiagnosticEventArgs>? Diagnostic;

    /// <summary>
    /// Creates a new instance of the VideoPlayerControl.
    /// </summary>
    public VideoPlayerControl()
    {
        InitializeComponent();

        // Enable focus so we can receive keyboard events
        Focusable = true;

        // Icons must be ready for the first render, before any playback event.
        ApplyControlIcons();

        // Apply initial visibility based on properties
        if (ControlPanelBorder != null)
        {
            ControlPanelBorder.IsVisible = ShowControls;
        }
        if (OpenButton != null)
        {
            OpenButton.IsVisible = ShowOpenButton;
        }

        // Setup seek bar events
        if (SeekBar != null)
        {
            SeekBar.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnSeekBarPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            SeekBar.AddHandler(InputElement.PointerReleasedEvent, OnSeekBarDragCompleted, RoutingStrategies.Tunnel);
            SeekBar.AddHandler(InputElement.PointerCaptureLostEvent, OnSeekBarDragCompleted, RoutingStrategies.Tunnel);
        }

        // Setup volume slider
        if (VolumeSlider != null)
        {
            VolumeSlider.ValueChanged += OnVolumeChanged;
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
            case Key.Right:
                if (_mediaPlayer != null && _mediaPlayer.Length > 0)
                {
                    // Seek 5 seconds, or 30 seconds with Ctrl.
                    double currentPos = _mediaPlayer.Position * _mediaPlayer.Length; // ms
                    double jump = isCtrl ? 30000 : 5000; // ms
                    double newPos = e.Key == Key.Left
                        ? Math.Max(0, currentPos - jump)
                        : Math.Min(_mediaPlayer.Length, currentPos + jump);
                    Seek((float)(newPos / _mediaPlayer.Length));
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
            if (OpenButton != null)
            {
                OpenButton.IsVisible = (bool)(e.NewValue ?? true);
            }
        }
        else if (e.Property == ControlPanelBackgroundProperty)
        {
            if (ControlPanelBorder != null && e.NewValue is global::Avalonia.Media.IBrush brush)
            {
                ControlPanelBorder.Background = brush;
            }
        }
        else if (e.Property == VideoBackgroundProperty)
        {
            if (VideoBorder != null)
            {
                VideoBorder.Background = e.NewValue as global::Avalonia.Media.IBrush; // Can be null for transparency
            }
        }
        else if (e.Property == VideoStretchProperty)
        {
            if (e.NewValue is global::Avalonia.Media.Stretch rendererStretch)
                ApplyVideoStretch(rendererStretch);
        }
        else if (e.Property == ShowControlsProperty)
        {
            if (ControlPanelBorder != null)
            {
                ControlPanelBorder.IsVisible = (bool)(e.NewValue ?? true);
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
        else if (e.Property == IconProviderProperty)
        {
            ApplyControlIcons();
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
            PlayPauseIcon,
            this.FindControl<Path>("StopIcon"),
            VolumeIcon,
            this.FindControl<Path>("FullscreenIcon")
        };
        foreach (var icon in icons)
        {
            if (icon != null) icon.Fill = brush;
        }
        // Apply to time text
        if (CurrentTimeText != null) CurrentTimeText.Foreground = brush;
        if (TotalTimeText != null) TotalTimeText.Foreground = brush;
    }

    private void ApplyButtonBackground(global::Avalonia.Media.IBrush? brush)
    {
        if (brush == null) return;
        var buttons = new[] {
            OpenButton,
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
        // Styles can supply a provider and brushes when the control is attached.
        // Apply them independently of native media initialization.
        ApplyControlIcons();
        ApplyControlForeground(ControlForeground);
        InitializePlayer();
    }

    private void ApplyControlIcons()
    {
        var provider = GetIconProvider();
        var folderIcon = this.FindControl<Path>("FolderIcon");
        if (folderIcon != null) folderIcon.Data = provider.CreateFolderOpenIcon();
        var stopIcon = this.FindControl<Path>("StopIcon");
        if (stopIcon != null) stopIcon.Data = provider.CreateStopIcon();
        if (PlayPauseIcon != null)
            PlayPauseIcon.Data = IsPlaying ? provider.CreatePauseIcon() : provider.CreatePlayIcon();
        if (VolumeIcon != null)
            VolumeIcon.Data = _isMuted ? provider.CreateVolumeOffIcon() : provider.CreateVolumeHighIcon();
        UpdateFullscreenIcon();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ReportDiagnostic(VideoPlayerDiagnosticKind.Detached);
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

            // Capture the configured factory for this native player, matching the
            // existing lifetime of AudioPlayerFactory. The wrapper keeps audio
            // failures nonfatal and exposes them to optional diagnostics observers.
            var configuredAudioFactory = AudioPlayerFactory;
            Func<int, int, IAudioPlayer?> effectiveAudioFactory =
                (sampleRate, channels) => CreateAudioPlayer(sampleRate, channels, configuredAudioFactory);

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
            if (OpenButton != null)
            {
                OpenButton.IsVisible = ShowOpenButton;
            }
            var fullscreenBtn = this.FindControl<Button>("FullscreenButton");
            if (fullscreenBtn != null)
            {
                fullscreenBtn.IsVisible = ShowFullscreenButton;
            }
            if (ControlPanelBorder != null)
            {
                ControlPanelBorder.IsVisible = ShowControls;
                if (ControlPanelBackground != null)
                {
                    ControlPanelBorder.Background = ControlPanelBackground;
                }
            }
            if (VideoBorder != null)
            {
                VideoBorder.Background = VideoBackground; // Can be null for transparency
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
            var error = new MediaError(
                MediaErrorCode.NativeLibraryUnavailable,
                "FFmpeg or its audio output could not be initialized.",
                Exception: ex);
            ReportDiagnostic(VideoPlayerDiagnosticKind.Failed, error: error, exception: ex);
            MediaFailed?.Invoke(this, new MediaFailedEventArgs(error));
        }
    }

    private IAudioPlayer? CreateAudioPlayer(
        int sampleRate,
        int channels,
        Func<int, int, IAudioPlayer?>? factory)
    {
        try
        {
            var player = factory is null
                ? new global::FFmpegVideoPlayer.Audio.OpenTK.OpenTKAudioPlayer(sampleRate, channels)
                : factory(sampleRate, channels);

            if (player is null)
            {
                ReportDiagnostic(VideoPlayerDiagnosticKind.AudioOutputFailed,
                    error: new MediaError(MediaErrorCode.AudioOutputUnavailable,
                        "The audio output factory returned no audio player."),
                    sampleRate: sampleRate, channels: channels);
            }
            else
            {
                ReportDiagnostic(VideoPlayerDiagnosticKind.AudioOutputReady,
                    sampleRate: sampleRate, channels: channels);
            }

            return player;
        }
        catch (Exception ex)
        {
            ReportDiagnostic(VideoPlayerDiagnosticKind.AudioOutputFailed,
                error: new MediaError(MediaErrorCode.AudioOutputUnavailable,
                    "The audio output could not be initialized.", Exception: ex),
                exception: ex, sampleRate: sampleRate, channels: channels);
            return null;
        }
    }

    private void ReportDiagnostic(
        VideoPlayerDiagnosticKind kind,
        MediaInfo? mediaInfo = null,
        MediaError? error = null,
        Exception? exception = null,
        int? sampleRate = null,
        int? channels = null)
    {
        var observers = Diagnostic;
        if (observers is null)
            return;

        // Hold one native-player reference while visual-tree detach may clear it.
        var player = Volatile.Read(ref _mediaPlayer);
        var duration = player?.Length ?? 0;
        var position = player is null ? 0 : (long)(player.Position * duration);
        var diagnostic = new VideoPlayerDiagnosticEventArgs(
            kind, _playerId, position, duration,
            mediaInfo ?? _diagnosticMediaInfo, error, exception ?? error?.Exception,
            sampleRate, channels);

        foreach (EventHandler<VideoPlayerDiagnosticEventArgs> observer in observers.GetInvocationList())
        {
            try
            {
                observer(this, diagnostic);
            }
            catch (Exception)
            {
                // Diagnostics must neither interrupt playback nor prevent other
                // observers from receiving the same snapshot.
            }
        }
    }

    private void SetupVideoRenderer()
    {
        if (VideoBorder == null) return;
        VideoBorder.MinWidth = 0;

        _videoRenderer?.Dispose();
        _videoRenderer = null;

        // Remove old renderer from visual tree
        VideoBorder.Child = null;

        Control? rendererControl;
        try
        {
            // OpenGL is retained as a CPU-backed compatibility renderer.
            rendererControl = RenderingMode switch
            {
                VideoRenderingMode.Cpu => new CpuVideoRenderer(),
                VideoRenderingMode.OpenGL => new OpenGLVideoRenderer(),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoPlayerControl] Failed to create renderer: {ex.Message}");
            rendererControl = new CpuVideoRenderer();
        }

        _videoRenderer = rendererControl as IVideoRenderer;
        if (rendererControl != null)
        {
            rendererControl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            rendererControl.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            
            VideoBorder.Child = rendererControl;
            
            ApplyVideoStretch(VideoStretch);
        }
    }

    private void ApplyVideoStretch(Stretch stretch)
    {
        if (_videoRenderer is CpuVideoRenderer cpuRenderer)
            cpuRenderer.Stretch = stretch;
        else if (_videoRenderer is OpenGLVideoRenderer glRenderer)
            glRenderer.Stretch = stretch;
    }

    private void OnFrameReady(object? sender, FrameEventArgs e)
    {
        // Note: This is already called on the UI thread via Dispatcher.UIThread.Post in FFmpegMediaPlayer
        var openGeneration = Volatile.Read(ref _openGeneration);
        var mediaGeneration = e.MediaGeneration;
        var timelineGeneration = e.TimelineGeneration;
        if (sender is not FFmpegMediaPlayer mediaPlayer
            || !ReferenceEquals(mediaPlayer, Volatile.Read(ref _mediaPlayer))
            || mediaGeneration != mediaPlayer.MediaGeneration
            || timelineGeneration != mediaPlayer.TimelineGeneration)
        {
            // A frame can already be queued on the dispatcher when its source is
            // replaced or closed. Never let that stale frame flash in the new renderer.
            e.Dispose();
            return;
        }

        var rendered = false;
        try
        {
            if (_videoRenderer != null)
            {
                // Use the renderer interface
                _videoRenderer.RenderFrame(e.Data, e.Width, e.Height, e.Stride);
                rendered = true;
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

        if (rendered && _firstFramePresentation.TryPresent(
                openGeneration,
                mediaGeneration))
        {
            ReportDiagnostic(VideoPlayerDiagnosticKind.FirstFramePresented);
            if (!IsCurrentPlayer(mediaPlayer, openGeneration)
                || mediaGeneration != mediaPlayer.MediaGeneration
                || timelineGeneration != mediaPlayer.TimelineGeneration)
            {
                return;
            }
            try
            {
                FirstFramePresented?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VideoPlayerControl] FirstFramePresented subscriber failed: {ex.Message}");
            }
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
        InitializePlayer();
        var mediaPlayer = Volatile.Read(ref _mediaPlayer);
        if (mediaPlayer is null)
            return CreateInitializationFailure();

        CancelControlOpen();
        var generation = Volatile.Read(ref _openGeneration);
        Volatile.Write(ref _requestedSource, source);
        _hasMediaLoaded = false;
        _diagnosticMediaInfo = null;
        ReportDiagnostic(VideoPlayerDiagnosticKind.Opening);
        if (!IsCurrentPlayer(mediaPlayer, generation)
            || !ReferenceEquals(source, Volatile.Read(ref _requestedSource)))
            return SupersededOpenResult();
        MediaOpening?.Invoke(this, new MediaOpeningEventArgs(source));
        if (!IsCurrentPlayer(mediaPlayer, generation)
            || !ReferenceEquals(source, Volatile.Read(ref _requestedSource)))
            return SupersededOpenResult();
        var result = mediaPlayer.Open(source, options);
        if (IsCurrentSource(source, generation)
            && result.Succeeded
            && result.MediaInfo is not null)
        {
            CompleteMediaOpen(source, result.MediaInfo, generation);
        }
        return result;
    }

    /// <summary>Asynchronously resolves, opens and optionally plays a typed source.</summary>
    public async Task<MediaOpenResult> OpenAsync(
        MediaSource source,
        MediaOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        InitializePlayer();
        var mediaPlayer = Volatile.Read(ref _mediaPlayer);
        if (mediaPlayer is null)
            return CreateInitializationFailure();

        var generation = Interlocked.Increment(ref _openGeneration);
        _firstFramePresentation.Reset();
        Volatile.Write(ref _requestedSource, source);
        var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previousCancellation = Interlocked.Exchange(ref _sourceOpenCancellation, operationCancellation);
        TryCancel(previousCancellation);

        _hasMediaLoaded = false;
        _diagnosticMediaInfo = null;
        try
        {
            ReportDiagnostic(VideoPlayerDiagnosticKind.Opening);
            operationCancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentPlayer(mediaPlayer, generation)
                || !ReferenceEquals(source, Volatile.Read(ref _requestedSource)))
                return SupersededOpenResult();
            MediaOpening?.Invoke(this, new MediaOpeningEventArgs(source));
            operationCancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentPlayer(mediaPlayer, generation)
                || !ReferenceEquals(source, Volatile.Read(ref _requestedSource)))
                return SupersededOpenResult();
            var result = await mediaPlayer
                .OpenAsync(source, options, operationCancellation.Token)
                .ConfigureAwait(false);

            if (generation == Volatile.Read(ref _openGeneration)
                && result.Succeeded
                && result.MediaInfo is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsCurrentSource(source, generation))
                    {
                        CompleteMediaOpen(source, result.MediaInfo, generation);
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
                {
                    var generation = Volatile.Read(ref _openGeneration);
                    ReportDiagnostic(VideoPlayerDiagnosticKind.Failed, error: error);
                    if (IsCurrentSource(source, generation))
                        MediaFailed?.Invoke(this, new MediaFailedEventArgs(error, source));
                }
            });
        }
    }

    private void CompleteMediaOpen(MediaSource source, MediaInfo mediaInfo, long generation)
    {
        var mediaPlayer = _mediaPlayer;
        if (mediaPlayer is null || !IsCurrentSource(source, generation))
        {
            return;
        }

        _currentMediaPath = source.DisplayName;
        _hasMediaLoaded = true;
        _diagnosticMediaInfo = mediaInfo;

        if (mediaInfo.HasVideo)
        {
            SetupVideoRenderer();
            _firstFramePresentation.Activate(generation, mediaPlayer.MediaGeneration);
        }
        else
        {
            _firstFramePresentation.Reset();
            ShowAudioOnlyPlaceholder();
        }

        ReportDiagnostic(VideoPlayerDiagnosticKind.Opened, mediaInfo: mediaInfo);

        // Diagnostic observers can replace the source just like media observers.
        if (!IsCurrentSource(source, generation)
            || !ReferenceEquals(mediaPlayer, _mediaPlayer))
        {
            return;
        }

        MediaOpened?.Invoke(this, new MediaOpenedEventArgs(source, mediaInfo));

        if (!IsCurrentSource(source, generation)
            || !ReferenceEquals(mediaPlayer, _mediaPlayer))
        {
            return;
        }

        if (AutoPlay)
        {
            mediaPlayer.Play();
        }
        else if (mediaInfo.HasVideo)
        {
            mediaPlayer.DecodeFirstFrame();
        }
    }

    private bool IsCurrentPlayer(FFmpegMediaPlayer? player, long generation) =>
        player is not null
        && ReferenceEquals(player, Volatile.Read(ref _mediaPlayer))
        && generation == Volatile.Read(ref _openGeneration);

    private bool IsCurrentSource(MediaSource? source, long generation) =>
        generation == Volatile.Read(ref _openGeneration)
        && ReferenceEquals(source, Volatile.Read(ref _requestedSource));

    private static MediaOpenResult SupersededOpenResult() => MediaOpenResult.Failure(new MediaError(
        MediaErrorCode.Cancelled, "The media open was superseded by another player operation."));

    private MediaOpenResult CreateInitializationFailure()
    {
        var error = new MediaError(
            MediaErrorCode.NativeLibraryUnavailable,
            "The FFmpeg media player is not initialized.");
        ReportDiagnostic(VideoPlayerDiagnosticKind.Failed, error: error);
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
        if (SeekBar != null) SeekBar.Value = 0;
        if (CurrentTimeText != null) CurrentTimeText.Text = "00:00";
        // Clear the renderer to show background when stopped
        _videoRenderer?.Clear();
    }

    /// <summary>Cancels pending resolution and releases current and detached native media sessions.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        CancelControlOpen();
        var generation = Volatile.Read(ref _openGeneration);
        var player = Volatile.Read(ref _mediaPlayer);
        if (player is not null)
        {
            try
            {
                await player.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Detach can dispose this player while close is awaiting work.
            }
        }

        var disposalException = await Volatile.Read(ref _detachedDisposal)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        if (disposalException is not null)
            throw new InvalidOperationException("A detached media player could not be disposed.", disposalException);
        if (player is null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsCurrentPlayer(player, generation))
                return;
            _currentMediaPath = null;
            _hasMediaLoaded = false;
            _diagnosticMediaInfo = null;
            if (SeekBar != null) SeekBar.Value = 0;
            if (CurrentTimeText != null) CurrentTimeText.Text = "00:00";
            if (TotalTimeText != null) TotalTimeText.Text = "00:00";
            _videoRenderer?.Clear();
        });
    }

    private void CancelControlOpen()
    {
        Interlocked.Increment(ref _openGeneration);
        _firstFramePresentation.Reset();
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
        if (UpdatePlaybackState(sender, VideoPlayerDiagnosticKind.Playing))
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnCoreMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        if (!ReferenceEquals(sender, Volatile.Read(ref _mediaPlayer)))
            return;
        var requestedSource = Volatile.Read(ref _requestedSource);
        if (e.Source is not null && !ReferenceEquals(e.Source, requestedSource))
            return;

        _hasMediaLoaded = false;
        var generation = Volatile.Read(ref _openGeneration);
        ReportDiagnostic(VideoPlayerDiagnosticKind.Failed, error: e.Error);
        if (IsCurrentSource(requestedSource, generation))
            MediaFailed?.Invoke(this, e);
    }

    private void OnCoreStateChanged(object? sender, PlaybackStateChangedEventArgs e) =>
        PlaybackStateChanged?.Invoke(this, e);

    private void ShowAudioOnlyPlaceholder()
    {
        if (VideoBorder == null)
            return;

        _videoRenderer?.Dispose();
        _videoRenderer = null;
        VideoBorder.MinWidth = 520;

        var fileName = !string.IsNullOrWhiteSpace(_currentMediaPath)
            ? System.IO.Path.GetFileName(_currentMediaPath)
            : "Audio";

        VideoBorder.Child = new Grid
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
        if (UpdatePlaybackState(sender, VideoPlayerDiagnosticKind.Paused))
            PlaybackPaused?.Invoke(this, EventArgs.Empty);
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        if (UpdatePlaybackState(sender, VideoPlayerDiagnosticKind.Stopped))
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    private bool UpdatePlaybackState(object? sender, VideoPlayerDiagnosticKind kind)
    {
        var generation = Volatile.Read(ref _openGeneration);
        if (sender is not FFmpegMediaPlayer player || !IsCurrentPlayer(player, generation)) return false;
        var state = player.State;
        UpdatePlayPauseButton(kind == VideoPlayerDiagnosticKind.Playing);
        // Clear before notifying observers, which may open a replacement source.
        if (kind == VideoPlayerDiagnosticKind.Stopped) _videoRenderer?.Clear();
        ReportDiagnostic(kind);
        return IsCurrentPlayer(player, generation) && state == player.State;
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        var generation = Volatile.Read(ref _openGeneration);
        if (sender is not FFmpegMediaPlayer player || !IsCurrentPlayer(player, generation)) return;
        var timelineGeneration = player.TimelineGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsCurrentPlayer(player, generation)
                || timelineGeneration != player.TimelineGeneration)
                return;
            UpdatePlayPauseButton(false);

            // Set seek bar to end and show total time
            if (SeekBar != null) SeekBar.Value = 100;
            if (CurrentTimeText != null && TotalTimeText != null)
                CurrentTimeText.Text = TotalTimeText.Text;

            ReportDiagnostic(VideoPlayerDiagnosticKind.Ended);
            if (IsCurrentPlayer(player, generation)
                && timelineGeneration == player.TimelineGeneration)
                MediaEnded?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        if (_isDraggingSeekBar || _mediaPlayer == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (SeekBar != null)
            {
                SeekBar.Value = e.Position * 100;
            }

            if (CurrentTimeText != null && _mediaPlayer.Length > 0)
            {
                var currentTime = TimeSpan.FromMilliseconds(_mediaPlayer.Length * e.Position);
                CurrentTimeText.Text = FormatTime(currentTime);
            }
        });
    }

    private void OnLengthChanged(object? sender, LengthChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (TotalTimeText != null)
            {
                var totalTime = TimeSpan.FromMilliseconds(e.Length);
                TotalTimeText.Text = FormatTime(totalTime);
            }
        });
    }

    private void OnSeekBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _isDraggingSeekBar = true;
    }

    private void OnSeekBarDragCompleted(object? sender, RoutedEventArgs e)
    {
        if (_isDraggingSeekBar)
        {
            _isDraggingSeekBar = false;
            SeekToCurrentSliderPosition();
        }
    }

    private void SeekToCurrentSliderPosition()
    {
        if (_mediaPlayer == null || SeekBar == null) return;

        var position = (float)(SeekBar.Value / 100);
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
                FileTypeFilter =
                [
                    new("Video Files") { Patterns = ["*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv", "*.flv", "*.webm", "*.m4v", "*.ts"] },
                    new("Audio Files") { Patterns = ["*.mp3", "*.wav", "*.flac", "*.aac", "*.ogg", "*.m4a"] },
                    new("All Files") { Patterns = ["*"] }
                ]
            });

            if (files.Count > 0)
            {
                var file = files[0];
                var path = file.Path.LocalPath;
                Open(path);
                if (_hasMediaLoaded && _mediaPlayer != null && !_mediaPlayer.IsPlaying)
                {
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
        if (_mediaPlayer == null || VolumeIcon == null) return;

        _isMuted = !_isMuted;

        if (_isMuted)
        {
            _previousVolume = _mediaPlayer.Volume;
            _mediaPlayer.Volume = 0;
            if (VolumeSlider != null) VolumeSlider.Value = 0;
            VolumeIcon.Data = GetIconProvider().CreateVolumeOffIcon();
        }
        else
        {
            _mediaPlayer.Volume = _previousVolume;
            if (VolumeSlider != null) VolumeSlider.Value = _previousVolume;
            VolumeIcon.Data = GetIconProvider().CreateVolumeHighIcon();
        }
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e)
    {
        IsFullscreen = !IsFullscreen;
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
            if (PlayPauseIcon != null)
            {
                PlayPauseIcon.Data = isPlaying ? iconProvider.CreatePauseIcon() : iconProvider.CreatePlayIcon();
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
        var mediaPlayer = Volatile.Read(ref _mediaPlayer);
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
            var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var previousDisposal = Interlocked.Exchange(ref _detachedDisposal, completion.Task);
            // Publish the pending cleanup before clearing the native player, so a
            // concurrent CloseAsync cannot miss both the player and its disposal.
            Interlocked.CompareExchange(ref _mediaPlayer, null, mediaPlayer);
            _ = DisposeDetachedPlayerAsync(mediaPlayer, previousDisposal, completion);
        }
        
        if (_videoRenderer != null)
        {
            _videoRenderer.Dispose();
            _videoRenderer = null;
        }
        
        _isInitialized = false;
        _currentMediaPath = null;
        _hasMediaLoaded = false;
        _diagnosticMediaInfo = null;
    }

    private Task DisposeDetachedPlayerAsync(
        FFmpegMediaPlayer mediaPlayer,
        Task<Exception?> previousDisposal,
        TaskCompletionSource<Exception?> completion) =>
        Task.Run(async () =>
        {
            Exception? failure = null;
            try
            {
                await mediaPlayer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
                Debug.WriteLine($"[VideoPlayerControl] Background cleanup failed: {ex.Message}");
            }

            try
            {
                var previousFailure = await previousDisposal.ConfigureAwait(false);
                if (previousFailure is not null)
                    failure = failure is null ? previousFailure : new AggregateException(previousFailure, failure);
            }
            catch (Exception ex)
            {
                failure = failure is null ? ex : new AggregateException(ex, failure);
            }

            completion.TrySetResult(failure);
            if (failure is not null)
            {
                ReportDiagnostic(VideoPlayerDiagnosticKind.DetachedCleanupFailed, error: new MediaError(
                    MediaErrorCode.PlaybackFailed, "A detached media player could not be disposed.", Exception: failure));
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

    // Use StreamGeometry: Avalonia 12.1.3 clones PathGeometry without its figures
    // when Path.Stretch applies a transform, leaving the initial icon invisible.

    /// <summary>Creates a play icon geometry (triangle pointing right).</summary>
    public Geometry CreatePlayIcon() =>
        Geometry.Parse("M 6,4 L 6,16 L 18,10 Z");

    /// <summary>Creates a pause icon geometry (two vertical bars).</summary>
    public Geometry CreatePauseIcon() =>
        Geometry.Parse("M 6,4 L 10,4 L 10,16 L 6,16 Z M 14,4 L 18,4 L 18,16 L 14,16 Z");

    /// <summary>Creates a stop icon geometry (square).</summary>
    public Geometry CreateStopIcon() =>
        Geometry.Parse("M 6,6 L 18,6 L 18,18 L 6,18 Z");

    /// <summary>Creates a folder open icon geometry.</summary>
    public Geometry CreateFolderOpenIcon() =>
        Geometry.Parse("M 4,6 L 4,8 L 6,8 L 8,6 L 16,6 L 18,8 L 18,16 L 4,16 Z M 4,8 L 6,10 L 18,10 L 18,8 Z");

    /// <summary>Creates a volume high icon geometry (speaker with sound waves).</summary>
    public Geometry CreateVolumeHighIcon() =>
        Geometry.Parse("M 5,6 L 5,14 L 9,14 L 13,18 L 13,2 L 9,6 Z M 15,8 A 2,2 0 0,1 15,12 Z M 17,6 A 3,3 0 0,1 17,14 Z");

    /// <summary>Creates a volume off icon geometry (speaker with X).</summary>
    public Geometry CreateVolumeOffIcon() =>
        Geometry.Parse("M 5,6 L 5,14 L 9,14 L 13,18 L 13,2 L 9,6 Z M 15,5 L 19,9 Z M 15,9 L 19,5 Z");
}
