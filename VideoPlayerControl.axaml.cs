using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>
/// A self-contained video player control with playback controls, seek bar, and volume control.
/// Uses FFmpeg for cross-platform media playback including ARM64 macOS.
/// Requires FFmpeg 8.x libraries (libavcodec.62) to be available.
/// </summary>
public partial class VideoPlayerControl : UserControl
{
    private FFmpegMediaPlayer? _mediaPlayer;
    private Image? _videoImage;
    private Slider? _seekBar;
    private Slider? _volumeSlider;
    private TextBlock? _currentTimeText;
    private TextBlock? _totalTimeText;
    private MaterialIcon? _playPauseIcon;
    private TextBlock? _playPauseText;
    private MaterialIcon? _volumeIcon;
    private bool _isDraggingSeekBar;
    private bool _isMuted;
    private int _previousVolume = 100;
    private bool _isInitialized;
    private Border? _controlPanelBorder;
    private Border? _videoBorder;
    private Button? _openButton;
    private WriteableBitmap? _frameBitmap;
    private string? _currentMediaPath;
    private bool _hasMediaLoaded;

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

    /// <summary>
    /// Defines the ControlPanelBackground property.
    /// </summary>
    public static readonly StyledProperty<Media.IBrush?> ControlPanelBackgroundProperty =
        AvaloniaProperty.Register<VideoPlayerControl, Media.IBrush?>(nameof(ControlPanelBackground), null);

    /// <summary>
    /// Defines the VideoBackground property.
    /// </summary>
    public static readonly StyledProperty<Media.IBrush?> VideoBackgroundProperty =
        AvaloniaProperty.Register<VideoPlayerControl, Media.IBrush?>(nameof(VideoBackground), Media.Brushes.Black);

