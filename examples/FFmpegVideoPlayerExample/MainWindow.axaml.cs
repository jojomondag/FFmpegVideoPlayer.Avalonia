using System;
using Avalonia.Controls;
using Avalonia.FFmpegVideoPlayer;
using Avalonia.Interactivity;
using Avalonia.Media;
using Serilog;

namespace FFmpegVideoPlayerExample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        ShowControlsCheckBox.IsCheckedChanged += OnShowControlsChanged;
        TransparentBgCheckBox.IsCheckedChanged += OnTransparentBgChanged;
        StretchModeComboBox.SelectionChanged += OnStretchModeChanged;

        VideoPlayer.MediaOpened += (s, e) => Log.Information("Media opened: {MediaPath}", e.Path);
        VideoPlayer.PlaybackStarted += (s, e) => LogPlaybackEvent("started");
        VideoPlayer.PlaybackPaused += (s, e) => LogPlaybackEvent("paused");
        VideoPlayer.PlaybackStopped += (s, e) => LogPlaybackEvent("stopped");
        VideoPlayer.MediaEnded += (s, e) => LogPlaybackEvent("ended");
    }

    private void LogPlaybackEvent(string eventName)
    {
        var path = VideoPlayer?.CurrentMediaPath;
        if (path != null)
            Log.Information("Playback {EventName}: {MediaPath}", eventName, path);
        else
            Log.Information("Playback {EventName}.", eventName);
    }

    private void OnShowControlsChanged(object? sender, RoutedEventArgs e)
    {
        if (VideoPlayer == null) return;
        VideoPlayer.ShowControls = ShowControlsCheckBox.IsChecked ?? true;
    }

    private void OnTransparentBgChanged(object? sender, RoutedEventArgs e)
    {
        if (VideoPlayer == null) return;
        VideoPlayer.VideoBackground = TransparentBgCheckBox.IsChecked == true 
            ? Brushes.Transparent 
            : Brushes.Black;
    }

    private void OnStretchModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VideoPlayer == null) return;
        if (StretchModeComboBox.SelectedItem is ComboBoxItem { Content: string stretchMode } &&
            Enum.TryParse<Stretch>(stretchMode, out var stretch))
        {
            VideoPlayer.VideoStretch = stretch;
        }
    }

    private void OnShortcutsClick(object? sender, RoutedEventArgs e)
    {
        var window = new ShortcutsWindow();
        window.Show(this);
    }
}
