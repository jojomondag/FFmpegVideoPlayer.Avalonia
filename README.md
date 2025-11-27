# VlcVideoPlayer.Avalonia

A self-contained VLC-based video player control for Avalonia UI with **embedded VLC libraries**.

[![NuGet](https://img.shields.io/nuget/v/VlcVideoPlayer.Avalonia.svg)](https://www.nuget.org/packages/VlcVideoPlayer.Avalonia/)

## Features

- 🎬 Full-featured video player control for Avalonia
- 📦 **VLC libraries included** - no manual installation required!
- 🎨 Built-in playback controls with Material Icons
- 🖥️ Cross-platform (Windows, macOS, Linux)
- ⚡ Based on LibVLCSharp for maximum codec support

## Installation

```bash
dotnet add package VlcVideoPlayer.Avalonia
```

The package includes the official VideoLAN LibVLC libraries for Windows. For other platforms, see [Platform Support](#platform-support).

## Quick Start

### 1. Add Material Icons to App.axaml

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             ...>
    <Application.Styles>
        <FluentTheme />
        <materialIcons:MaterialIconStyles />
    </Application.Styles>
</Application>
```

### 2. Initialize VLC (Program.cs or App startup)

```csharp
using Avalonia.VlcVideoPlayer;

// Call before creating any windows
VlcInitializer.Initialize();
```

### 3. Add the VideoPlayerControl to your XAML

```xml
<Window xmlns:vlc="clr-namespace:Avalonia.VlcVideoPlayer;assembly=Avalonia.VlcVideoPlayer">
    <vlc:VideoPlayerControl x:Name="VideoPlayer" ShowControls="True" />
</Window>
```

### 4. Play a video

```csharp
// Play a local file
VideoPlayer.Open("/path/to/video.mp4");

// Or play from URL
VideoPlayer.OpenUri(new Uri("https://example.com/video.mp4"));
```

## Platform Support

| Platform | VLC Libraries |
|----------|---------------|
| **Windows x64** | ✅ Included via NuGet (VideoLAN.LibVLC.Windows) |
| **macOS** | 📥 Auto-copies from VLC.app if installed, or prompts to install |
| **Linux** | 📦 Uses system VLC (`sudo apt install vlc libvlc-dev`) |

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