    /// <summary>
    /// Defines the VideoStretch property.
    /// </summary>
    public static readonly StyledProperty<Media.Stretch> VideoStretchProperty =
        AvaloniaProperty.Register<VideoPlayerControl, Media.Stretch>(nameof(VideoStretch), Media.Stretch.Uniform);

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
    /// Gets or sets the background brush for the control panel.
    /// Default is White. Set to any brush to customize the appearance.
    /// </summary>
    public Media.IBrush? ControlPanelBackground
    {
        get => GetValue(ControlPanelBackgroundProperty);
        set => SetValue(ControlPanelBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for the video area.
    /// Default is Black. Set to Transparent or any other brush to customize.
    /// </summary>
    public Media.IBrush? VideoBackground
    {
        get => GetValue(VideoBackgroundProperty);
        set => SetValue(VideoBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the stretch mode for the video.
    /// Default is Uniform. Options: None, Fill, Uniform, UniformToFill.
    /// </summary>
    public Media.Stretch VideoStretch
    {
        get => GetValue(VideoStretchProperty);
        set => SetValue(VideoStretchProperty, value);
    }

    /// <summary>
    /// Gets the full path of the currently loaded media file, if any.
    /// </summary>
    public string? CurrentMediaPath => _currentMediaPath;

    /// <summary>
    /// Gets whether the control currently has a media resource loaded.
    /// </summary>
    public bool HasMediaLoaded => _hasMediaLoaded;

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

    /// <summary>
    /// Occurs when playback starts.
    /// </summary>
    public event EventHandler? PlaybackStarted;

    /// <summary>
    /// Occurs when media is successfully opened.
    /// </summary>
    public event EventHandler<MediaOpenedEventArgs>? MediaOpened;

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

        _videoImage = this.FindControl<Image>("VideoImage");
        _seekBar = this.FindControl<Slider>("SeekBar");
        _volumeSlider = this.FindControl<Slider>("VolumeSlider");
        _currentTimeText = this.FindControl<TextBlock>("CurrentTimeText");
        _totalTimeText = this.FindControl<TextBlock>("TotalTimeText");
        _playPauseIcon = this.FindControl<MaterialIcon>("PlayPauseIcon");
        _playPauseText = this.FindControl<TextBlock>("PlayPauseText");
        _volumeIcon = this.FindControl<MaterialIcon>("VolumeIcon");
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
        this.AttachedToVisualTree += OnAttachedToVisualTree;
        this.DetachedFromVisualTree += OnDetachedFromVisualTree;
        
        // Handle property changes
        this.PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SourceProperty)
        {
            var newSource = e.NewValue as string;
            if (!string.IsNullOrEmpty(newSource) && _isInitialized)
            {
                Open(newSource);
                if (AutoPlay)
                {
                    Play();
                }
            }
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
            if (_controlPanelBorder != null && e.NewValue is Media.IBrush brush)
            {
                _controlPanelBorder.Background = brush;
            }
        }
        else if (e.Property == VideoBackgroundProperty)
        {
            if (_videoBorder != null && e.NewValue is Media.IBrush brush)
            {
                _videoBorder.Background = brush;
            }
        }
        else if (e.Property == VideoStretchProperty)
        {
            if (_videoImage != null && e.NewValue is Media.Stretch stretch)
            {
                _videoImage.Stretch = stretch;
            }
        }
        else if (e.Property == ShowControlsProperty)
        {
            if (_controlPanelBorder != null)
            {
                _controlPanelBorder.IsVisible = (bool)(e.NewValue ?? true);
            }
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

            _mediaPlayer = new FFmpegMediaPlayer();

            // Subscribe to media player events
            _mediaPlayer.PositionChanged += OnPositionChanged;
            _mediaPlayer.LengthChanged += OnLengthChanged;
            _mediaPlayer.Playing += OnPlaying;
            _mediaPlayer.Paused += OnPaused;
            _mediaPlayer.Stopped += OnStopped;
            _mediaPlayer.EndReached += OnEndReached;
            _mediaPlayer.FrameReady += OnFrameReady;

            _isInitialized = true;
            
            // Apply initial property values
            if (_openButton != null)
            {
                _openButton.IsVisible = ShowOpenButton;
            }
            if (_controlPanelBorder != null)
            {
                _controlPanelBorder.IsVisible = ShowControls;
                if (ControlPanelBackground != null)
                {
                    _controlPanelBorder.Background = ControlPanelBackground;
                }
            }
            if (_videoBorder != null && VideoBackground != null)
            {
                _videoBorder.Background = VideoBackground;
            }
            if (_videoImage != null)
            {
                _videoImage.Stretch = VideoStretch;
            }
            
            // Load source if set
            if (!string.IsNullOrEmpty(Source))
            {
                Open(Source);
                if (AutoPlay)
                {
                    Play();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoPlayerControl] Failed to initialize FFmpeg: {ex.Message}");
        }
    }

    private void OnFrameReady(object? sender, FrameEventArgs e)
    {
        // Note: This is already called on the UI thread via Dispatcher.UIThread.Post in FFmpegMediaPlayer
        try
        {
            // Create or recreate bitmap if needed
            if (_frameBitmap == null || 
                _frameBitmap.PixelSize.Width != e.Width || 
                _frameBitmap.PixelSize.Height != e.Height)
            {
                _frameBitmap = new WriteableBitmap(
                    new PixelSize(e.Width, e.Height),
                    new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Bgra8888,
                    Avalonia.Platform.AlphaFormat.Premul);
            }

            // Copy frame data to bitmap
            using (var fb = _frameBitmap.Lock())
            {
                var destPtr = fb.Address;
                
                for (int y = 0; y < e.Height; y++)
                {
                    var sourceOffset = y * e.Stride;
                    var destOffset = y * fb.RowBytes;
                    var maxRowLength = Math.Min(e.Stride, fb.RowBytes);
                    var requestedLength = Math.Min(e.Width * 4, maxRowLength);
                    var available = Math.Max(0, e.DataLength - sourceOffset);
                    var rowLength = Math.Min(requestedLength, available);
                    if (rowLength <= 0)
                    {
                        break;
                    }

                    Marshal.Copy(e.Data, sourceOffset, destPtr + destOffset, rowLength);
                }
            }

            if (_videoImage != null)
            {
                _videoImage.Source = _frameBitmap;
                _videoImage.InvalidateVisual();
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

    /// <summary>
    /// Opens and optionally plays a media file.
    /// </summary>
    /// <param name="path">The path to the media file.</param>
    public void Open(string path)
    {
        Debug.WriteLine($"[VideoPlayerControl] Open called with path: {path}");
        
        if (_mediaPlayer == null)
        {
            Debug.WriteLine("[VideoPlayerControl] FFmpeg not initialized - _mediaPlayer is null");
            return;
        }

        _hasMediaLoaded = false;
        Debug.WriteLine("[VideoPlayerControl] Calling _mediaPlayer.Open...");

        var opened = _mediaPlayer.Open(path);
        Debug.WriteLine($"[VideoPlayerControl] _mediaPlayer.Open returned: {opened}");
        
        if (!opened)
        {
            Debug.WriteLine($"[VideoPlayerControl] Failed to open media: {path}");
            return;
        }

        _currentMediaPath = path;
        _hasMediaLoaded = true;
        Debug.WriteLine($"[VideoPlayerControl] Media loaded successfully, raising MediaOpened event");
        MediaOpened?.Invoke(this, new MediaOpenedEventArgs(path));

        if (AutoPlay)
        {
            _mediaPlayer.Play();
        }
    }

    /// <summary>
    /// Opens and optionally plays a media from a URI.
    /// </summary>
    /// <param name="uri">The URI of the media.</param>
    public void OpenUri(Uri uri)
    {
        if (uri.IsFile)
        {
            Open(uri.LocalPath);
        }
        else
        {
            Open(uri.ToString());
        }
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
        _mediaPlayer?.Stop();
        if (_seekBar != null) _seekBar.Value = 0;
        if (_currentTimeText != null) _currentTimeText.Text = "00:00";
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

    private void OnPaused(object? sender, EventArgs e)
    {
        UpdatePlayPauseButton(false);
        PlaybackPaused?.Invoke(this, EventArgs.Empty);
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        UpdatePlayPauseButton(false);
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdatePlayPauseButton(false);
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
            if (_mediaPlayer != null && _seekBar != null)
            {
                _mediaPlayer.Seek((float)(_seekBar.Value / 100));
            }
        }
    }

    private void OnSeekBarPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (_isDraggingSeekBar)
        {
            _isDraggingSeekBar = false;
            if (_mediaPlayer != null && _seekBar != null)
            {
                _mediaPlayer.Seek((float)(_seekBar.Value / 100));
            }
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
        if (_mediaPlayer == null || _volumeSlider == null || _volumeIcon == null) return;

        _isMuted = !_isMuted;

        if (_isMuted)
        {
            _previousVolume = _mediaPlayer.Volume;
            _mediaPlayer.Volume = 0;
            _volumeSlider.Value = 0;
            _volumeIcon.Kind = MaterialIconKind.VolumeOff;
        }
        else
        {
            _mediaPlayer.Volume = _previousVolume;
            _volumeSlider.Value = _previousVolume;
            _volumeIcon.Kind = MaterialIconKind.VolumeHigh;
        }
    }

    private void UpdatePlayPauseButton(bool isPlaying)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_playPauseIcon != null)
            {
                _playPauseIcon.Kind = isPlaying ? MaterialIconKind.Pause : MaterialIconKind.Play;
            }
            if (_playPauseText != null)
            {
                _playPauseText.Text = isPlaying ? "Pause" : "Play";
            }
        });
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.Hours > 0
            ? $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    private void Cleanup()
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.PositionChanged -= OnPositionChanged;
            _mediaPlayer.LengthChanged -= OnLengthChanged;
            _mediaPlayer.Playing -= OnPlaying;
            _mediaPlayer.Paused -= OnPaused;
            _mediaPlayer.Stopped -= OnStopped;
            _mediaPlayer.EndReached -= OnEndReached;
            _mediaPlayer.FrameReady -= OnFrameReady;
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }
        _frameBitmap = null;
        _isInitialized = false;
        _currentMediaPath = null;
        _hasMediaLoaded = false;
    }
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

    /// <summary>
    /// Gets the full path of the media that was opened.
    /// </summary>
    public string Path { get; }
}
