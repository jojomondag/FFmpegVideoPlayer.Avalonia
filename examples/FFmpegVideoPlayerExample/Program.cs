using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.FFmpegVideoPlayer;
using Serilog;

namespace FFmpegVideoPlayerExample;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        SetupLogging();

        try
        {
            Log.Information("Launching FFmpeg Video Player example.");

            FFmpegInitializer.StatusChanged += msg =>
            {
                Log.Information(msg);
                Console.WriteLine(msg);
            };

            FFmpegInitializer.Initialize();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly.");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void SetupLogging()
    {
        var logDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "debug"));
        Directory.CreateDirectory(logDir);

        foreach (var file in Directory.EnumerateFiles(logDir, "ffmpeg-example-*.log"))
        {
            try { File.Delete(file); } catch { /* Ignore delete errors */ }
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(logDir, "ffmpeg-example-.log"), 
                rollingInterval: RollingInterval.Day, 
                retainedFileCountLimit: 7, 
                shared: true, 
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .WriteTo.Console()
            .CreateLogger();

        Trace.Listeners.Add(new SerilogTraceListener());
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private class SerilogTraceListener : TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message) && message.StartsWith("["))
                Log.Debug(message);
        }
    }
}
