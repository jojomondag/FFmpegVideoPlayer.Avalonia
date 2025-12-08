using System;
using Avalonia.Controls;
using Avalonia.FFmpegVideoPlayer;
using Avalonia.Interactivity;
using Avalonia.Media;
using Serilog;

namespace FFmpegVideoPlayerExample;

public partial class MainWindow : Window
{
    private CheckBox? _showControlsCheckBox;
    private CheckBox? _transparentBgCheckBox;
    private ComboBox? _stretchModeComboBox;

    public MainWindow()
    {
        InitializeComponent();
        
        _showControlsCheckBox = this.FindControl<CheckBox>("ShowControlsCheckBox");
        _transparentBgCheckBox = this.FindControl<CheckBox>("TransparentBgCheckBox");
        _stretchModeComboBox = this.FindControl<ComboBox>("StretchModeComboBox");
        
        if (_showControlsCheckBox != null)
        {
            _showControlsCheckBox.IsCheckedChanged += OnShowControlsChanged;
        }
        
        if (_transparentBgCheckBox != null)
        {
            _transparentBgCheckBox.IsCheckedChanged += OnTransparentBgChanged;
        }

        if (_stretchModeComboBox != null)
        {
            _stretchModeComboBox.SelectionChanged += OnStretchModeChanged;
        }

        VideoPlayer.MediaOpened += OnMediaOpened;
        VideoPlayer.PlaybackStarted += OnPlaybackStarted;
        VideoPlayer.PlaybackPaused += OnPlaybackPaused;
        VideoPlayer.PlaybackStopped += OnPlaybackStopped;
        VideoPlayer.MediaEnded += OnMediaEnded;
    }

    private void OnShowControlsChanged(object? sender, RoutedEventArgs e)
    {
        if (VideoPlayer == null) return;
        var showControls = _showControlsCheckBox?.IsChecked ?? true;
        VideoPlayer.ShowControls = showControls;
    }

    private void OnTransparentBgChanged(object? sender, RoutedEventArgs e)
    {
        if (VideoPlayer == null) return;
        if (_transparentBgCheckBox?.IsChecked == true)
        {
            VideoPlayer.VideoBackground = Brushes.Transparent;
        }
        else
        {
            VideoPlayer.VideoBackground = Brushes.Black;
        }
    }

    private void OnStretchModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VideoPlayer == null) return;
        if (_stretchModeComboBox?.SelectedItem is ComboBoxItem item && item.Content is string stretchMode)
        {
            if (Enum.TryParse<Stretch>(stretchMode, out var stretch))
            {
                VideoPlayer.VideoStretch = stretch;
            }
        }
    }

    private void OnMediaOpened(object? sender, MediaOpenedEventArgs e)
    {
        Log.Information("Media opened: {MediaPath}", e.Path);
    }

    private void OnPlaybackStarted(object? sender, EventArgs e)
    {
        var path = VideoPlayer?.CurrentMediaPath;
        if (path != null)
        {
            Log.Information("Playback started: {MediaPath}", path);
        }
        else
        {
            Log.Information("Playback started.");
        }
    }

    private void OnPlaybackPaused(object? sender, EventArgs e)
    {
        var path = VideoPlayer?.CurrentMediaPath;
        if (path != null)
        {
            Log.Information("Playback paused: {MediaPath}", path);
        }
        else
        {
            Log.Information("Playback paused.");
        }
    }

    private void OnPlaybackStopped(object? sender, EventArgs e)
    {
        var path = VideoPlayer?.CurrentMediaPath;
        if (path != null)
        {
            Log.Information("Playback stopped: {MediaPath}", path);
        }
        else
        {
            Log.Information("Playback stopped.");
        }
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        var path = VideoPlayer?.CurrentMediaPath;
        if (path != null)
        {
            Log.Information("Playback ended: {MediaPath}", path);
        }
        else
        {
            Log.Information("Playback ended.");
        }
    }
}
