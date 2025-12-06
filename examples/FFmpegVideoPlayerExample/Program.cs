using Avalonia;
using Avalonia.FFmpegVideoPlayer;

namespace FFmpegVideoPlayerExample;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Subscribe to status updates during initialization
        FFmpegInitializer.StatusChanged += (message) => Console.WriteLine(message);
        
        // Initialize FFmpeg - on macOS, auto-installs via Homebrew if needed!
        FFmpegInitializer.Initialize();
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
