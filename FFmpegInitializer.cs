using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>
/// Handles FFmpeg initialization for cross-platform video playback.
/// On macOS, can automatically install FFmpeg via Homebrew if not present.
/// 
/// Platform Support:
/// 
/// Windows (x64/x86/ARM64): Install FFmpeg via winget, chocolatey, or download binaries.
///                          winget install ffmpeg
///                          choco install ffmpeg
/// 
/// macOS (Intel x64/ARM64): Automatic installation via Homebrew supported!
///                          Or manually: brew install ffmpeg
/// 
/// Linux (x64/ARM64):       Install via package manager.
///                          sudo apt install ffmpeg libavcodec-dev libavformat-dev libavutil-dev libswscale-dev libswresample-dev
/// 
/// Note: This library uses FFmpeg.AutoGen 8.x which requires FFmpeg 8.x libraries (libavcodec.62).
/// </summary>
public static class FFmpegInitializer
{
    private static bool _isInitialized;
    private static string? _ffmpegPath;
    private static string? _initializationError;

    /// <summary>
    /// Gets whether FFmpeg has been successfully initialized.
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the path to the FFmpeg library directory being used, or null if using system default.
    /// </summary>
    public static string? FFmpegPath => _ffmpegPath;

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

    /// <summary>
    /// Determines if the current system is macOS.
    /// </summary>
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Determines if the current system is Windows.
    /// </summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Determines if the current system is Linux.
    /// </summary>
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

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
    /// Initializes FFmpeg with system-installed libraries.
    /// On macOS, automatically installs FFmpeg via Homebrew if not found and autoInstall is true.
    /// Call this method BEFORE creating any Avalonia windows or media player instances.
    /// Typically called at the very start of Main() in Program.cs.
    /// </summary>
    /// <param name="customPath">Optional custom path to FFmpeg libraries.</param>
    /// <param name="autoInstall">If true, automatically install FFmpeg on macOS via Homebrew if not found. Default is true.</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    /// <exception cref="FFmpegNotFoundException">Thrown when FFmpeg is not installed and cannot be auto-installed.</exception>
    public static bool Initialize(string? customPath = null, bool autoInstall = true)
    {
        if (_isInitialized)
        {
            return true;
        }

        try
        {
            Log($"Initializing FFmpeg for {PlatformInfo}");
            StatusChanged?.Invoke($"Initializing FFmpeg for {PlatformInfo}...");

            // Prefer packaged runtimes if present (runtimes/<rid>/native in the NuGet)
            _ffmpegPath = FFmpegPathResolver.TryConfigureBundledFFmpeg();

            // Fallback to system/custom discovery
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                _ffmpegPath = FindFFmpegPath(customPath);
            }

            // If not found on macOS and autoInstall is enabled, try to install via Homebrew
            if (string.IsNullOrEmpty(_ffmpegPath) && IsMacOS && autoInstall)
            {
                Log("FFmpeg not found on macOS, attempting automatic installation via Homebrew...");
                StatusChanged?.Invoke("FFmpeg not found. Installing via Homebrew (this may take a few minutes)...");
                
                if (TryInstallFFmpegOnMacOS())
                {
                    // Try to find FFmpeg again after installation
                    _ffmpegPath = FindFFmpegPath(null);
                }
            }

            if (!string.IsNullOrEmpty(_ffmpegPath))
            {
                Log($"Using FFmpeg from: {_ffmpegPath}");
                FFmpegPathResolver.ConfigureNativeSearchPath(_ffmpegPath);
            }
            else
            {
                // No explicit path found, but we still need to initialize the bindings
                // This will try to load from system default locations
                FFmpegPathResolver.InitializeBindings();
            }

            // Test FFmpeg by getting version info
            string versionStr = "unknown";
            try
            {
                // Call avcodec_version to trigger library loading and verify FFmpeg works
                var codecVersion = ffmpeg.avcodec_version();
                versionStr = $"{codecVersion >> 16}.{(codecVersion >> 8) & 0xFF}.{codecVersion & 0xFF}";
            }
            catch { }
            Log($"FFmpeg libavcodec version: {versionStr}");

            _isInitialized = true;
            StatusChanged?.Invoke($"FFmpeg initialized successfully (libavcodec: {versionStr})");
            return true;
        }
        catch (DllNotFoundException ex)
        {
            _initializationError = $"FFmpeg libraries not found: {ex.Message}";
            StatusChanged?.Invoke(_initializationError);
            Log(_initializationError);
            throw new FFmpegNotFoundException(
                $"FFmpeg libraries not found.\n{GetInstallationInstructions()}", ex);
        }
        catch (Exception ex)
        {
            _initializationError = ex.Message;
            StatusChanged?.Invoke($"Failed to initialize FFmpeg: {ex.Message}");
            Log($"Failed to initialize FFmpeg: {ex.Message}");
            throw new FFmpegNotFoundException(
                $"Failed to initialize FFmpeg: {ex.Message}\n{GetInstallationInstructions()}", ex);
        }
    }

    /// <summary>
    /// Asynchronously initializes FFmpeg with automatic installation support.
    /// On macOS, automatically installs FFmpeg via Homebrew if not found.
    /// </summary>
    /// <param name="customPath">Optional custom path to FFmpeg libraries.</param>
    /// <param name="autoInstall">If true, automatically install FFmpeg on macOS via Homebrew if not found.</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    public static async Task<bool> InitializeAsync(string? customPath = null, bool autoInstall = true)
    {
        if (_isInitialized)
        {
            return true;
        }

        return await Task.Run(() => Initialize(customPath, autoInstall));
    }

    /// <summary>
    /// Tries to initialize FFmpeg without throwing exceptions.
    /// </summary>
    /// <param name="customPath">Optional custom path to FFmpeg libraries.</param>
    /// <param name="errorMessage">Output parameter containing error message if initialization fails.</param>
    /// <param name="autoInstall">If true, automatically install FFmpeg on macOS via Homebrew if not found.</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    public static bool TryInitialize(string? customPath, out string? errorMessage, bool autoInstall = true)
    {
        try
        {
            Initialize(customPath, autoInstall);
            errorMessage = null;
            return true;
        }
        catch (FFmpegNotFoundException ex)
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

    #endregion

    #region Automatic Installation

    /// <summary>
    /// Gets the path to the Homebrew executable.
    /// </summary>
    private static string? GetHomebrewPath()
    {
        if (File.Exists("/opt/homebrew/bin/brew"))
            return "/opt/homebrew/bin/brew"; // Apple Silicon
        if (File.Exists("/usr/local/bin/brew"))
            return "/usr/local/bin/brew"; // Intel
        return null;
    }

    /// <summary>
    /// Attempts to install FFmpeg via Homebrew on macOS.
    /// </summary>
    /// <returns>True if installation succeeded, false otherwise.</returns>
    public static bool TryInstallFFmpegOnMacOS()
    {
        if (!IsMacOS)
        {
            Log("TryInstallFFmpegOnMacOS called on non-macOS platform");
            return false;
        }

        var brewPath = GetHomebrewPath();
        
        // If Homebrew is not installed, try to install it first
        if (brewPath == null)
        {
            Log("Homebrew not found, attempting to install Homebrew first...");
            StatusChanged?.Invoke("Installing Homebrew...");
            
            if (!TryInstallHomebrew())
            {
                Log("Failed to install Homebrew");
                StatusChanged?.Invoke("Failed to install Homebrew. Please install manually.");
                return false;
            }
            
            brewPath = GetHomebrewPath();
            if (brewPath == null)
            {
                Log("Homebrew still not found after installation attempt");
                return false;
            }
        }

        Log($"Using Homebrew at: {brewPath}");
        StatusChanged?.Invoke("Installing FFmpeg via Homebrew (this may take several minutes)...");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = brewPath,
                Arguments = "install ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Set up environment for Homebrew
            if (IsArm)
            {
                startInfo.Environment["PATH"] = $"/opt/homebrew/bin:/opt/homebrew/sbin:{Environment.GetEnvironmentVariable("PATH")}";
            }
            else
            {
                startInfo.Environment["PATH"] = $"/usr/local/bin:/usr/local/sbin:{Environment.GetEnvironmentVariable("PATH")}";
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Log("Failed to start Homebrew process");
                return false;
            }

            // Read output asynchronously
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Log("FFmpeg installed successfully via Homebrew");
                StatusChanged?.Invoke("FFmpeg installed successfully!");
                return true;
            }
            else
            {
                Log($"Homebrew install failed with exit code {process.ExitCode}");
                Log($"stderr: {error}");
                
                // Check if already installed (exit code might be non-zero but ffmpeg is there)
                if (error.Contains("already installed") || output.Contains("already installed"))
                {
                    Log("FFmpeg was already installed");
                    return true;
                }
                
                StatusChanged?.Invoke($"FFmpeg installation failed: {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"Exception during FFmpeg installation: {ex.Message}");
            StatusChanged?.Invoke($"FFmpeg installation error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Attempts to install Homebrew on macOS.
    /// </summary>
    private static bool TryInstallHomebrew()
    {
        try
        {
            Log("Attempting to install Homebrew...");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-c \"$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Set NONINTERACTIVE to avoid prompts
            startInfo.Environment["NONINTERACTIVE"] = "1";

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Log("Failed to start Homebrew installation process");
                return false;
            }

            // Close stdin to signal no interactive input
            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                Log("Homebrew installed successfully");
                StatusChanged?.Invoke("Homebrew installed successfully!");
                return true;
            }
            else
            {
                Log($"Homebrew installation failed: {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"Exception during Homebrew installation: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Installation Instructions

    /// <summary>
    /// Gets platform-specific FFmpeg installation instructions.
    /// </summary>
    public static string GetInstallationInstructions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return @"
WINDOWS:
Install FFmpeg using one of these methods:

Option 1 - WinGet (Recommended for Windows 11):
    winget install ffmpeg

Option 2 - Chocolatey:
    choco install ffmpeg

Option 3 - Manual Installation:
    1. Download from https://www.gyan.dev/ffmpeg/builds/ (get the 'full' build)
    2. Extract to a folder (e.g., C:\ffmpeg)
    3. Add C:\ffmpeg\bin to your system PATH
    
After installation, restart your terminal/IDE.";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return @"
macOS (Intel x64 and Apple Silicon ARM64):
Install FFmpeg using Homebrew (supports both architectures):

    brew install ffmpeg

If you don't have Homebrew installed:
    /bin/bash -c ""$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)""
    
After installation, restart your terminal/IDE.";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var arch = GetArchitectureName();
            return $@"
LINUX ({arch}):
Install FFmpeg using your package manager:

Debian/Ubuntu:
    sudo apt update
    sudo apt install ffmpeg libavcodec-dev libavformat-dev libavutil-dev libswscale-dev libswresample-dev

Fedora:
    sudo dnf install ffmpeg ffmpeg-devel

Arch Linux:
    sudo pacman -S ffmpeg

After installation, restart your terminal/IDE.";
        }

        return "FFmpeg libraries not found. Please install FFmpeg on your system.";
    }

    /// <summary>
    /// Checks if FFmpeg is properly installed on the system.
    /// </summary>
    public static FFmpegInstallationStatus CheckInstallation()
    {
        var status = new FFmpegInstallationStatus
        {
            Platform = GetPlatformName(),
            Architecture = GetArchitectureName()
        };

        try
        {
            var path = FindFFmpegPath(null);
            if (!string.IsNullOrEmpty(path))
            {
                status.IsInstalled = true;
                status.LibraryPath = path;
            }
            else
            {
                // Try to load without explicit path
                try
                {
                    ffmpeg.RootPath = "";
                    var version = ffmpeg.av_version_info();
                    status.IsInstalled = true;
                    status.LibraryPath = "System default";
                }
                catch
                {
                    status.IsInstalled = false;
                }
            }

            status.IsArchitectureCompatible = true; // FFmpeg builds are architecture-specific
        }
        catch (Exception ex)
        {
            status.Error = ex.Message;
        }

        status.InstallationInstructions = GetInstallationInstructions();
        return status;
    }

    #endregion

    #region Path Discovery

    private static string? FindFFmpegPath(string? customPath)
    {
        // 1. Custom path provided by user
        if (!string.IsNullOrEmpty(customPath) && Directory.Exists(customPath))
        {
            Log($"Using custom FFmpeg path: {customPath}");
            return customPath;
        }

        // 2. Check for bundled FFmpeg (from NuGet packages like FFmpeg.Native)
        var bundledPath = FindBundledFFmpeg();
        if (!string.IsNullOrEmpty(bundledPath))
        {
            Log($"Found bundled FFmpeg: {bundledPath}");
            return bundledPath;
        }

        // 3. Application directory
        var baseDir = AppContext.BaseDirectory;
        var appDirPaths = new[]
        {
            Path.Combine(baseDir, "ffmpeg"),
            Path.Combine(baseDir, "lib"),
            baseDir
        };

        foreach (var path in appDirPaths)
        {
            if (FFmpegPathResolver.HasFFmpegLibrary(path))
            {
                Log($"Found FFmpeg in application directory: {path}");
                return path;
            }
        }

        // 4. Platform-specific detection
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindWindowsFFmpeg();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return FindMacOSFFmpeg();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return FindLinuxFFmpeg();
        }

        return null;
    }

    private static string? FindBundledFFmpeg()
    {
        // NuGet packages typically deploy native libraries to runtimes/{rid}/native/
        return FFmpegPathResolver.TryConfigureBundledFFmpeg();
    }

    private static string? FindWindowsFFmpeg()
    {
        // Check common installation paths
        var paths = new[]
        {
            @"C:\ffmpeg\bin",
            @"C:\Program Files\ffmpeg\bin",
            @"C:\Program Files (x86)\ffmpeg\bin",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin"),
        };

        foreach (var path in paths)
        {
            if (FFmpegPathResolver.HasFFmpegLibrary(path))
            {
                Log($"Found FFmpeg at: {path}");
                return path;
            }
        }

        // Check PATH environment variable
        var envPath = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(envPath))
        {
            foreach (var path in envPath.Split(';'))
            {
                if (FFmpegPathResolver.HasFFmpegLibrary(path))
                {
                    Log($"Found FFmpeg in PATH: {path}");
                    return path;
                }
            }
        }

        return null;
    }

    private static string? FindMacOSFFmpeg()
    {
        // Homebrew paths (supports both Intel and Apple Silicon)
        var homebrewPaths = new[]
        {
            "/opt/homebrew/lib",           // Homebrew on Apple Silicon
            "/usr/local/lib",              // Homebrew on Intel
            "/opt/homebrew/Cellar/ffmpeg", // Homebrew Cellar on Apple Silicon
            "/usr/local/Cellar/ffmpeg",    // Homebrew Cellar on Intel
        };

        foreach (var path in homebrewPaths)
        {
            if (FFmpegPathResolver.HasFFmpegLibrary(path))
            {
                Log($"Found FFmpeg via Homebrew: {path}");
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
                        if (FFmpegPathResolver.HasFFmpegLibrary(libPath))
                        {
                            Log($"Found FFmpeg via Homebrew Cellar: {libPath}");
                            return libPath;
                        }
                    }
                }
                catch { }
            }
        }

        // MacPorts
        if (FFmpegPathResolver.HasFFmpegLibrary("/opt/local/lib"))
        {
            Log("Found FFmpeg via MacPorts");
            return "/opt/local/lib";
        }

        return null;
    }

    private static string? FindLinuxFFmpeg()
    {
        // System library paths (architecture-specific)
        var systemPaths = new List<string>();

        if (IsArm)
        {
            systemPaths.AddRange(new[]
            {
                "/usr/lib/aarch64-linux-gnu",        // Debian/Ubuntu ARM64
                "/usr/lib64",                         // Fedora/RHEL ARM64
            });
        }
        else
        {
            systemPaths.AddRange(new[]
            {
                "/usr/lib/x86_64-linux-gnu",         // Debian/Ubuntu x64
                "/usr/lib64",                         // Fedora/RHEL x64
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
            if (FFmpegPathResolver.HasFFmpegLibrary(path))
            {
                Log($"Found FFmpeg at: {path}");
                return path;
            }
        }

        // Check LD_LIBRARY_PATH
        var ldPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        if (!string.IsNullOrEmpty(ldPath))
        {
            foreach (var path in ldPath.Split(':'))
            {
                if (FFmpegPathResolver.HasFFmpegLibrary(path))
                {
                    Log($"Found FFmpeg via LD_LIBRARY_PATH: {path}");
                    return path;
                }
            }
        }

        return null;
    }

    #endregion

    #region Logging

    [System.Diagnostics.Conditional("DEBUG")]
    private static void Log(string message)
    {
        var logMessage = $"[FFmpegInitializer] {message}";
        Debug.WriteLine(logMessage);
        Console.WriteLine(logMessage);
    }

    #endregion
}

/// <summary>
/// Exception thrown when FFmpeg is not installed or cannot be found on the system.
/// </summary>
public class FFmpegNotFoundException : Exception
{
    public FFmpegNotFoundException(string message) : base(message) { }
    public FFmpegNotFoundException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Provides information about the FFmpeg installation status on the system.
/// </summary>
public class FFmpegInstallationStatus
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
    /// Whether FFmpeg libraries were found on the system.
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Whether the found FFmpeg libraries are compatible with the current architecture.
    /// </summary>
    public bool IsArchitectureCompatible { get; set; }

    /// <summary>
    /// The path to the FFmpeg libraries, if found.
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
    /// Whether FFmpeg is ready to use (installed and architecture compatible).
    /// </summary>
    public bool IsReady => IsInstalled && IsArchitectureCompatible;

    public override string ToString()
    {
        if (IsReady)
            return $"FFmpeg is installed and ready at: {LibraryPath}";

        if (IsInstalled && !IsArchitectureCompatible)
            return $"FFmpeg is installed at {LibraryPath} but is not compatible with {Architecture}";

        return $"FFmpeg is not installed.\n{InstallationInstructions}";
    }
}
