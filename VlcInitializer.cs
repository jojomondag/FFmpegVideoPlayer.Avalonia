using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace Avalonia.VlcVideoPlayer;

/// <summary>
/// Handles VLC initialization using NuGet packages where available.
/// 
/// Platform Support:
/// 
/// Windows (x64/x86): VLC binaries included via VideoLAN.LibVLC.Windows NuGet package.
///                    No additional setup required.
/// 
/// macOS (Intel x64): VLC binaries included via VideoLAN.LibVLC.Mac NuGet package.
///                    No additional setup required.
/// 
/// macOS (ARM64):     No NuGet package available. Requires system VLC installation.
///                    Install via: brew install --cask vlc (then get ARM64 version)
/// 
/// Linux (x64/ARM64): No NuGet package available. Requires system VLC installation.
///                    Install via: sudo apt install vlc libvlc-dev
/// </summary>
public static class VlcInitializer
{
    // P/Invoke to set environment variable at the native level (required for macOS/Linux)
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    private static bool _isInitialized;
    private static string? _vlcLibPath;
    private static string? _initializationError;

    /// <summary>
    /// Gets whether VLC has been successfully initialized.
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the path to the VLC library directory being used, or null if using system default.
    /// </summary>
    public static string? VlcLibPath => _vlcLibPath;

    /// <summary>
    /// Gets any error message from initialization, or null if successful.
    /// </summary>
    public static string? InitializationError => _initializationError;

    /// <summary>
    /// Gets the detected platform and architecture (e.g., "macos-arm64", "windows-x64").
    /// </summary>
    public static string PlatformInfo => $"{GetPlatformName()}-{GetArchitectureName()}";

    /// <summary>
    /// Event raised with status messages during initialization.
    /// </summary>
    public static event Action<string>? StatusChanged;

    #region Platform Detection

    /// <summary>
    /// Determines if the current system is running on ARM architecture.
    /// </summary>
    public static bool IsArm => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ||
                                 RuntimeInformation.ProcessArchitecture == Architecture.Arm;

    /// <summary>
    /// Determines if the current system is running on x64 architecture.
    /// </summary>
    public static bool IsX64 => RuntimeInformation.ProcessArchitecture == Architecture.X64;

    /// <summary>
    /// Determines if the current system is running on x86 architecture.
    /// </summary>
    public static bool IsX86 => RuntimeInformation.ProcessArchitecture == Architecture.X86;

    private static string GetPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        return "unknown";
    }

    private static string GetArchitectureName()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes VLC with system-installed libraries.
    /// Call this method BEFORE creating any Avalonia windows or VLC instances.
    /// Typically called at the very start of Main() in Program.cs.
    /// </summary>
    /// <param name="customVlcPath">Optional custom path to VLC libraries.</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    /// <exception cref="VlcNotFoundException">Thrown when VLC is not installed on the system.</exception>
    public static bool Initialize(string? customVlcPath = null)
    {
        if (_isInitialized)
        {
            return true;
        }

        try
        {
            Log($"Initializing VLC for {PlatformInfo}");
            StatusChanged?.Invoke($"Initializing VLC for {PlatformInfo}...");

            // Try to find VLC installation
            _vlcLibPath = FindVlcLibPath(customVlcPath);

            if (_vlcLibPath != null)
            {
                // Validate architecture on macOS
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    if (!ValidateMacOsArchitecture(_vlcLibPath))
                    {
                        var arch = IsArm ? "Apple Silicon (ARM64)" : "Intel (x64)";
                        throw new VlcNotFoundException(
                            $"Found VLC at {_vlcLibPath}, but it's not compatible with {arch}.\n" +
                            GetInstallationInstructions());
                    }
                }

                return InitializeWithPath(_vlcLibPath);
            }

            // Windows: Try default initialization (NuGet package handles binaries)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    Log("Attempting default Windows initialization via NuGet package");
                    Core.Initialize();
                    _isInitialized = true;
                    StatusChanged?.Invoke("VLC initialized via NuGet package");
                    return true;
                }
                catch (Exception ex)
                {
                    throw new VlcNotFoundException(
                        $"VLC libraries not found. {ex.Message}\n" +
                        GetInstallationInstructions());
                }
            }

            // macOS/Linux: VLC not found
            throw new VlcNotFoundException(
                $"VLC libraries not found on {GetPlatformName()} ({GetArchitectureName()}).\n" +
                GetInstallationInstructions());
        }
        catch (VlcNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _initializationError = ex.Message;
            StatusChanged?.Invoke($"Failed to initialize VLC: {ex.Message}");
            Log($"Failed to initialize VLC: {ex.Message}");
            throw new VlcNotFoundException(
                $"Failed to initialize VLC: {ex.Message}\n" +
                GetInstallationInstructions(), ex);
        }
    }

    /// <summary>
    /// Tries to initialize VLC without throwing exceptions.
    /// </summary>
    /// <param name="customVlcPath">Optional custom path to VLC libraries.</param>
    /// <param name="errorMessage">Output parameter containing error message if initialization fails.</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    public static bool TryInitialize(string? customVlcPath, out string? errorMessage)
    {
        try
        {
            Initialize(customVlcPath);
            errorMessage = null;
            return true;
        }
        catch (VlcNotFoundException ex)
        {
            errorMessage = ex.Message;
            _initializationError = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _initializationError = ex.Message;
            return false;
        }
    }

    private static bool InitializeWithPath(string libPath)
    {
        // Set up plugin path
        var pluginPath = FindPluginPath(Path.GetDirectoryName(libPath));
        if (pluginPath != null)
        {
            SetPluginPath(pluginPath);
        }

        // Set library path environment variable BEFORE Core.Initialize()
        SetLibraryPath(libPath);

        StatusChanged?.Invoke($"Using VLC from: {libPath}");
        Log($"Using VLC from: {libPath}");

        Core.Initialize(libPath);
        _isInitialized = true;
        return true;
    }

    #endregion

    #region Installation Instructions

    /// <summary>
    /// Gets platform-specific VLC installation instructions.
    /// </summary>
    public static string GetInstallationInstructions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return @"
