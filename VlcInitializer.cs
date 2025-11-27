using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace Avalonia.VlcVideoPlayer;

/// <summary>
/// Handles VLC initialization and environment setup for embedded VLC libraries.
/// Can automatically download VLC libraries if not found.
/// </summary>
public static class VlcInitializer
{
    // P/Invoke to set environment variable at the native level (works on macOS/Linux)
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string name, string value, int overwrite);

    private static bool _isInitialized;
    private static string? _vlcLibPath;

    // VLC download URLs for different platforms (LibVLC 3.0.21)
    private const string VLC_VERSION = "3.0.21";
    private static readonly string VlcMacOsUrl = $"https://get.videolan.org/vlc/{VLC_VERSION}/macosx/vlc-{VLC_VERSION}-arm64.dmg";
    private static readonly string VlcMacOsIntelUrl = $"https://get.videolan.org/vlc/{VLC_VERSION}/macosx/vlc-{VLC_VERSION}-intel64.dmg";
    private static readonly string VlcWindowsUrl = $"https://get.videolan.org/vlc/{VLC_VERSION}/win64/vlc-{VLC_VERSION}-win64.zip";
    private static readonly string VlcLinuxUrl = $"https://get.videolan.org/vlc/{VLC_VERSION}/";

    /// <summary>
    /// Gets whether VLC has been initialized.
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the path to the VLC library directory being used.
    /// </summary>
    public static string? VlcLibPath => _vlcLibPath;

    /// <summary>
    /// Event raised when download progress changes.
    /// </summary>
    public static event Action<int>? DownloadProgressChanged;

    /// <summary>
    /// Event raised with status messages during initialization.
    /// </summary>
    public static event Action<string>? StatusChanged;

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
            // Set up plugin path environment variable
            var pluginPath = FindPluginPath(customVlcPath);
            if (pluginPath != null)
            {
                SetPluginPath(pluginPath);
            }

            // Find and initialize the VLC library
            _vlcLibPath = FindVlcLibPath(customVlcPath);

            if (_vlcLibPath != null)
            {
                StatusChanged?.Invoke($"Using VLC from: {_vlcLibPath}");
                Console.WriteLine($"[VlcInitializer] Using VLC from: {_vlcLibPath}");
                Core.Initialize(_vlcLibPath);
                _isInitialized = true;
                return true;
            }

            // VLC not found - try auto-download if enabled
            if (autoDownload)
            {
                StatusChanged?.Invoke("VLC libraries not found. Starting download...");
                Console.WriteLine("[VlcInitializer] VLC not found. Attempting to download...");
                
                var downloadTask = DownloadVlcAsync();
                downloadTask.Wait();
                
                if (downloadTask.Result)
                {
                    // Retry finding VLC after download
                    pluginPath = FindPluginPath(null);
                    if (pluginPath != null)
                    {
                        SetPluginPath(pluginPath);
                    }
                    
                    _vlcLibPath = FindVlcLibPath(null);
                    if (_vlcLibPath != null)
                    {
                        StatusChanged?.Invoke($"Using downloaded VLC from: {_vlcLibPath}");
                        Console.WriteLine($"[VlcInitializer] Using downloaded VLC from: {_vlcLibPath}");
                        Core.Initialize(_vlcLibPath);
                        _isInitialized = true;
                        return true;
                    }
                }
            }

            // Fall back to system VLC
            StatusChanged?.Invoke("Using system default VLC");
            Console.WriteLine("[VlcInitializer] Using system default VLC");
            Core.Initialize();
            _isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Failed to initialize VLC: {ex.Message}");
            Console.WriteLine($"[VlcInitializer] Failed to initialize VLC: {ex.Message}");
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
            var pluginPath = FindPluginPath(customVlcPath);
            if (pluginPath != null)
            {
                SetPluginPath(pluginPath);
            }

            _vlcLibPath = FindVlcLibPath(customVlcPath);

            if (_vlcLibPath != null)
            {
                StatusChanged?.Invoke($"Using VLC from: {_vlcLibPath}");
                Core.Initialize(_vlcLibPath);
                _isInitialized = true;
                return true;
            }

            if (autoDownload)
            {
                StatusChanged?.Invoke("VLC libraries not found. Starting download...");
                
                if (await DownloadVlcAsync())
                {
                    pluginPath = FindPluginPath(null);
                    if (pluginPath != null)
                    {
                        SetPluginPath(pluginPath);
                    }
                    
                    _vlcLibPath = FindVlcLibPath(null);
                    if (_vlcLibPath != null)
                    {
                        StatusChanged?.Invoke($"Using downloaded VLC from: {_vlcLibPath}");
                        Core.Initialize(_vlcLibPath);
                        _isInitialized = true;
                        return true;
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

    /// <summary>
    /// Downloads VLC libraries for the current platform.
    /// </summary>
    public static async Task<bool> DownloadVlcAsync()
    {
        try
        {
            var vlcDir = Path.Combine(AppContext.BaseDirectory, "vlc");
            
            // Check if already downloaded
            if (Directory.Exists(vlcDir) && Directory.Exists(Path.Combine(vlcDir, "lib")))
            {
                StatusChanged?.Invoke("VLC already downloaded");
                return true;
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
                StatusChanged?.Invoke("On Linux, please install VLC using your package manager: sudo apt install vlc");
                Console.WriteLine("[VlcInitializer] On Linux, please install VLC using your package manager");
                return false;
            }

            return false;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Download failed: {ex.Message}");
            Console.WriteLine($"[VlcInitializer] Download failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> DownloadVlcWindowsAsync(string vlcDir)
    {
        StatusChanged?.Invoke("Downloading VLC for Windows...");
        
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);
        
        var zipPath = Path.Combine(vlcDir, "vlc.zip");
        
        // Download the ZIP file
        using (var response = await httpClient.GetAsync(VlcWindowsUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(zipPath);
            
            var buffer = new byte[8192];
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
        }
        
        StatusChanged?.Invoke("Extracting VLC...");
        
        // Extract the ZIP
        var extractDir = Path.Combine(vlcDir, "extract");
        ZipFile.ExtractToDirectory(zipPath, extractDir, true);
        
        // Find the extracted VLC folder and move contents
        var vlcExtractedDir = Directory.GetDirectories(extractDir).FirstOrDefault();
        if (vlcExtractedDir != null)
        {
            // Copy lib and plugins folders
            var libSrc = vlcExtractedDir;
            CopyDirectory(libSrc, vlcDir);
        }
        
        // Cleanup
        File.Delete(zipPath);
        Directory.Delete(extractDir, true);
        
        // Rename the plugins64 folder to plugins if exists
        var plugins64Dir = Path.Combine(vlcDir, "plugins64");
        var pluginsDir = Path.Combine(vlcDir, "plugins");
        if (Directory.Exists(plugins64Dir) && !Directory.Exists(pluginsDir))
        {
            Directory.Move(plugins64Dir, pluginsDir);
        }
        
        // Create lib folder with libvlc.dll
        var libDir = Path.Combine(vlcDir, "lib");
        if (!Directory.Exists(libDir))
        {
            Directory.CreateDirectory(libDir);
            var libvlcDll = Path.Combine(vlcDir, "libvlc.dll");
            var libvlccoreDll = Path.Combine(vlcDir, "libvlccore.dll");
            if (File.Exists(libvlcDll)) File.Copy(libvlcDll, Path.Combine(libDir, "libvlc.dll"));
            if (File.Exists(libvlccoreDll)) File.Copy(libvlccoreDll, Path.Combine(libDir, "libvlccore.dll"));
        }
        
        StatusChanged?.Invoke("VLC downloaded and extracted successfully");
        return true;
    }

    private static async Task<bool> DownloadVlcMacOsAsync(string vlcDir)
    {
        StatusChanged?.Invoke("Downloading VLC for macOS...");
        
        // For macOS, we'll download from a GitHub release or use the app bundle approach
        // Since DMG extraction is complex, we'll provide instructions or use a tar.gz if available
        
        // Alternative: Check if VLC.app exists and copy from there
        var vlcAppPath = "/Applications/VLC.app/Contents/MacOS";
        if (Directory.Exists(vlcAppPath))
        {
            StatusChanged?.Invoke("Found VLC.app - copying libraries...");
            
            var libDir = Path.Combine(vlcDir, "lib");
            var pluginsDir = Path.Combine(vlcDir, "plugins");
            
            Directory.CreateDirectory(libDir);
            Directory.CreateDirectory(pluginsDir);
            
            // Copy lib folder
            var srcLibDir = Path.Combine(vlcAppPath, "lib");
            if (Directory.Exists(srcLibDir))
            {
                CopyDirectory(srcLibDir, libDir);
            }
            
            // Copy plugins folder
            var srcPluginsDir = Path.Combine(vlcAppPath, "plugins");
            if (Directory.Exists(srcPluginsDir))
            {
                CopyDirectory(srcPluginsDir, pluginsDir);
            }
            
            StatusChanged?.Invoke("VLC libraries copied from VLC.app");
            return true;
        }
        
        // If VLC.app not found, provide download instructions
        StatusChanged?.Invoke("VLC.app not found. Please install VLC from https://www.videolan.org/vlc/download-macosx.html");
        Console.WriteLine("[VlcInitializer] macOS: Please install VLC.app from https://www.videolan.org/vlc/");
        
        // Try to open the download page
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "open",
                Arguments = "https://www.videolan.org/vlc/download-macosx.html",
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
        
        return false;
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
        Console.WriteLine($"[VlcInitializer] Setting VLC_PLUGIN_PATH to: {pluginPath}");

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

    private static string? FindPluginPath(string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            var customPluginPath = Path.Combine(customPath, "plugins");
            if (Directory.Exists(customPluginPath))
            {
                return customPluginPath;
            }
        }

        var baseDir = AppContext.BaseDirectory;
        var embeddedPluginPath = Path.Combine(baseDir, "vlc", "plugins");
        if (Directory.Exists(embeddedPluginPath))
        {
            return embeddedPluginPath;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var macOsPluginPath = "/Applications/VLC.app/Contents/MacOS/plugins";
            if (Directory.Exists(macOsPluginPath))
            {
                return macOsPluginPath;
            }
        }

        return null;
    }

    private static string? FindVlcLibPath(string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            var customLibPath = Path.Combine(customPath, "lib");
            if (Directory.Exists(customLibPath))
            {
                return customLibPath;
            }
            if (Directory.Exists(customPath) &&
                (File.Exists(Path.Combine(customPath, "libvlc.dylib")) ||
                 File.Exists(Path.Combine(customPath, "libvlc.so")) ||
                 File.Exists(Path.Combine(customPath, "libvlc.dll"))))
            {
                return customPath;
            }
        }

        var baseDir = AppContext.BaseDirectory;
        
        // On Windows, check for libvlc.dll directly in the vlc folder first
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var embeddedVlcPath = Path.Combine(baseDir, "vlc");
            if (File.Exists(Path.Combine(embeddedVlcPath, "libvlc.dll")))
            {
                return embeddedVlcPath;
            }
        }
        
        var embeddedLibPath = Path.Combine(baseDir, "vlc", "lib");
        if (Directory.Exists(embeddedLibPath))
        {
            return embeddedLibPath;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var macOsLibPath = "/Applications/VLC.app/Contents/MacOS/lib";
            if (Directory.Exists(macOsLibPath))
            {
                return macOsLibPath;
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linuxPaths = new[]
            {
                "/usr/lib/x86_64-linux-gnu",
                "/usr/lib64",
                "/usr/lib"
            };

            foreach (var path in linuxPaths)
            {
                if (File.Exists(Path.Combine(path, "libvlc.so")))
                {
                    return path;
                }
            }
        }

        return null;
    }
}
