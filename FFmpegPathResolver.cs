using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>
/// Locates packaged FFmpeg binaries (NuGet runtimes) and configures the native search path.
/// </summary>
internal static class FFmpegPathResolver
{
    private static bool _bindingsInitialized;

    /// <summary>
    /// Tries to find a bundled FFmpeg path under runtimes/&lt;rid&gt;/native and, if found,
    /// configures the process search path so the native loader can locate dependencies.
    /// </summary>
    public static string? TryConfigureBundledFFmpeg()
    {
        var path = FindBundledPath();
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        ConfigureNativeSearchPath(path);
        return path;
    }

    /// <summary>
    /// Returns runtimes/&lt;rid&gt;/native if it contains FFmpeg binaries.
    /// </summary>
    public static string? FindBundledPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var rid = GetRuntimeIdentifier();

        var candidates = new List<string>
        {
            Path.Combine(baseDir, "runtimes", rid, "native"),
            Path.Combine(baseDir, rid),
            Path.Combine(baseDir, "native", rid),
            Path.Combine(baseDir, "ffmpeg", rid),
            Path.Combine(baseDir, "ffmpeg", "bin")
        };

        // Fallback for universal macOS builds if provided as osx-universal/native
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            candidates.Add(Path.Combine(baseDir, "runtimes", "osx-universal", "native"));
        }

        foreach (var candidate in candidates)
        {
            if (HasFFmpegLibrary(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Ensures the native loader can locate FFmpeg binaries from a specific folder.
    /// </summary>
    public static void ConfigureNativeSearchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        // Hint FFmpeg.AutoGen to load from this folder
        ffmpeg.RootPath = path;

        AddPathVariable("PATH", path);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            AddPathVariable("DYLD_LIBRARY_PATH", path);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            AddPathVariable("LD_LIBRARY_PATH", path);
        }

        // Initialize the dynamically loaded bindings after setting the path
        InitializeBindings();
    }

    /// <summary>
    /// Initializes the FFmpeg.AutoGen dynamic bindings.
    /// Must be called after setting <see cref="ffmpeg.RootPath"/>.
    /// </summary>
    public static void InitializeBindings()
    {
        if (_bindingsInitialized)
            return;

        try
        {
            // FFmpeg.AutoGen 8.x uses DynamicallyLoadedBindings that must be initialized.
            // This sets up lazy loading of all FFmpeg functions.
            DynamicallyLoadedBindings.Initialize();
            _bindingsInitialized = true;
#if DEBUG
            Console.WriteLine($"[FFmpegPathResolver] Bindings initialized (RootPath: {ffmpeg.RootPath})");
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FFmpegPathResolver] Failed to initialize bindings: {ex.Message}");
            throw;
        }
    }

    public static string GetRuntimeIdentifier()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "unknown";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64"
        };

        return $"{os}-{arch}";
    }

    public static bool HasFFmpegLibrary(string path)
    {
        if (!Directory.Exists(path))
            return false;

        var libraryPatterns = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "avcodec*.dll", "avformat*.dll", "avutil*.dll" }
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new[] { "libavcodec*.dylib", "libavformat*.dylib", "libavutil*.dylib" }
                : new[] { "libavcodec.so*", "libavformat.so*", "libavutil.so*" };

        foreach (var pattern in libraryPatterns)
        {
            try
            {
                if (Directory.GetFiles(path, pattern).Length > 0)
                    return true;
            }
            catch
            {
                // Ignore IO issues and continue checking other patterns
            }
        }

        return false;
    }

    private static void AddPathVariable(string variable, string path)
    {
        var current = Environment.GetEnvironmentVariable(variable) ?? string.Empty;
        var parts = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            return;

        var newValue = string.IsNullOrEmpty(current)
            ? path
            : $"{path}{Path.PathSeparator}{current}";

        Environment.SetEnvironmentVariable(variable, newValue);
    }
}

