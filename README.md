# FFmpegVideoPlayer.Avalonia

A **self-contained** FFmpeg video player control for Avalonia UI. FFmpeg 8.x libraries are **bundled** - no external installation required!

[![NuGet](https://img.shields.io/nuget/v/FFmpegVideoPlayer.Avalonia.svg)](https://www.nuget.org/packages/FFmpegVideoPlayer.Avalonia/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## ✨ Features

- 🎬 Full-featured video player control
- 📦 **Self-contained** - FFmpeg libraries bundled in the NuGet package
- 🖥️ Cross-platform: Windows x64, macOS ARM64 (Apple Silicon)
- 🎨 Customizable appearance via XAML properties
- 🎛️ Built-in controls: Play/Pause, Stop, Seek, Volume, Mute
- ⚡ Hardware-accelerated decoding

## 📦 Installation

```bash
dotnet add package FFmpegVideoPlayer.Avalonia
```

That's it! The FFmpeg libraries are included in the package.

## 🚀 Quick Start

### 1. Add Material Icons to App.axaml

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="YourApp.App">
    <Application.Styles>
        <FluentTheme />
        <materialIcons:MaterialIconStyles />
    </Application.Styles>
</Application>
```

### 2. Initialize FFmpeg in Program.cs

```csharp
using Avalonia.FFmpegVideoPlayer;

public static void Main(string[] args)
{
    FFmpegInitializer.Initialize();
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
```

### 3. Add the Video Player to your Window

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:ffmpeg="clr-namespace:Avalonia.FFmpegVideoPlayer;assembly=Avalonia.FFmpegVideoPlayer"
        Title="Video Player">
    
    <ffmpeg:VideoPlayerControl />
    
</Window>
```

## 🎨 XAML Properties

All properties can be set directly in XAML:

```xml
<ffmpeg:VideoPlayerControl 
    Source="/path/to/video.mp4"
    AutoPlay="True"
    Volume="80"
    ShowControls="True"
    ShowOpenButton="False"
    ControlPanelBackground="#1a1a1a" />
```

### Available Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Source` | `string` | `null` | Path to video file - automatically loads when set |
| `AutoPlay` | `bool` | `False` | Auto-play when video is loaded |
| `Volume` | `int` | `100` | Volume level (0-100) |
| `ShowControls` | `bool` | `True` | Show/hide the control bar |
| `ShowOpenButton` | `bool` | `True` | Show/hide the "Open" file button |
| `ControlPanelBackground` | `IBrush` | `White` | Background color of the control bar |

### Styling Examples

**Dark themed player:**
```xml
<ffmpeg:VideoPlayerControl 
    ControlPanelBackground="#1a1a1a" />
```

**Embedded player (no file picker):**
```xml
<ffmpeg:VideoPlayerControl 
    Source="C:\Videos\intro.mp4"
    AutoPlay="True"
    ShowOpenButton="False" />
```

**Transparent controls (overlay style):**
```xml
<ffmpeg:VideoPlayerControl 
    ControlPanelBackground="Transparent" />
```

**Using theme resources:**
```xml
<ffmpeg:VideoPlayerControl 
    ControlPanelBackground="{DynamicResource SystemControlBackgroundAltHighBrush}" />
```

## 💻 Programmatic Control

```csharp
// Open and play a video
VideoPlayer.Open(@"C:\Videos\movie.mp4");
VideoPlayer.Play();

// Or use Source property (auto-loads)
VideoPlayer.Source = @"C:\Videos\movie.mp4";
VideoPlayer.AutoPlay = true;

// Playback control
VideoPlayer.Pause();
VideoPlayer.Stop();
VideoPlayer.Seek(0.5f);  // Seek to 50%

// Volume control
VideoPlayer.Volume = 50;
VideoPlayer.ToggleMute();

// Customize appearance
VideoPlayer.ShowOpenButton = false;
VideoPlayer.ControlPanelBackground = new SolidColorBrush(Color.Parse("#2d2d2d"));
```

## 📋 Complete Example

**YourApp.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.6" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.6" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.6" />
    <PackageReference Include="FFmpegVideoPlayer.Avalonia" Version="2.0.0" />
  </ItemGroup>
</Project>
```

**App.axaml:**
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             x:Class="YourApp.App">
    <Application.Styles>
        <FluentTheme />
        <materialIcons:MaterialIconStyles />
    </Application.Styles>
</Application>
```

**App.axaml.cs:**
```csharp
using Avalonia;
using Avalonia.Markup.Xaml;

namespace YourApp;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
```

**Program.cs:**
```csharp
using Avalonia;
using Avalonia.FFmpegVideoPlayer;

namespace YourApp;

class Program
{
    public static void Main(string[] args)
    {
        FFmpegInitializer.Initialize();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
```

**MainWindow.axaml:**
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ffmpeg="clr-namespace:Avalonia.FFmpegVideoPlayer;assembly=Avalonia.FFmpegVideoPlayer"
        x:Class="YourApp.MainWindow"
        Title="Video Player" Width="800" Height="600">
    
    <ffmpeg:VideoPlayerControl 
        x:Name="VideoPlayer"
        ControlPanelBackground="#2d2d2d" />
    
</Window>
```

## 📖 API Reference

### Methods

| Method | Description |
|--------|-------------|
| `Open(string path)` | Open a video file |
| `OpenUri(Uri uri)` | Open from URL or file URI |
| `Play()` | Start/resume playback |
| `Pause()` | Pause playback |
| `Stop()` | Stop playback and reset position |
| `Seek(float position)` | Seek to position (0.0 = start, 1.0 = end) |
| `ToggleMute()` | Toggle audio mute |
| `TogglePlayPause()` | Toggle between play and pause |

### Read-only Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsPlaying` | `bool` | Whether video is currently playing |
| `Position` | `long` | Current position in milliseconds |
| `Duration` | `long` | Total duration in milliseconds |

### Events

| Event | Description |
|-------|-------------|
| `PlaybackStarted` | Fired when playback begins |
| `PlaybackPaused` | Fired when playback is paused |
| `PlaybackStopped` | Fired when playback stops |
| `MediaEnded` | Fired when video reaches the end |

## 🌍 Platform Support

| Platform | Status |
|----------|--------|
| Windows x64 | ✅ Bundled |
| macOS ARM64 (M1/M2/M3/M4) | ✅ Bundled |
| macOS x64 | 🔧 Add libraries to `runtimes/osx-x64/native/` |
| Linux x64 | 🔧 Add libraries to `runtimes/linux-x64/native/` |

## 🔧 Troubleshooting

**Video doesn't play:**
- Ensure `FFmpegInitializer.Initialize()` is called before creating windows
- Check console output for FFmpeg initialization errors

**No audio:**
- OpenAL is required for audio. On Linux: `sudo apt install libopenal1`

**Player shows black screen:**
- Some video codecs may not be supported. Try a different video file.

## 📄 License

MIT License - see [LICENSE](LICENSE) for details.

## 🙏 Credits

- [FFmpeg](https://ffmpeg.org/) - Multimedia framework
- [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen) - .NET bindings
- [OpenTK](https://opentk.net/) - Audio playback
- [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons.Avalonia) - UI icons
