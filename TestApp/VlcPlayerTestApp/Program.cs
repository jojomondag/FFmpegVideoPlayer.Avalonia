using Avalonia;
using Avalonia.VlcVideoPlayer;
using System;

namespace VlcPlayerTestApp;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Initialize VLC before creating any windows
        // This will auto-download VLC libraries if not found
        VlcInitializer.StatusChanged += (status) =>
        {
            Console.WriteLine($"[VLC Status] {status}");
        };
        
        VlcInitializer.DownloadProgressChanged += (progress) =>
        {
            Console.WriteLine($"[VLC Download] {progress}%");
        };
        
        Console.WriteLine("[TestApp] Starting VLC initialization...");
        var result = VlcInitializer.Initialize();
        Console.WriteLine($"[TestApp] VLC initialized: {result}");
        Console.WriteLine($"[TestApp] VLC Lib Path: {VlcInitializer.VlcLibPath ?? "null"}");
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
