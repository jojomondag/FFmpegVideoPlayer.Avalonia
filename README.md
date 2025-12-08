[![NuGet](https://img.shields.io/nuget/v/FFmpegVideoPlayer.Avalonia.svg)](https://www.nuget.org/packages/FFmpegVideoPlayer.Avalonia/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

# FFmpegVideoPlayer.Avalonia

Self-contained FFmpeg video player for Avalonia UI. FFmpeg libraries bundled - no installation required.

![Preview](https://raw.githubusercontent.com/jojomondag/FFmpegVideoPlayer.Avalonia/main/images/Preview1.png)

## Installation

```bash
dotnet add package FFmpegVideoPlayer.Avalonia
```

## Quick Start

**Program.cs:**
```csharp
using Avalonia.FFmpegVideoPlayer;

public static void Main(string[] args)
{
    FFmpegInitializer.Initialize();
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
```

**App.axaml:**
```xml
<Application.Styles>
    <FluentTheme />
    <materialIcons:MaterialIconStyles />
</Application.Styles>
```

**MainWindow.axaml:**
```xml
<ffmpeg:VideoPlayerControl />
```

## Try the Example

```bash
git clone https://github.com/jojomondag/FFmpegVideoPlayer.Avalonia.git
cd FFmpegVideoPlayer.Avalonia/examples/FFmpegVideoPlayerExample
dotnet run
```

## FFmpegMediaPlayer API

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Source` | `string?` | `null` | Video file path or URL |
| `AutoPlay` | `bool` | `false` | Auto-play when media is loaded |
| `Volume` | `int` | `100` | Volume level (0-100) |
| `ShowBuiltInControls` | `bool` | `true` | Show/hide control bar |
| `ShowOpenButton` | `bool` | `true` | Show/hide file picker button |
| `ControlPanelBackground` | `IBrush?` | `White` | Control bar background brush |
| `VideoBackground` | `IBrush?` | `Black` | Video area background (set to `Transparent` for overlays) |
| `VideoStretch` | `Stretch` | `Uniform` | Video stretch mode (`None`, `Fill`, `Uniform`, `UniformToFill`) |
| `EnableKeyboardShortcuts` | `bool` | `true` | Enable keyboard controls (Space, Arrow keys, M) |
| `CurrentMediaPath` | `string?` | `null` | Full path of currently loaded media (read-only) |
| `HasMediaLoaded` | `bool` | `false` | Whether media is currently loaded (read-only) |
| `IsPlaying` | `bool` | `false` | Whether playback is active (read-only) |
| `Position` | `long` | `0` | Current playback position in milliseconds (read-only) |
| `Duration` | `long` | `0` | Total media duration in milliseconds (read-only) |

### Methods

| Method | Parameters | Description |
|--------|------------|-------------|
| `Open(string path)` | `path`: File path or URL | Opens and loads a media file |
| `OpenUri(Uri uri)` | `uri`: Media URI | Opens media from a URI |
| `Play()` | - | Starts or resumes playback |
| `Pause()` | - | Pauses playback |
| `Stop()` | - | Stops playback and resets position |
| `TogglePlayPause()` | - | Toggles between play and pause |
| `Seek(float positionPercent)` | `positionPercent`: 0.0 to 1.0 | Seeks to specific position |
| `ToggleMute()` | - | Toggles mute state |

### Events

| Event | EventArgs | Description |
|-------|-----------|-------------|
| `PlaybackStarted` | `EventArgs` | Raised when playback starts |
| `PlaybackPaused` | `EventArgs` | Raised when playback is paused |
| `PlaybackStopped` | `EventArgs` | Raised when playback is stopped |
| `MediaOpened` | `MediaOpenedEventArgs` | Raised when media is successfully opened |
| `MediaEnded` | `EventArgs` | Raised when media reaches the end |

## Platform Support

| Platform | Status |
|----------|--------|
| Windows x64 | ✅ Bundled |
| macOS ARM64 | ✅ Bundled |
| macOS x64 / Linux | Add libs to `runtimes/<rid>/native/` |

## License

MIT
