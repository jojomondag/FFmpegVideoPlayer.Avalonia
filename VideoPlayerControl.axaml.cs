using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Avalonia.VlcVideoPlayer;

/// <summary>
/// A self-contained video player control with playback controls, seek bar, and volume control.
/// </summary>
public partial class VideoPlayerControl : UserControl
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private VideoView? _videoView;
    private Slider? _seekBar;
    private Slider? _volumeSlider;
    private TextBlock? _currentTimeText;
    private TextBlock? _totalTimeText;
    private MaterialIcon? _playPauseIcon;
    private TextBlock? _playPauseText;
    private MaterialIcon? _volumeIcon;
    private bool _isDraggingSeekBar;
    private bool _isUpdatingSeekBar;
    private bool _isMuted;
    private int _previousVolume = 100;
    private bool _isInitialized;

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
        set => SetValue(ShowControlsProperty, value);
    }

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

        _videoView = this.FindControl<VideoView>("VideoView");
        _seekBar = this.FindControl<Slider>("SeekBar");
        _volumeSlider = this.FindControl<Slider>("VolumeSlider");
        _currentTimeText = this.FindControl<TextBlock>("CurrentTimeText");
        _totalTimeText = this.FindControl<TextBlock>("TotalTimeText");
        _playPauseIcon = this.FindControl<MaterialIcon>("PlayPauseIcon");
        _playPauseText = this.FindControl<TextBlock>("PlayPauseText");
        _volumeIcon = this.FindControl<MaterialIcon>("VolumeIcon");

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

        // Initialize VLC when attached to visual tree
        this.AttachedToVisualTree += OnAttachedToVisualTree;
        this.DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        InitializeVlc();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Cleanup();
    }

    private void InitializeVlc()
    {
        if (_isInitialized) return;

        try
        {
            // Ensure VLC is initialized globally
            if (!VlcInitializer.IsInitialized)
            {
                VlcInitializer.Initialize();
            }

            _libVLC = new LibVLC("--no-video-title-show");
            _mediaPlayer = new MediaPlayer(_libVLC);

            if (_videoView != null)
            {
                _videoView.MediaPlayer = _mediaPlayer;
            }

            // Subscribe to media player events
            _mediaPlayer.PositionChanged += OnPositionChanged;
            _mediaPlayer.LengthChanged += OnLengthChanged;
            _mediaPlayer.Playing += OnPlaying;
            _mediaPlayer.Paused += OnPaused;
            _mediaPlayer.Stopped += OnStopped;
            _mediaPlayer.EndReached += OnEndReached;

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VideoPlayerControl] Failed to initialize VLC: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens and optionally plays a media file.
    /// </summary>
    /// <param name="path">The path to the media file.</param>
    public void Open(string path)
    {
        if (_libVLC == null || _mediaPlayer == null)
        {
            Console.WriteLine("[VideoPlayerControl] VLC not initialized");
            return;
        }

        var media = new LibVLCSharp.Shared.Media(_libVLC, path, FromType.FromPath);
        _mediaPlayer.Media = media;

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
        if (_libVLC == null || _mediaPlayer == null)
        {
            Console.WriteLine("[VideoPlayerControl] VLC not initialized");
            return;
        }

        var media = new LibVLCSharp.Shared.Media(_libVLC, uri);
        _mediaPlayer.Media = media;

        if (AutoPlay)
        {
            _mediaPlayer.Play();
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
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Position = Math.Clamp(positionPercent, 0f, 1f);
        }
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

    private void OnPositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
    {
        if (_isDraggingSeekBar || _mediaPlayer == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_seekBar != null)
            {
                _isUpdatingSeekBar = true;
                _seekBar.Value = e.Position * 100;
                _isUpdatingSeekBar = false;
            }

            if (_currentTimeText != null && _mediaPlayer.Length > 0)
            {
                var currentTime = TimeSpan.FromMilliseconds(_mediaPlayer.Length * e.Position);
                _currentTimeText.Text = FormatTime(currentTime);
            }
        });
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
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
                _mediaPlayer.Position = (float)(_seekBar.Value / 100);
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
                _mediaPlayer.Position = (float)(_seekBar.Value / 100);
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

        if (files.Count > 0 && _libVLC != null && _mediaPlayer != null)
        {
            var file = files[0];
            var path = file.Path.LocalPath;
            var media = new LibVLCSharp.Shared.Media(_libVLC, path, FromType.FromPath);
            _mediaPlayer.Play(media);
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
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }
        _libVLC?.Dispose();
        _libVLC = null;
        _isInitialized = false;
    }
}
