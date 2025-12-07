# FFmpegVideoPlayer.Avalonia

Self-contained FFmpeg video player for Avalonia UI. FFmpeg libraries bundled - no installation required.

[![NuGet](https://img.shields.io/nuget/v/FFmpegVideoPlayer.Avalonia.svg)](https://www.nuget.org/packages/FFmpegVideoPlayer.Avalonia/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

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

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Source` | `string` | `null` | Video file path |
| `AutoPlay` | `bool` | `False` | Auto-play on load |
| `Volume` | `int` | `100` | Volume (0-100) |
| `ShowControls` | `bool` | `True` | Show control bar |
| `ShowOpenButton` | `bool` | `True` | Show file picker button |
| `ControlPanelBackground` | `IBrush` | `White` | Control bar background |
| `VideoBackground` | `IBrush` | `Black` | Video area background (set to `Transparent` for overlays) |

## Methods

| Method | Description |
|--------|-------------|
| `Open(path)` | Open video file |
| `Play()` | Start playback |
| `Pause()` | Pause playback |
| `Stop()` | Stop and reset |
| `Seek(float)` | Seek (0.0-1.0) |
| `ToggleMute()` | Toggle mute |

## Events

`PlaybackStarted` · `PlaybackPaused` · `PlaybackStopped` · `MediaEnded`

## Control-less Usage (Custom UI)

For advanced scenarios like live wallpapers or custom video UIs, you can use `FFmpegMediaPlayer` directly and build your own controls:

```csharp
using Avalonia.FFmpegVideoPlayer;

// Initialize FFmpeg once at startup
FFmpegInitializer.Initialize();

// Create player instance
var player = new FFmpegMediaPlayer();

// Subscribe to events
player.FrameReady += (s, e) =>
{
    // e.Data contains BGRA pixel data
    // e.Width, e.Height, e.Stride for frame dimensions
    // Render to your own Image control or OpenGL surface
};

player.PositionChanged += (s, e) => 
{
    // e.Position is 0.0 to 1.0
    // Update your custom seek bar
};

player.Playing += (s, e) => { /* Update UI */ };
player.Paused += (s, e) => { /* Update UI */ };
player.EndReached += (s, e) => { /* Handle loop or next video */ };

// Control playback
player.Open("video.mp4");
player.Play();
player.Pause();
player.Seek(0.5f); // Seek to 50%
player.Volume = 75; // 0-100

// Properties
bool isPlaying = player.IsPlaying;
long durationMs = player.Length;
float position = player.Position; // 0.0 to 1.0

// Cleanup
player.Dispose();
```

### Minimal Control-less Example

```xml
<!-- Just the video, no controls -->
<ffmpeg:VideoPlayerControl ShowControls="False" 
                           ShowOpenButton="False"
                           VideoBackground="Transparent"
                           Source="background.mp4"
                           AutoPlay="True" />
```

## Platform Support

| Platform | Status |
|----------|--------|
| Windows x64 | ✅ Bundled |
| macOS ARM64 | ✅ Bundled |
| macOS x64 / Linux | Add libs to `runtimes/<rid>/native/` |

## License

MIT
