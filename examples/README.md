# FFmpegVideoPlayer.Avalonia Examples

This folder contains example projects demonstrating how to use the FFmpegVideoPlayer.Avalonia library.

## FFmpegVideoPlayerExample

A simple example showing the basic usage of the `VideoPlayerControl`.

### Running the Example

```bash
cd examples/FFmpegVideoPlayerExample
dotnet run
```

### What it demonstrates

- Basic setup with `FFmpegInitializer.Initialize()`
- Using `VideoPlayerControl` in XAML
- Required Material.Icons styles for the player UI

### Project Structure

- `Program.cs` - Entry point with FFmpeg initialization
- `App.axaml` - Application setup with required styles
- `MainWindow.axaml` - Main window with VideoPlayerControl

## Using the NuGet Package Instead

If you want to test with the published NuGet package instead of the local project reference, modify the `.csproj`:

```xml
<!-- Remove this -->
<ProjectReference Include="../../Avalonia.FFmpegVideoPlayer.csproj" />

<!-- Add this -->
<PackageReference Include="FFmpegVideoPlayer.Avalonia" Version="2.1.2" />
```
