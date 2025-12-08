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

## VideoPlayerControl API

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Source` | `string?` | `null` | Video file path or URL |
| `AutoPlay` | `bool` | `false` | Auto-play when media is loaded |
| `Volume` | `int` | `100` | Volume level (0-100) |
| `ShowControls` | `bool` | `true` | Show/hide control bar |
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

### Keyboard Shortcuts

When `EnableKeyboardShortcuts` is `true`:

| Key | Action |
|-----|--------|
| `Space` | Toggle play/pause |
| `Left Arrow` | Seek backward 5 seconds (30s with Ctrl) |
| `Right Arrow` | Seek forward 5 seconds (30s with Ctrl) |
| `Up Arrow` | Increase volume by 5 |
| `Down Arrow` | Decrease volume by 5 |
| `M` | Toggle mute |

## FFmpegMediaPlayer API

For advanced scenarios requiring custom UI or direct frame access:

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsPlaying` | `bool` | Whether media is currently playing (read-only) |
| `Position` | `float` | Current position as percentage (0.0 to 1.0, read-only) |
| `Length` | `long` | Total duration in milliseconds (read-only) |
| `Volume` | `int` | Volume level (0-100) |
| `VideoWidth` | `int` | Video frame width (read-only) |
| `VideoHeight` | `int` | Video frame height (read-only) |

### Methods

| Method | Parameters | Returns | Description |
|--------|------------|---------|-------------|
| `Open(string path)` | `path`: File path or URL | `bool` | Opens media file, returns true if successful |
| `Play()` | - | - | Starts or resumes playback |
| `Pause()` | - | - | Pauses playback |
| `Stop()` | - | - | Stops playback and resets to beginning |
| `Seek(float positionPercent)` | `positionPercent`: 0.0 to 1.0 | - | Seeks to specific position |
| `Close()` | - | - | Closes current media and releases resources |
| `Dispose()` | - | - | Disposes the player and all resources |

### Events

| Event | EventArgs | Description |
|-------|-----------|-------------|
| `Playing` | `EventArgs` | Raised when playback starts |
| `Paused` | `EventArgs` | Raised when playback is paused |
| `Stopped` | `EventArgs` | Raised when playback is stopped |
| `EndReached` | `EventArgs` | Raised when media reaches the end |
| `PositionChanged` | `PositionChangedEventArgs` | Raised during playback with position updates |
| `LengthChanged` | `LengthChangedEventArgs` | Raised when media duration becomes known |
| `FrameReady` | `FrameEventArgs` | Raised when a new video frame is available |

### FrameEventArgs Properties

| Property | Type | Description |
|----------|------|-------------|
| `Data` | `byte[]` | BGRA pixel data |
| `Width` | `int` | Frame width in pixels |
| `Height` | `int` | Frame height in pixels |
| `Stride` | `int` | Bytes per row |
| `DataLength` | `int` | Total data length in bytes |

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
