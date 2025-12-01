# VlcVideoPlayer.Avalonia

A self-contained VLC-based video player control for Avalonia UI with **embedded VLC libraries**.

[![NuGet](https://img.shields.io/nuget/v/VlcVideoPlayer.Avalonia.svg)](https://www.nuget.org/packages/VlcVideoPlayer.Avalonia/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

![Video Player Screenshot](https://raw.githubusercontent.com/jojomondag/Avalonia.VlcVideoPlayer/main/screenshot.png)

## Features

- 🎬 Full-featured video player control for Avalonia
- 📦 **VLC libraries included automatically** - no manual VLC installation required!
- 🎨 Clean, modern UI with Material Design icons
- 🖥️ Cross-platform (Windows, macOS, Linux)
- ⚡ Based on LibVLCSharp for maximum codec support
- 🎛️ Built-in controls: Play/Pause, Stop, Seek bar, Volume slider, Mute

## Installation

```bash
dotnet add package VlcVideoPlayer.Avalonia
```

That's it! The VLC native libraries for Windows are automatically included as a transitive dependency. For other platforms, see [Platform Support](#platform-support).

## Quick Start

### Step 1: Add Material Icons to App.axaml

**Important:** The video player uses Material Icons for its controls. You must add the `MaterialIconStyles` to your `App.axaml`:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="YourApp.App">
    <Application.Styles>
        <FluentTheme />
        <!-- Required for video player icons -->
        <materialIcons:MaterialIconStyles />
    </Application.Styles>
</Application>
```

### Step 2: Initialize VLC at Startup

In your `Program.cs` or `App.axaml.cs`, initialize VLC before creating any windows:

```csharp
using Avalonia.VlcVideoPlayer;

public class Program
{
    public static void Main(string[] args)
    {
        // Initialize VLC - must be called before creating windows
        VlcInitializer.Initialize();
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
```

### Step 3: Add the VideoPlayerControl to your Window

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vlc="clr-namespace:Avalonia.VlcVideoPlayer;assembly=Avalonia.VlcVideoPlayer"
        Title="My Video Player" Width="800" Height="600">
    
    <vlc:VideoPlayerControl x:Name="VideoPlayer" />
    
</Window>
```

### Step 4: Play a Video

Use the built-in "Open" button, or load programmatically:

```csharp
// Play a local file
VideoPlayer.Open(@"C:\Videos\movie.mp4");

// Or play from URL
VideoPlayer.OpenUri(new Uri("https://example.com/video.mp4"));
```

## Complete Example

Here's a minimal working example:

**MyApp.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.6" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.6" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.6" />
    <PackageReference Include="VlcVideoPlayer.Avalonia" Version="1.5.0" />
  </ItemGroup>
</Project>
```

**App.axaml:**
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="MyApp.App">
    <Application.Styles>
        <FluentTheme />
        <materialIcons:MaterialIconStyles />
    </Application.Styles>
</Application>
```

**Program.cs:**
```csharp
using Avalonia;
using Avalonia.VlcVideoPlayer;

namespace MyApp;

class Program
{
    public static void Main(string[] args)
    {
        VlcInitializer.Initialize();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
```

## Embedded Player (No Open Button)

For scenarios where you want to play a specific video without the file browser, use the `Source` property and hide the Open button:

```xml
<!-- XAML: Embedded player with custom background -->
<vlc:VideoPlayerControl 
    Source="C:\Videos\intro.mp4"
    AutoPlay="True"
    ShowOpenButton="False"
    ControlPanelBackground="#2d2d2d" />
```

Or set programmatically:

```csharp
// Hide the Open button and set source
VideoPlayer.ShowOpenButton = false;
VideoPlayer.AutoPlay = true;
VideoPlayer.Source = @"C:\Videos\movie.mp4";

// Customize the control panel background
VideoPlayer.ControlPanelBackground = new SolidColorBrush(Color.Parse("#1a1a1a"));
```

### Custom Control Panel Colors

The control panel background can be customized to match your app's theme:

```xml
<!-- Dark theme -->
<vlc:VideoPlayerControl ControlPanelBackground="#1a1a1a" />

<!-- Match your app's accent color -->
<vlc:VideoPlayerControl ControlPanelBackground="{DynamicResource SystemAccentColor}" />

<!-- Transparent (overlay style) -->
<vlc:VideoPlayerControl ControlPanelBackground="Transparent" />
```

## Platform Support

| Platform | Architecture | VLC Libraries |
|----------|--------------|---------------|
| **Windows** | x64 | ✅ Included via NuGet (VideoLAN.LibVLC.Windows) |
| **Windows** | ARM64 | ✅ Uses x64 via emulation |
| **macOS** | ARM64 (Apple Silicon) | ✅ Auto-downloads correct DMG or copies from VLC.app |
| **macOS** | x64 (Intel) | ✅ Auto-downloads correct DMG or copies from VLC.app |
| **Linux** | x64 | 📦 Uses system VLC (`sudo apt install vlc libvlc-dev`) |
| **Linux** | ARM64 | 📦 Uses system VLC |

### Architecture Detection

The library automatically detects your system's architecture and:
- **macOS**: Validates existing VLC.app architecture matches your Mac (ARM64 vs Intel)
- **macOS**: Downloads the correct VLC DMG (arm64 or intel64) if needed
- **Windows**: Uses x64 VLC (works on ARM64 via emulation)
- **Linux**: Detects x64 vs ARM64 library paths

### Adding macOS/Linux support to your project

For cross-platform applications, add the appropriate LibVLC packages:

```xml
<!-- In your .csproj -->
<ItemGroup Condition="$([MSBuild]::IsOSPlatform('OSX'))">
  <PackageReference Include="VideoLAN.LibVLC.Mac" Version="3.0.21" />
</ItemGroup>

<ItemGroup Condition="$([MSBuild]::IsOSPlatform('Linux'))">
  <PackageReference Include="VideoLAN.LibVLC.Linux" Version="3.0.21" />
</ItemGroup>
```

## API Reference

### VideoPlayerControl Properties

| Property | Type | Description |
|----------|------|-------------|
| `Volume` | `int` | Volume level (0-100) |
| `AutoPlay` | `bool` | Auto-play when media is loaded |
| `ShowControls` | `bool` | Show/hide playback controls |
| `ShowOpenButton` | `bool` | Show/hide the Open button (default: true) |
| `Source` | `string` | Video source path - set to auto-load video |
| `ControlPanelBackground` | `IBrush` | Background color of the control panel (default: White) |
| `IsPlaying` | `bool` | Whether media is currently playing |
| `Position` | `double` | Current playback position (0.0-1.0) |
| `Duration` | `TimeSpan` | Total media duration |

### VideoPlayerControl Methods

| Method | Description |
|--------|-------------|
| `Open(string path)` | Open a local file |
| `OpenUri(Uri uri)` | Open from URL |
| `Play()` | Start/resume playback |
| `Pause()` | Pause playback |
| `Stop()` | Stop playback |
| `Seek(double position)` | Seek to position (0.0-1.0) |
| `ToggleMute()` | Toggle audio mute |

### VideoPlayerControl Events

| Event | Description |
|-------|-------------|
| `PlaybackStarted` | Fired when playback starts |
| `PlaybackPaused` | Fired when playback is paused |
| `PlaybackStopped` | Fired when playback stops |
| `MediaEnded` | Fired when media reaches the end |

## License

MIT License - see [LICENSE](LICENSE) for details.

## Credits

- [LibVLCSharp](https://github.com/videolan/libvlcsharp) - VLC bindings for .NET
- [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons.Avalonia) - Material Design icons
