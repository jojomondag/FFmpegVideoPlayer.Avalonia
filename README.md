# VlcVideoPlayer.Avalonia

A self-contained VLC-based video player control for Avalonia UI with **automatic VLC library download**.

[![NuGet](https://img.shields.io/nuget/v/VlcVideoPlayer.Avalonia.svg)](https://www.nuget.org/packages/VlcVideoPlayer.Avalonia/)

## Features

- 🎬 Full-featured video player control for Avalonia
- 📥 **Automatic VLC library download** - no manual installation required!
- 🎨 Built-in playback controls with Material Icons
- 🖥️ Cross-platform (Windows, macOS, Linux)
- ⚡ Based on LibVLCSharp for maximum codec support

## Installation

```bash
dotnet add package VlcVideoPlayer.Avalonia
```

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

// Call before creating any windows - will auto-download VLC if needed
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

## Auto-Download Feature

The `VlcInitializer` automatically handles VLC library setup:

- **Windows**: Downloads VLC ZIP and extracts to your app's output folder
- **macOS**: Copies libraries from VLC.app if installed, or prompts to install
- **Linux**: Prompts to install VLC via package manager

### Track download progress

```csharp
VlcInitializer.DownloadProgressChanged += (progress) => 
{
    Console.WriteLine($"Download: {progress}%");
};

VlcInitializer.StatusChanged += (status) =>
{
    Console.WriteLine(status);
};

await VlcInitializer.InitializeAsync();
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
