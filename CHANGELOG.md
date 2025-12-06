# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.1.1] - 2025-12-03

### Fixed
- Fixed MSBuild targets file syntax error that caused build failures in consuming projects

## [2.1.0] - 2025-12-02

### Changed
- **Self-contained NuGet package** - FFmpeg native libraries are now bundled with the package
- Windows (x64) and macOS (arm64) FFmpeg binaries included - no external installation required
- Package now includes MSBuild targets to automatically copy native libraries to output directory

### Added
- Preview image for NuGet gallery and GitHub README

## [2.0.0] - 2025-12-02

### Changed
- **BREAKING: Switched video backend to FFmpeg** - Complete rewrite using FFmpeg.AutoGen
- Package renamed from the old `VideoPlayer.Avalonia` name to `FFmpegVideoPlayer.Avalonia`
- Replaced the legacy initializer with `FFmpegInitializer`
- Video rendering now uses Avalonia's WriteableBitmap instead of the previous video view implementation

### Added
- **Full macOS ARM64 (Apple Silicon) support** - FFmpeg has native ARM64 binaries via Homebrew
- **Automatic FFmpeg installation on macOS** - Installs FFmpeg via Homebrew if not found! 🎉
- **Automatic Homebrew installation on macOS** - Installs Homebrew if needed for FFmpeg
- `FFmpegMediaPlayer` - New FFmpeg-based media player backend
- `AudioPlayer` - Cross-platform audio playback using OpenTK/OpenAL
- `FFmpegNotFoundException` exception with platform-specific installation instructions
- `FFmpegInstallationStatus` class to check FFmpeg installation
- `InitializeAsync()` method for async initialization with auto-install
- `StatusChanged` event for progress updates during initialization/installation
- `IsHomebrewInstalled()` method to check Homebrew status on macOS
- `TryInstallFFmpegOnMacOS()` method for manual Homebrew installation trigger
- `TryInstallFFmpegOnWindows()` method for manual winget installation trigger
- Multi-threaded video decoding for better performance
- Support for all FFmpeg-supported codecs and formats

### Removed
- All legacy native video engine dependencies that were used prior to the FFmpeg migration

### Platform Requirements
- **macOS**: **Zero configuration!** FFmpeg auto-installs via Homebrew ✅
- **Windows**: Install FFmpeg via `winget install ffmpeg` or `choco install ffmpeg`
- **Linux**: Install FFmpeg via package manager (`apt install ffmpeg`, `dnf install ffmpeg`, etc.)

## [1.6.0] - 2025-12-01

### Changed
- Internal implementation details for the legacy 1.x engine (no longer relevant for FFmpeg-based 2.x releases)

### Added
- Additional platform and architecture handling for the legacy engine (superseded by FFmpeg-based implementation)

### Removed
- Old automatic native library download logic and related platform-specific installers

## [1.5.0] - 2025-12-01

### Added
- Architecture detection and platform-specific handling for the legacy backend

### Fixed
- Various stability and platform compatibility issues in the pre-FFmpeg implementation

### Changed
- Improved logging and diagnostics for the legacy backend

## [1.4.0] - 2025-11-27

### Added
- `Source` property - Set video path directly in XAML or code, auto-loads when set
- `ShowOpenButton` property - Hide the Open button for embedded player scenarios
- `ControlPanelBackground` property - Customize the control panel background color

### Changed
- Improved property change handling for dynamic updates

## [1.3.0] - 2025-11-27

### Changed
- Redesigned UI with clean white control panel background
- Improved button styling with custom template (no overlay issues)
- Dark text and icons for better contrast and readability
- Light gray buttons with visible borders

### Fixed
- Fixed dark overlay appearing over buttons in some themes
- Fixed button visibility issues

## [1.2.0] - 2025-11-27

### Changed
- Optimized NuGet package size from 90MB to 0.02MB
- Improved icon visibility for dark themes

### Fixed
- Fixed seek bar dragging issues

## [1.0.0] - 2025-11-27

### Added
- Initial release
- `VideoPlayerControl` - Full-featured video player control
- Play, pause, stop, seek functionality
- Volume control with mute toggle
- Material Design icons
- Events: `PlaybackStarted`, `PlaybackPaused`, `PlaybackStopped`, `MediaEnded`
- Properties: `Volume`, `AutoPlay`, `ShowControls`, `IsPlaying`, `Position`, `Duration`
