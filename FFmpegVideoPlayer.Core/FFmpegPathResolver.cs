using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace FFmpegVideoPlayer.Core;

/// <summary>
/// Locates packaged FFmpeg binaries (NuGet runtimes) and configures the native search path.
/// </summary>
internal static unsafe class FFmpegPathResolver
{
    // FFmpeg reports a non-fatal warning for H.264 streams that carry an SEI
    // message after the picture it describes. The decoder deliberately ignores
    // that metadata, but the diagnostic is extremely noisy for normal playback.
    // Keep the native callback alive for the lifetime of the process and filter
    // only this known message; all other FFmpeg diagnostics still use FFmpeg's
    // default callback.
    private static readonly av_log_set_callback_callback s_logCallback = FilterLog;

    [ThreadStatic]
    private static LateSeiLogFilterState s_lateSeiLogFilterState;

    private const string LateSeiHeader = "Late SEI";
    private const string MissingFeatureContinuation =
        " is not implemented. Update your FFmpeg version to the newest one from Git. " +
        "If the problem still occurs, it means that your file has a feature which has not " +
        "been implemented.\n";
    private const string SampleUploadContinuation =
        "If you want to help, upload a sample of this file to https://streams.videolan.org/upload/ " +
        "and contact the ffmpeg-devel mailing list. (ffmpeg-devel@ffmpeg.org)\n";

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
    /// Initializes (or re-initializes) the FFmpeg.AutoGen dynamic bindings.
    /// Must be called after setting <see cref="ffmpeg.RootPath"/>. Safe to call multiple
    /// times — each call re-resolves every function against the current RootPath.
    /// </summary>
    public static void InitializeBindings()
    {
        // FFmpeg.AutoGen 8.x's DynamicallyLoadedBindings.Initialize is idempotent and
        // re-probes every function, so we can call it again after the search path changes.
        DynamicallyLoadedBindings.Initialize();
        ffmpeg.av_log_set_callback(s_logCallback);
#if DEBUG
        Console.WriteLine($"[FFmpegPathResolver] Bindings initialized (RootPath: {ffmpeg.RootPath})");
#endif
    }

    private static unsafe void FilterLog(void* avcl, int level, string format, byte* vl)
    {
        // avpriv_request_sample emits this diagnostic as three separate log
        // callbacks: "Late SEI", the generic "is not implemented" text, and
        // the optional upload-help text. Track the continuation on the same
        // decoder thread so only this diagnostic is removed.
        if (ShouldSuppressLateSeiLogFragment(format, ref s_lateSeiLogFilterState))
        {
            return;
        }

        ffmpeg.av_log_default_callback(avcl, level, format, vl);
    }

    internal static bool ShouldSuppressLateSeiLogFragment(
        string? format,
        ref LateSeiLogFilterState state)
    {
        switch (state)
        {
            case LateSeiLogFilterState.None:
                if (string.Equals(format, LateSeiHeader, StringComparison.Ordinal))
                {
                    state = LateSeiLogFilterState.ExpectMissingFeatureContinuation;
                    return true;
                }

                return false;

            case LateSeiLogFilterState.ExpectMissingFeatureContinuation:
                if (string.Equals(format, MissingFeatureContinuation, StringComparison.Ordinal))
                {
                    state = LateSeiLogFilterState.ExpectSampleUploadContinuation;
                    return true;
                }

                state = LateSeiLogFilterState.None;
                return false;

            case LateSeiLogFilterState.ExpectSampleUploadContinuation:
                if (string.Equals(format, SampleUploadContinuation, StringComparison.Ordinal))
                {
                    state = LateSeiLogFilterState.None;
                    return true;
                }

                state = LateSeiLogFilterState.None;
                return false;

            default:
                state = LateSeiLogFilterState.None;
                return false;
        }
    }

    /// <summary>
    /// Probes whether the currently-configured FFmpeg libraries actually loaded and
    /// exported their functions. FFmpeg.AutoGen silently swallows dlopen/LoadLibrary
    /// failures and replaces unresolved functions with stubs that throw
    /// NotSupportedException at call time — so "bindings initialized" does not imply
    /// "native library loaded". Calling a trivial function and checking for a non-zero
    /// return is the cheapest reliable check.
    /// </summary>
    public static bool TryValidateBindings()
    {
        try
        {
            return ffmpeg.avcodec_version() != 0;
        }
        catch
        {
            return false;
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

internal enum LateSeiLogFilterState
{
    None,
    ExpectMissingFeatureContinuation,
    ExpectSampleUploadContinuation
}

