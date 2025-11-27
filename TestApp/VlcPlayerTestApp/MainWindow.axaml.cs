using Avalonia.Controls;
using Avalonia.Threading;

namespace VlcPlayerTestApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // Subscribe to video player events for testing
        VideoPlayer.PlaybackStarted += (s, e) => UpdateStatus("Playback started");
        VideoPlayer.PlaybackPaused += (s, e) => UpdateStatus("Playback paused");
        VideoPlayer.PlaybackStopped += (s, e) => UpdateStatus("Playback stopped");
        VideoPlayer.MediaEnded += (s, e) => UpdateStatus("Media ended");
    }
    
    private void UpdateStatus(string message)
    {
        Dispatcher.UIThread.Post(() => StatusText.Text = message);
    }
}