# FFmpegVideoPlayer.Avalonia Examples

The example demonstrates `VideoPlayerControl` with Avalonia 12.1+ and .NET 8+.
Run it from the repository root:

```bash
dotnet run --project examples/FFmpegVideoPlayerExample
```

## What it demonstrates

- FFmpeg initialization, including custom native-library paths in `Program.cs`.
- Playback with audio enabled by default; `AudioPlayerFactory` allows a custom backend.
- Controls, transparency, stretch modes and a keyboard-shortcut window.
- Built-in icons without extra packages. For Material icons, set `IconProvider`
  to `Avalonia.FFmpegVideoPlayer.MaterialVideoIconProvider.Instance`.
- Console and rotating file logs under the example's output `logs/` directory.

The package bundles FFmpeg for Windows x64 and OpenAL Soft for Windows x64, x86
and ARM64. Other platforms need compatible system libraries; see the
[main README](../README.md) for native requirements and customization.

## Using the NuGet package

Replace both `ProjectReference` entries in the example `.csproj` with this single
reference. Core, the control and the default audio backend are included:

```xml
<PackageReference Include="FFmpegVideoPlayer.Avalonia" Version="3.1.0-preview.2" />
```

Until the preview is published, build it locally with
`dotnet pack Avalonia.FFmpegVideoPlayer.csproj -c Release -o artifacts/packages`.
Then restore the example from that directory alongside NuGet.org:

```bash
dotnet restore examples/FFmpegVideoPlayerExample --source artifacts/packages --source https://api.nuget.org/v3/index.json
```