WINDOWS:
VLC binaries are provided via the VideoLAN.LibVLC.Windows NuGet package.
Ensure your project references 'VideoLAN.LibVLC.Windows' version 3.0.21 or later.
The binaries should be in your output directory under 'libvlc/win-x64/'.";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (IsArm)
            {
                return @"
macOS (Apple Silicon ARM64):
No NuGet package is available for macOS ARM64.
You must install VLC on your system:

    brew install --cask vlc

Note: After installing, ensure you have the ARM64 version of VLC.app
in /Applications. The library will automatically detect it.";
            }
            return @"
macOS (Intel x64):
VLC binaries are provided via the VideoLAN.LibVLC.Mac NuGet package.
Ensure your project references 'VideoLAN.LibVLC.Mac' version 3.1.3.1 or later.
The binaries should be in your output directory under 'libvlc/osx-x64/'.";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var arch = GetArchitectureName();
            return $@"
LINUX ({arch}):
No NuGet package is available for Linux.
You must install VLC on your system:

Debian/Ubuntu:
    sudo apt update
    sudo apt install vlc libvlc-dev

Fedora:
    sudo dnf install vlc vlc-devel

Arch Linux:
    sudo pacman -S vlc";
        }

        return "VLC libraries not found. Please ensure VLC is installed on your system.";
    }

    /// <summary>
    /// Checks if VLC is properly installed on the system.
    /// </summary>
    public static VlcInstallationStatus CheckInstallation()
    {
        var status = new VlcInstallationStatus
        {
            Platform = GetPlatformName(),
            Architecture = GetArchitectureName()
        };

        try
        {
            var libPath = FindVlcLibPath(null);

            if (libPath != null)
            {
                status.IsInstalled = true;
                status.LibraryPath = libPath;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    status.IsArchitectureCompatible = ValidateMacOsArchitecture(libPath);
                }
                else
                {
                    status.IsArchitectureCompatible = true;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Check if NuGet package is available
                var nugetPath = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
                status.IsInstalled = Directory.Exists(nugetPath);
                status.LibraryPath = nugetPath;
                status.IsArchitectureCompatible = true;
            }
        }
        catch (Exception ex)
        {
            status.Error = ex.Message;
        }

        status.InstallationInstructions = GetInstallationInstructions();
        return status;
    }

    #endregion

    #region Architecture Validation

    /// <summary>
    /// Validates that the VLC library at the given path matches the current architecture on macOS.
    /// </summary>
    private static bool ValidateMacOsArchitecture(string libPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return true;

        var libvlcPath = FindLibVlcDylib(libPath);
        if (libvlcPath == null)
        {
            Log($"Could not find libvlc.dylib in {libPath}");
            return true; // Can't validate, assume OK
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "file",
                Arguments = $"\"{libvlcPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return true;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var expectedArch = IsArm ? "arm64" : "x86_64";
            var isCompatible = output.Contains(expectedArch);

            if (!isCompatible)
            {
                Log($"Architecture mismatch: Expected {expectedArch}, but VLC is: {output.Trim()}");
            }
            else
            {
                Log($"Architecture validated: {expectedArch}");
            }

            return isCompatible;
        }
        catch (Exception ex)
        {
            Log($"Could not validate architecture: {ex.Message}");
            return true; // Assume OK if we can't check
        }
    }

    private static string? FindLibVlcDylib(string basePath)
    {
        // Direct path
        var direct = Path.Combine(basePath, "libvlc.dylib");
        if (File.Exists(direct)) return direct;

        // In lib subdirectory
        var inLib = Path.Combine(basePath, "lib", "libvlc.dylib");
        if (File.Exists(inLib)) return inLib;

        // Parent lib directory
        var parent = Path.GetDirectoryName(basePath);
        if (parent != null)
        {
            var parentLib = Path.Combine(parent, "lib", "libvlc.dylib");
            if (File.Exists(parentLib)) return parentLib;
        }

        return null;
    }

    #endregion

    #region Path Discovery

    private static string? FindVlcLibPath(string? customPath)
    {
        // 1. Custom path provided by user
        if (!string.IsNullOrEmpty(customPath))
        {
            var resolved = ResolveVlcPath(customPath);
            if (resolved != null)
            {
                Log($"Found VLC at custom path: {resolved}");
                return resolved;
            }
        }

        // 2. Platform-specific detection
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindWindowsVlc();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return FindMacOsVlc();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return FindLinuxVlc();
        }

        return null;
    }

    private static string? FindWindowsVlc()
    {
        var baseDir = AppContext.BaseDirectory;

        // 1. VideoLAN.LibVLC.Windows NuGet package (primary method)
        var nugetPaths = new[]
        {
            Path.Combine(baseDir, "libvlc", "win-x64"),
            Path.Combine(baseDir, "libvlc", "win-x86"),
            Path.Combine(baseDir, "runtimes", "win-x64", "native"),
            Path.Combine(baseDir, "runtimes", "win-x86", "native"),
        };

        foreach (var path in nugetPaths)
        {
            if (File.Exists(Path.Combine(path, "libvlc.dll")))
            {
                Log($"Found VLC via NuGet package: {path}");
                return path;
            }
        }

        // 2. Application directory
        if (File.Exists(Path.Combine(baseDir, "libvlc.dll")))
        {
            Log($"Found VLC in application directory: {baseDir}");
            return baseDir;
        }

        // 3. Common VLC installation paths
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var installPaths = new[]
        {
            Path.Combine(programFiles, "VideoLAN", "VLC"),
            Path.Combine(programFilesX86, "VideoLAN", "VLC"),
        };

        foreach (var path in installPaths)
        {
            if (File.Exists(Path.Combine(path, "libvlc.dll")))
            {
                Log($"Found VLC installation: {path}");
                return path;
            }
        }

        return null;
    }

    private static string? FindMacOsVlc()
    {
        // 1. Homebrew installation (universal binary - supports both Intel and ARM64)
        var homebrewPaths = new[]
        {
            "/opt/homebrew/lib",           // Homebrew on Apple Silicon
            "/usr/local/lib",              // Homebrew on Intel
            "/opt/homebrew/Cellar/vlc",    // Homebrew Cellar on Apple Silicon
            "/usr/local/Cellar/vlc",       // Homebrew Cellar on Intel
        };

        foreach (var path in homebrewPaths)
        {
            if (File.Exists(Path.Combine(path, "libvlc.dylib")))
            {
                Log($"Found VLC via Homebrew: {path}");
                return path;
            }

            // Check Cellar subdirectories
            if (path.Contains("Cellar") && Directory.Exists(path))
            {
                try
                {
                    foreach (var versionDir in Directory.GetDirectories(path))
                    {
                        var libPath = Path.Combine(versionDir, "lib");
                        if (File.Exists(Path.Combine(libPath, "libvlc.dylib")))
                        {
                            Log($"Found VLC via Homebrew Cellar: {libPath}");
                            return libPath;
                        }
                    }
                }
                catch { }
            }
        }

        // 2. VLC.app installation
        var vlcAppLib = "/Applications/VLC.app/Contents/MacOS/lib";
        if (Directory.Exists(vlcAppLib))
        {
            Log($"Found VLC.app: {vlcAppLib}");
            return vlcAppLib;
        }

        // 3. User Applications folder
        var userVlcAppLib = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Applications/VLC.app/Contents/MacOS/lib");
        if (Directory.Exists(userVlcAppLib))
        {
            Log($"Found VLC.app in user Applications: {userVlcAppLib}");
            return userVlcAppLib;
        }

        // 4. NuGet package (Intel x64 only)
        if (!IsArm)
        {
            var baseDir = AppContext.BaseDirectory;
            var nugetPath = Path.Combine(baseDir, "libvlc", "osx-x64");
            if (File.Exists(Path.Combine(nugetPath, "libvlc.dylib")))
            {
                Log($"Found VLC via NuGet package (Intel): {nugetPath}");
                return nugetPath;
            }
        }

        return null;
    }

    private static string? FindLinuxVlc()
    {
        // System library paths (architecture-specific)
        var systemPaths = new System.Collections.Generic.List<string>();

        if (IsArm)
        {
            systemPaths.AddRange(new[]
            {
                "/usr/lib/aarch64-linux-gnu",        // Debian/Ubuntu ARM64
                "/usr/lib64",                         // Fedora/RHEL ARM64
                "/usr/lib/arm-linux-gnueabihf",      // Debian/Ubuntu ARMv7
            });
        }
        else
        {
            systemPaths.AddRange(new[]
            {
                "/usr/lib/x86_64-linux-gnu",         // Debian/Ubuntu x64
                "/usr/lib64",                         // Fedora/RHEL x64
                "/usr/lib/i386-linux-gnu",           // Debian/Ubuntu x86
                "/usr/lib32",                         // 32-bit libs on 64-bit system
            });
        }

        // Common paths for all architectures
        systemPaths.AddRange(new[]
        {
            "/usr/lib",
            "/usr/local/lib",
            "/lib",
        });

        foreach (var path in systemPaths)
        {
            if (HasVlcLibrary(path))
            {
                Log($"Found VLC at: {path}");
                return path;
            }

            // Check vlc subdirectory
            var vlcSubdir = Path.Combine(path, "vlc");
            if (HasVlcLibrary(vlcSubdir))
            {
                Log($"Found VLC at: {vlcSubdir}");
                return vlcSubdir;
            }
        }

        // Check LD_LIBRARY_PATH
        var ldPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        if (!string.IsNullOrEmpty(ldPath))
        {
            foreach (var path in ldPath.Split(':'))
            {
                if (HasVlcLibrary(path))
                {
                    Log($"Found VLC via LD_LIBRARY_PATH: {path}");
                    return path;
                }
            }
        }

        return null;
    }

    private static string? ResolveVlcPath(string path)
    {
        // Direct library files
        if (HasVlcLibrary(path))
            return path;

        // Check lib subdirectory
        var libPath = Path.Combine(path, "lib");
        if (HasVlcLibrary(libPath))
            return libPath;

        // macOS .app bundle
        if (path.EndsWith(".app") && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var appLibPath = Path.Combine(path, "Contents", "MacOS", "lib");
            if (HasVlcLibrary(appLibPath))
                return appLibPath;
        }

        return null;
    }

    private static bool HasVlcLibrary(string path)
    {
        if (!Directory.Exists(path))
            return false;

        return File.Exists(Path.Combine(path, "libvlc.dylib")) ||
               File.Exists(Path.Combine(path, "libvlc.so")) ||
               File.Exists(Path.Combine(path, "libvlc.so.5")) ||
               File.Exists(Path.Combine(path, "libvlc.dll"));
    }

    private static string? FindPluginPath(string? basePath)
    {
        if (string.IsNullOrEmpty(basePath))
            return null;

        // Direct plugins folder
        var pluginsPath = Path.Combine(basePath, "plugins");
        if (Directory.Exists(pluginsPath))
            return pluginsPath;

        // Parent directory plugins
        var parent = Path.GetDirectoryName(basePath);
        if (parent != null)
        {
            var parentPlugins = Path.Combine(parent, "plugins");
            if (Directory.Exists(parentPlugins))
                return parentPlugins;
        }

        // macOS: VLC.app plugins
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var vlcAppPlugins = "/Applications/VLC.app/Contents/MacOS/plugins";
            if (Directory.Exists(vlcAppPlugins))
                return vlcAppPlugins;
        }

        // Linux: System plugins
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linuxPluginPaths = new[]
            {
                "/usr/lib/vlc/plugins",
                "/usr/lib64/vlc/plugins",
                "/usr/local/lib/vlc/plugins",
                $"/usr/lib/{(IsArm ? "aarch64" : "x86_64")}-linux-gnu/vlc/plugins",
            };

            foreach (var path in linuxPluginPaths)
            {
                if (Directory.Exists(path))
                    return path;
            }
        }

        return null;
    }

    #endregion

    #region Environment Setup

    private static void SetPluginPath(string pluginPath)
    {
        Log($"Setting VLC_PLUGIN_PATH to: {pluginPath}");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            setenv("VLC_PLUGIN_PATH", pluginPath, 1);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginPath);
        }
    }

    private static void SetLibraryPath(string libPath)
    {
        Log($"Setting library path to: {libPath}");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // DYLD_LIBRARY_PATH needed for the dynamic linker to find libvlc.dylib
            var existingPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH");
            var newPath = string.IsNullOrEmpty(existingPath) ? libPath : $"{libPath}:{existingPath}";
            setenv("DYLD_LIBRARY_PATH", newPath, 1);

            // Also set DYLD_FALLBACK_LIBRARY_PATH as a fallback
            var existingFallback = Environment.GetEnvironmentVariable("DYLD_FALLBACK_LIBRARY_PATH");
            var newFallback = string.IsNullOrEmpty(existingFallback) ? libPath : $"{libPath}:{existingFallback}";
            setenv("DYLD_FALLBACK_LIBRARY_PATH", newFallback, 1);

            Log($"Set DYLD_LIBRARY_PATH to: {newPath}");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // LD_LIBRARY_PATH is used on Linux
            var existingPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            var newPath = string.IsNullOrEmpty(existingPath) ? libPath : $"{libPath}:{existingPath}";
            setenv("LD_LIBRARY_PATH", newPath, 1);
            Log($"Set LD_LIBRARY_PATH to: {newPath}");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Add to PATH on Windows
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!existingPath.Contains(libPath))
            {
                Environment.SetEnvironmentVariable("PATH", $"{libPath};{existingPath}");
                Log($"Added to PATH: {libPath}");
            }
        }
    }

    #endregion

    #region Logging

    private static void Log(string message)
    {
        Debug.WriteLine($"[VlcInitializer] {message}");
#if DEBUG
        Console.WriteLine($"[VlcInitializer] {message}");
#endif
    }

    #endregion
}

