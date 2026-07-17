[![NuGet](https://img.shields.io/nuget/v/FFmpegVideoPlayer.Avalonia.svg)](https://www.nuget.org/packages/FFmpegVideoPlayer.Avalonia/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

# FFmpegVideoPlayer.Avalonia

Standalone FFmpeg media player for Avalonia 12. It owns source resolution, streaming,
playback lifecycle, cancellation and cleanup for local video/audio, HTTP media,
seekable streams, DASH manifests and YouTube URLs.

![Preview](https://raw.githubusercontent.com/jojomondag/FFmpegVideoPlayer.Avalonia/main/images/Preview1.png)

## Install

```bash
dotnet add package FFmpegVideoPlayer.Avalonia --version 3.0.0
```

Version 3 requires **Avalonia 12.1.0+ (below 13)** and **.NET 8+**. The single
package contains the Avalonia control, Core and OpenTK/OpenAL audio implementation;
applications do not reference separate player packages.

The managed player API is cross-platform, but the native payload is platform-specific:
FFmpeg 8.0.1 DLLs are bundled **only for Windows x64**. Windows x86/ARM64, macOS,
and Linux require a compatible system FFmpeg installation or an explicitly supplied
native-library path. OpenAL Soft audio DLLs are bundled separately for Windows x64,
x86, and ARM64.

## Quick start

**1. Initialize FFmpeg** (e.g. in `Program.cs` before the app starts):

```csharp
using FFmpegVideoPlayer.Core;

FFmpegInitializer.Initialize();
```

**2. Add the control in XAML:**

```xml
xmlns:ffmpeg="clr-namespace:Avalonia.FFmpegVideoPlayer;assembly=Avalonia.FFmpegVideoPlayer"

<ffmpeg:VideoPlayerControl Source="C:\path\to\video.mp4" ShowControls="True" />
```

Video and audio-only files are both supported, for example `.mp4`, `.mov`, `.mp3`, `.wav`, `.flac`, `.ogg`, and `.m4a`.

`Source` also accepts direct HTTP(S) and YouTube URLs. Source changes are resolved
asynchronously and stale opens are cancelled automatically:

```xml
<ffmpeg:VideoPlayerControl
    Source="https://youtu.be/Ppejf4-YmSM"
    AutoPlay="True"
    ShowOpenButton="False" />
```

For custom headers, in-memory media or manifests, use the typed API:

```csharp
var source = MediaSource.FromUri(
    new Uri("https://media.example/video.mp4"),
    headers: new Dictionary<string, string> { ["Authorization"] = "Bearer …" });

var result = await player.OpenAsync(
    source,
    new MediaOpenOptions
    {
        OpenTimeout = TimeSpan.FromSeconds(20),
        ReadTimeout = TimeSpan.FromSeconds(30)
    },
    cancellationToken);
```

OpenAL Soft binaries for Windows x64, x86 and ARM64 are included in the package.

## FFmpeg

| Platform | Default |
|----------|---------|
| Windows x64 | Bundled DLLs in the package |
| Windows x86 / ARM64 | Compatible system FFmpeg 8 native libraries or a custom path |
| macOS (x64 / ARM64) | System/Homebrew (`brew install ffmpeg`); can auto-install |
| Linux (x64 / ARM64) | System packages (apt/dnf/pacman); can auto-install with passwordless sudo |

On every row except Windows x64, FFmpeg is a system/native prerequisite; it is not
contained in the NuGet package. Automatic installation, where supported, installs that
system prerequisite. The required FFmpeg 8 library ABI is `avcodec-62`, `avformat-62`,
`avutil-60`, `swresample-6`, and `swscale-9`.

Custom FFmpeg path:

```csharp
FFmpegInitializer.Initialize(customPath: @"C:\ffmpeg\bin", useBundledBinaries: false);
```

Subscribe to `StatusChanged` for progress during discovery or auto-install.

## Example

```bash
git clone https://github.com/jojomondag/FFmpegVideoPlayer.Avalonia.git
cd FFmpegVideoPlayer.Avalonia/examples/FFmpegVideoPlayerExample
dotnet run
```

## VideoPlayerControl

**Sources:** `Source` for a path/URL, or typed `Media` for reusable
`MediaSource` recipes.

**Lifecycle:** `PlaybackState`, `LastError`, `MediaOpening`, `MediaOpened`,
`MediaFailed`, `PlaybackStateChanged`, and `MediaEnded`.

**Methods:** `OpenAsync`, `Open`, `OpenUri`, `CloseAsync`, `Play`, `Pause`,
`Stop`, `TogglePlayPause`, `Seek`, and `ToggleMute`.

**Presentation:** `AutoPlay`, `Volume`, `ShowControls`, `ShowOpenButton`,
`VideoStretch`, `VideoBackground`, `EnableKeyboardShortcuts`, `RenderingMode`
(`Cpu` / `OpenGL`), `AudioPlayerFactory`, and `IconProvider`.

**Shortcuts:** Space (play/pause), arrow keys (seek/volume), M (mute)

Set `AudioPlayerFactory` to customize or disable audio. OpenAL via OpenTK is the default.

Custom icons: implement `IIconProvider` and set `IconProvider`.

## License

The managed project code is licensed under MIT. The NuGet package also redistributes
FFmpeg and OpenAL Soft native libraries under their respective GNU LGPL terms. See
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) for provenance, checksums, source
availability, replacement rights, and the packaged license-text locations.
