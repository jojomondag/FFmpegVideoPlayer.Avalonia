using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace Avalonia.VlcVideoPlayer;

/// <summary>
/// Handles VLC initialization and environment setup for embedded VLC libraries.
/// Automatically downloads and sets up the correct VLC libraries for the current platform and architecture.
/// Supports Windows (x64/ARM64), macOS (x64/ARM64), and Linux (x64/ARM64).
/// </summary>
public static class VlcInitializer
{
    // P/Invoke to set environment variable at the native level (works on macOS/Linux)
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    private static bool _isInitialized;
    private static string? _vlcLibPath;

    // VLC version to download
    private const string VLC_VERSION = "3.0.21";

    /// <summary>
    /// Gets whether VLC has been initialized.
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the path to the VLC library directory being used.
    /// </summary>
    public static string? VlcLibPath => _vlcLibPath;

    /// <summary>
    /// Gets the detected platform and architecture.
    /// </summary>
    public static string PlatformInfo => $"{GetPlatformName()}-{GetArchitectureName()}";

    /// <summary>
    /// Event raised when download progress changes (0-100).
    /// </summary>
    public static event Action<int>? DownloadProgressChanged;

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

    #region VLC Download URLs

    private static string GetVlcDownloadUrl()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: x64 available, ARM64 uses x64 via emulation
            if (IsArm)
            {
                Log("Windows ARM64 detected - using x64 VLC (runs via emulation)");
            }
            return $"https://get.videolan.org/vlc/{VLC_VERSION}/win64/vlc-{VLC_VERSION}-win64.zip";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: Both ARM64 (Apple Silicon) and Intel builds available
            if (IsArm)
            {
                return $"https://get.videolan.org/vlc/{VLC_VERSION}/macosx/vlc-{VLC_VERSION}-arm64.dmg";
            }
            return $"https://get.videolan.org/vlc/{VLC_VERSION}/macosx/vlc-{VLC_VERSION}-intel64.dmg";
        }

        return string.Empty;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes VLC with embedded or system libraries.
    /// Call this method BEFORE creating any Avalonia windows or VLC instances.
    /// Typically called at the very start of Main() in Program.cs.
    /// </summary>
    /// <param name="customVlcPath">Optional custom path to VLC libraries.</param>
    /// <param name="autoDownload">If true, automatically download VLC if not found.</param>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    public static bool Initialize(string? customVlcPath = null, bool autoDownload = true)
    {
        if (_isInitialized)
        {
            return true;
        }

        try
        {
            Log($"Initializing VLC for {PlatformInfo}");

            // First, try to find existing VLC installation
            _vlcLibPath = FindVlcLibPath(customVlcPath);

            // Validate architecture compatibility on macOS
            if (_vlcLibPath != null && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (!ValidateMacOsArchitecture(_vlcLibPath))
                {
                    Log("Existing VLC has wrong architecture, will try to download correct version");
                    _vlcLibPath = null;
                }
            }

            if (_vlcLibPath != null)
            {
                return InitializeWithPath(_vlcLibPath);
            }

            // VLC not found - try auto-download if enabled
            if (autoDownload)
            {
                StatusChanged?.Invoke("VLC libraries not found. Starting download...");
                Log("VLC not found. Attempting to download...");

                var downloadTask = DownloadVlcAsync();
                downloadTask.Wait();

                if (downloadTask.Result)
                {
                    _vlcLibPath = FindVlcLibPath(null);
                    if (_vlcLibPath != null)
                    {
                        return InitializeWithPath(_vlcLibPath);
                    }
                }
            }

            // Fall back to system VLC
            StatusChanged?.Invoke("Using system default VLC");
            Log("Using system default VLC");
            Core.Initialize();
            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Failed to initialize VLC: {ex.Message}");
            Log($"Failed to initialize VLC: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Asynchronously initializes VLC with automatic download support.
    /// </summary>
    public static async Task<bool> InitializeAsync(string? customVlcPath = null, bool autoDownload = true)
    {
        if (_isInitialized)
        {
            return true;
        }

        try
        {
            Log($"Initializing VLC for {PlatformInfo}");

            _vlcLibPath = FindVlcLibPath(customVlcPath);

            // Validate architecture compatibility on macOS
            if (_vlcLibPath != null && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (!ValidateMacOsArchitecture(_vlcLibPath))
                {
                    Log("Existing VLC has wrong architecture, will try to download correct version");
                    _vlcLibPath = null;
                }
            }

            if (_vlcLibPath != null)
            {
                return InitializeWithPath(_vlcLibPath);
            }

            if (autoDownload)
            {
                StatusChanged?.Invoke("VLC libraries not found. Starting download...");

                if (await DownloadVlcAsync())
                {
                    _vlcLibPath = FindVlcLibPath(null);
                    if (_vlcLibPath != null)
                    {
                        return InitializeWithPath(_vlcLibPath);
                    }
                }
            }

            Core.Initialize();
            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Failed to initialize VLC: {ex.Message}");
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
        // This is critical on macOS where DYLD_LIBRARY_PATH must be set
        SetLibraryPath(libPath);

        StatusChanged?.Invoke($"Using VLC from: {libPath}");
        Log($"Using VLC from: {libPath}");

        Core.Initialize(libPath);
        _isInitialized = true;
        return true;
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

        var libvlcPath = Path.Combine(libPath, "libvlc.dylib");
        if (!File.Exists(libvlcPath))
        {
            // Try parent directory
            var parentLib = Path.Combine(Path.GetDirectoryName(libPath) ?? "", "lib", "libvlc.dylib");
            if (File.Exists(parentLib))
            {
                libvlcPath = parentLib;
            }
            else
            {
                return true; // Can't validate, assume OK
            }
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
                Log($"Architecture mismatch: VLC is not {expectedArch}. Output: {output.Trim()}");
            }

            return isCompatible;
        }
        catch (Exception ex)
        {
            Log($"Could not validate architecture: {ex.Message}");
            return true; // Assume OK if we can't check
        }
    }

    #endregion

    #region Download Methods

    /// <summary>
    /// Downloads VLC libraries for the current platform and architecture.
    /// </summary>
    public static async Task<bool> DownloadVlcAsync()
    {
        try
        {
            var vlcDir = GetVlcDirectory();

            // Check if already downloaded with correct architecture
            if (Directory.Exists(vlcDir))
            {
                var libDir = Path.Combine(vlcDir, "lib");
                if (Directory.Exists(libDir))
                {
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || ValidateMacOsArchitecture(libDir))
                    {
                        StatusChanged?.Invoke("VLC already downloaded");
                        return true;
                    }
                    // Wrong architecture, delete and re-download
                    Log("Existing VLC has wrong architecture, re-downloading...");
                    Directory.Delete(vlcDir, true);
                }
            }

            Directory.CreateDirectory(vlcDir);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await DownloadVlcWindowsAsync(vlcDir);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return await DownloadVlcMacOsAsync(vlcDir);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await DownloadVlcLinuxAsync(vlcDir);
            }

            return false;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Download failed: {ex.Message}");
            Log($"Download failed: {ex.Message}");
            return false;
        }
    }

    private static string GetVlcDirectory()
    {
        // Include architecture in directory name to handle multiple architectures
        var archSuffix = GetArchitectureName();
        return Path.Combine(AppContext.BaseDirectory, "vlc", archSuffix);
    }

    private static async Task<bool> DownloadVlcWindowsAsync(string vlcDir)
    {
        var archInfo = IsArm ? "ARM64 (using x64 via emulation)" : "x64";
        StatusChanged?.Invoke($"Downloading VLC for Windows {archInfo}...");
        Log($"Downloading VLC for Windows {archInfo}");

        using var httpClient = CreateHttpClient();
        var url = GetVlcDownloadUrl();
        var zipPath = Path.Combine(vlcDir, "vlc.zip");

        await DownloadFileAsync(httpClient, url, zipPath);

        StatusChanged?.Invoke("Extracting VLC...");

        // Extract the ZIP
        var extractDir = Path.Combine(vlcDir, "extract");
        ZipFile.ExtractToDirectory(zipPath, extractDir, true);

        // Find the extracted VLC folder and move contents
        var vlcExtractedDir = Directory.GetDirectories(extractDir).FirstOrDefault();
        if (vlcExtractedDir != null)
        {
            CopyDirectory(vlcExtractedDir, vlcDir);
        }

        // Cleanup
        File.Delete(zipPath);
        Directory.Delete(extractDir, true);

        // Handle plugins folder naming
        var plugins64Dir = Path.Combine(vlcDir, "plugins64");
        var pluginsDir = Path.Combine(vlcDir, "plugins");
        if (Directory.Exists(plugins64Dir) && !Directory.Exists(pluginsDir))
        {
            Directory.Move(plugins64Dir, pluginsDir);
        }

        // Create lib folder structure
        var libDir = Path.Combine(vlcDir, "lib");
        if (!Directory.Exists(libDir))
        {
            Directory.CreateDirectory(libDir);
            var libvlcDll = Path.Combine(vlcDir, "libvlc.dll");
            var libvlccoreDll = Path.Combine(vlcDir, "libvlccore.dll");
            if (File.Exists(libvlcDll)) File.Copy(libvlcDll, Path.Combine(libDir, "libvlc.dll"), true);
            if (File.Exists(libvlccoreDll)) File.Copy(libvlccoreDll, Path.Combine(libDir, "libvlccore.dll"), true);
        }

        StatusChanged?.Invoke("VLC downloaded and extracted successfully");
        return true;
    }

    private static async Task<bool> DownloadVlcMacOsAsync(string vlcDir)
    {
        var archInfo = IsArm ? "Apple Silicon (ARM64)" : "Intel (x64)";
        StatusChanged?.Invoke($"Setting up VLC for macOS {archInfo}...");
        Log($"Setting up VLC for macOS {archInfo}");

        // First check if VLC.app exists and has correct architecture
        var vlcAppPath = "/Applications/VLC.app/Contents/MacOS";
        if (Directory.Exists(vlcAppPath))
        {
            var vlcAppLibPath = Path.Combine(vlcAppPath, "lib");
            if (ValidateMacOsArchitecture(vlcAppLibPath))
            {
                StatusChanged?.Invoke("Found compatible VLC.app - copying libraries...");
                return CopyFromVlcApp(vlcDir, vlcAppPath);
            }
            else
            {
                Log("VLC.app has wrong architecture, will download correct version");
            }
        }

        // Download DMG and extract
        using var httpClient = CreateHttpClient();
        var url = GetVlcDownloadUrl();
        var dmgPath = Path.Combine(vlcDir, "vlc.dmg");

        StatusChanged?.Invoke($"Downloading VLC for macOS {archInfo}...");
        await DownloadFileAsync(httpClient, url, dmgPath);

        StatusChanged?.Invoke("Extracting VLC from DMG...");

        // Mount DMG and copy files
        var success = await ExtractFromDmgAsync(dmgPath, vlcDir);

        // Cleanup DMG
        if (File.Exists(dmgPath))
        {
            File.Delete(dmgPath);
        }

        if (success)
        {
            StatusChanged?.Invoke("VLC downloaded and extracted successfully");
        }

        return success;
    }

    private static async Task<bool> ExtractFromDmgAsync(string dmgPath, string vlcDir)
    {
        try
        {
            // Create a temporary mount point
            var mountPoint = Path.Combine(Path.GetTempPath(), $"vlc_mount_{Guid.NewGuid():N}");
            Directory.CreateDirectory(mountPoint);

            try
            {
                // Mount the DMG
                Log($"Mounting DMG: {dmgPath}");
                var mountResult = await RunProcessAsync("hdiutil", $"attach \"{dmgPath}\" -mountpoint \"{mountPoint}\" -nobrowse -quiet");
                if (!mountResult.Success)
                {
                    Log($"Failed to mount DMG: {mountResult.Error}");
                    return await FallbackMacOsDownload(vlcDir);
                }

                try
                {
                    // Find VLC.app in the mounted DMG
                    var vlcAppInDmg = Path.Combine(mountPoint, "VLC.app", "Contents", "MacOS");
                    if (!Directory.Exists(vlcAppInDmg))
                    {
                        // Try to find it
                        var vlcApps = Directory.GetDirectories(mountPoint, "VLC.app", SearchOption.TopDirectoryOnly);
                        if (vlcApps.Length > 0)
                        {
                            vlcAppInDmg = Path.Combine(vlcApps[0], "Contents", "MacOS");
                        }
                    }

                    if (Directory.Exists(vlcAppInDmg))
                    {
                        return CopyFromVlcApp(vlcDir, vlcAppInDmg);
                    }
                    else
                    {
                        Log($"Could not find VLC.app in DMG at {mountPoint}");
                        return false;
                    }
                }
                finally
                {
                    // Unmount the DMG
                    await RunProcessAsync("hdiutil", $"detach \"{mountPoint}\" -quiet -force");
                }
            }
            finally
            {
                // Cleanup mount point
                if (Directory.Exists(mountPoint))
                {
                    try { Directory.Delete(mountPoint, true); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"DMG extraction failed: {ex.Message}");
            return await FallbackMacOsDownload(vlcDir);
        }
    }

    private static bool CopyFromVlcApp(string vlcDir, string vlcAppPath)
    {
        try
        {
            var libDir = Path.Combine(vlcDir, "lib");
            var pluginsDir = Path.Combine(vlcDir, "plugins");

            Directory.CreateDirectory(libDir);
            Directory.CreateDirectory(pluginsDir);

            // Copy lib folder
            var srcLibDir = Path.Combine(vlcAppPath, "lib");
            if (Directory.Exists(srcLibDir))
            {
                CopyDirectory(srcLibDir, libDir);
                Log($"Copied lib folder from {srcLibDir}");
            }

            // Copy plugins folder
            var srcPluginsDir = Path.Combine(vlcAppPath, "plugins");
            if (Directory.Exists(srcPluginsDir))
            {
                CopyDirectory(srcPluginsDir, pluginsDir);
                Log($"Copied plugins folder from {srcPluginsDir}");
            }

            StatusChanged?.Invoke("VLC libraries copied successfully");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to copy VLC libraries: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> FallbackMacOsDownload(string vlcDir)
    {
        var arch = IsArm ? "Apple Silicon" : "Intel";
        var message = $"Please install VLC ({arch}) from https://www.videolan.org/vlc/download-macosx.html";
        StatusChanged?.Invoke(message);
        Log(message);

        // Try to open the download page
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = "https://www.videolan.org/vlc/download-macosx.html",
                UseShellExecute = false
            });
        }
        catch { }

        return false;
    }

    private static async Task<bool> DownloadVlcLinuxAsync(string vlcDir)
    {
        var arch = GetArchitectureName();
        StatusChanged?.Invoke($"Detecting Linux VLC installation for {arch}...");
        Log($"Linux {arch} detected");

        // On Linux, we should use the system package manager
        // Check if VLC is already installed
        var vlcInstalled = await CheckLinuxVlcInstalledAsync();
        if (vlcInstalled)
        {
            StatusChanged?.Invoke("VLC is installed via system package manager");
            return true;
        }

        // Provide instructions based on distribution
        var distro = await DetectLinuxDistroAsync();
        var installCmd = distro switch
        {
            "debian" or "ubuntu" => "sudo apt install vlc libvlc-dev",
            "fedora" => "sudo dnf install vlc vlc-devel",
            "arch" => "sudo pacman -S vlc",
            "opensuse" => "sudo zypper install vlc vlc-devel",
            _ => "Please install VLC using your distribution's package manager"
        };

        StatusChanged?.Invoke($"Please install VLC: {installCmd}");
        Log($"Linux VLC installation command: {installCmd}");

        // Try to find system VLC paths for different architectures
        var systemPaths = GetLinuxLibraryPaths();
        foreach (var path in systemPaths)
        {
            if (File.Exists(Path.Combine(path, "libvlc.so")) ||
                File.Exists(Path.Combine(path, "libvlc.so.5")))
            {
                // Create symlink or reference to system VLC
                var libDir = Path.Combine(vlcDir, "lib");
                Directory.CreateDirectory(libDir);

                // Write a marker file indicating system VLC should be used
                File.WriteAllText(Path.Combine(vlcDir, "use_system_vlc"), path);
                return true;
            }
        }

        return false;
    }

    private static string[] GetLinuxLibraryPaths()
    {
        var paths = new List<string>();

        if (IsArm)
        {
            paths.AddRange(new[]
            {
                "/usr/lib/aarch64-linux-gnu",
                "/usr/lib64",
                "/usr/lib"
            });
        }
        else
        {
            paths.AddRange(new[]
            {
                "/usr/lib/x86_64-linux-gnu",
                "/usr/lib64",
                "/usr/lib"
            });
        }

        // Add common paths
        paths.Add("/usr/local/lib");

        return paths.ToArray();
    }

    private static async Task<bool> CheckLinuxVlcInstalledAsync()
    {
        var result = await RunProcessAsync("which", "vlc");
        return result.Success && !string.IsNullOrEmpty(result.Output);
    }

    private static async Task<string> DetectLinuxDistroAsync()
    {
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                var content = await File.ReadAllTextAsync("/etc/os-release");
                if (content.Contains("ubuntu", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("debian", StringComparison.OrdinalIgnoreCase))
                    return "debian";
                if (content.Contains("fedora", StringComparison.OrdinalIgnoreCase))
                    return "fedora";
                if (content.Contains("arch", StringComparison.OrdinalIgnoreCase))
                    return "arch";
                if (content.Contains("opensuse", StringComparison.OrdinalIgnoreCase))
                    return "opensuse";
            }
        }
        catch { }

        return "unknown";
    }

    #endregion

    #region Helper Methods

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(30);
        client.DefaultRequestHeaders.Add("User-Agent", "Avalonia.VlcVideoPlayer");
        return client;
    }

    private static async Task DownloadFileAsync(HttpClient httpClient, string url, string destinationPath)
    {
        Log($"Downloading from: {url}");

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var fileStream = File.Create(destinationPath);

        var buffer = new byte[81920];
        var bytesRead = 0L;
        int read;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read);
            bytesRead += read;

            if (totalBytes > 0)
            {
                var progress = (int)((bytesRead * 100) / totalBytes);
                DownloadProgressChanged?.Invoke(progress);
            }
        }

        Log($"Download complete: {destinationPath}");
    }

    private static async Task<(bool Success, string Output, string Error)> RunProcessAsync(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (false, "", "Failed to start process");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

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
            // On macOS, DYLD_LIBRARY_PATH is needed for the dynamic linker to find libvlc.dylib
            var existingPath = Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH");
            var newPath = string.IsNullOrEmpty(existingPath) ? libPath : $"{libPath}:{existingPath}";
            setenv("DYLD_LIBRARY_PATH", newPath, 1);

            // Also set DYLD_FALLBACK_LIBRARY_PATH as a fallback mechanism
            var existingFallback = Environment.GetEnvironmentVariable("DYLD_FALLBACK_LIBRARY_PATH");
            var newFallback = string.IsNullOrEmpty(existingFallback) ? libPath : $"{libPath}:{existingFallback}";
            setenv("DYLD_FALLBACK_LIBRARY_PATH", newFallback, 1);

            Log($"Set DYLD_LIBRARY_PATH to: {newPath}");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // On Linux, LD_LIBRARY_PATH is used
            var existingPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            var newPath = string.IsNullOrEmpty(existingPath) ? libPath : $"{libPath}:{existingPath}";
            setenv("LD_LIBRARY_PATH", newPath, 1);
            Log($"Set LD_LIBRARY_PATH to: {newPath}");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, add to PATH
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!existingPath.Contains(libPath))
            {
                Environment.SetEnvironmentVariable("PATH", $"{libPath};{existingPath}");
                Log($"Added to PATH: {libPath}");
            }
        }
    }

    private static string? FindPluginPath(string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            var customPluginPath = Path.Combine(customPath, "plugins");
            if (Directory.Exists(customPluginPath))
                return customPluginPath;
        }

        var baseDir = AppContext.BaseDirectory;
        var archDir = GetArchitectureName();

        // Check architecture-specific embedded path
        var embeddedArchPath = Path.Combine(baseDir, "vlc", archDir, "plugins");
        if (Directory.Exists(embeddedArchPath))
            return embeddedArchPath;

        // Check for VideoLAN.LibVLC NuGet package structure
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var nugetPluginPath = Path.Combine(baseDir, "libvlc", "win-x64", "plugins");
            if (Directory.Exists(nugetPluginPath))
                return nugetPluginPath;
        }

        var embeddedPath = Path.Combine(baseDir, "vlc", "plugins");
        if (Directory.Exists(embeddedPath))
            return embeddedPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var macOsPluginPath = "/Applications/VLC.app/Contents/MacOS/plugins";
            if (Directory.Exists(macOsPluginPath))
                return macOsPluginPath;
        }

        return null;
    }

    private static string? FindVlcLibPath(string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            var customLibPath = Path.Combine(customPath, "lib");
            if (Directory.Exists(customLibPath))
                return customLibPath;

            if (Directory.Exists(customPath) && HasVlcLibrary(customPath))
                return customPath;
        }

        var baseDir = AppContext.BaseDirectory;
        var archDir = GetArchitectureName();

        // Check architecture-specific embedded path
        var embeddedArchLibPath = Path.Combine(baseDir, "vlc", archDir, "lib");
        if (Directory.Exists(embeddedArchLibPath))
            return embeddedArchLibPath;

        // Check for VideoLAN.LibVLC NuGet package structure
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var nugetLibPath = Path.Combine(baseDir, "libvlc", "win-x64");
            if (File.Exists(Path.Combine(nugetLibPath, "libvlc.dll")))
                return nugetLibPath;

            var embeddedVlcPath = Path.Combine(baseDir, "vlc");
            if (File.Exists(Path.Combine(embeddedVlcPath, "libvlc.dll")))
                return embeddedVlcPath;
        }

        var embeddedLibPath = Path.Combine(baseDir, "vlc", "lib");
        if (Directory.Exists(embeddedLibPath))
            return embeddedLibPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var macOsLibPath = "/Applications/VLC.app/Contents/MacOS/lib";
            if (Directory.Exists(macOsLibPath))
                return macOsLibPath;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linuxPaths = GetLinuxLibraryPaths();
            foreach (var path in linuxPaths)
            {
                if (HasVlcLibrary(path))
                    return path;
            }
        }

        return null;
    }

    private static bool HasVlcLibrary(string path)
    {
        return File.Exists(Path.Combine(path, "libvlc.dylib")) ||
               File.Exists(Path.Combine(path, "libvlc.so")) ||
               File.Exists(Path.Combine(path, "libvlc.so.5")) ||
               File.Exists(Path.Combine(path, "libvlc.dll"));
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[VlcInitializer] {message}");
    }

    #endregion
}