/// <summary>
/// Exception thrown when VLC is not installed or cannot be found on the system.
/// </summary>
public class VlcNotFoundException : Exception
{
    public VlcNotFoundException(string message) : base(message) { }
    public VlcNotFoundException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Provides information about the VLC installation status on the system.
/// </summary>
public class VlcInstallationStatus
{
    /// <summary>
    /// The current operating system platform (windows, macos, linux).
    /// </summary>
    public string Platform { get; set; } = "";

    /// <summary>
    /// The current CPU architecture (x64, x86, arm64, arm).
    /// </summary>
    public string Architecture { get; set; } = "";

    /// <summary>
    /// Whether VLC libraries were found on the system.
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Whether the found VLC libraries are compatible with the current architecture.
    /// </summary>
    public bool IsArchitectureCompatible { get; set; }

    /// <summary>
    /// The path to the VLC libraries, if found.
    /// </summary>
    public string? LibraryPath { get; set; }

    /// <summary>
    /// Any error message encountered during detection.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Platform-specific installation instructions.
    /// </summary>
    public string InstallationInstructions { get; set; } = "";

    /// <summary>
    /// Whether VLC is ready to use (installed and architecture compatible).
    /// </summary>
    public bool IsReady => IsInstalled && IsArchitectureCompatible;

    public override string ToString()
    {
        if (IsReady)
            return $"VLC is installed and ready at: {LibraryPath}";

        if (IsInstalled && !IsArchitectureCompatible)
            return $"VLC is installed at {LibraryPath} but is not compatible with {Architecture}";

        return $"VLC is not installed.\n{InstallationInstructions}";
    }
}
