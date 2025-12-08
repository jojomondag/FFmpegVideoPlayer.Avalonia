using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.FFmpegVideoPlayer;
using Serilog;

namespace FFmpegVideoPlayerExample;

/// <summary>
/// Custom TraceListener that forwards Debug.WriteLine output to Serilog.
/// Only forwards messages that start with '[' to avoid infinite loops.
/// </summary>
class SerilogTraceListener : TraceListener
{
    public override void Write(string? message)
    {
        // Don't forward - only handle WriteLine
    }

    public override void WriteLine(string? message)
    {
        // Only forward our prefixed debug messages to avoid loops
        if (!string.IsNullOrEmpty(message) && message.StartsWith("["))
        {
            Log.Debug(message);
        }
    }
}

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "debug"));
        Directory.CreateDirectory(logDirectory);

        foreach (var file in Directory.EnumerateFiles(logDirectory, "ffmpeg-example-*.log"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete old log '{file}': {ex.Message}");
            }
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(logDirectory, "ffmpeg-example-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true, flushToDiskInterval: TimeSpan.FromSeconds(1))
            .WriteTo.Console()
            .CreateLogger();

        // Add trace listener to capture Debug.WriteLine from library code
        Trace.Listeners.Add(new SerilogTraceListener());

        try
        {
            Log.Information("Launching FFmpeg Video Player example.");

            // Subscribe to status updates during initialization
            FFmpegInitializer.StatusChanged += message =>
            {
                Log.Information(message);
                Console.WriteLine(message);
            };

            // Initialize FFmpeg - on macOS, auto-installs via Homebrew if needed!
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

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
