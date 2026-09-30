# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [3.0.3] - 2026-09-30

### Added
- Added `YouTubeMediaSourceResolver.PreloadAsync` with coalesced, short-lived resolution caching for known remote videos.
- Added `VideoPlayerControl.FirstFramePresented`, raised once after the active source has rendered its first frame.

### Fixed
- Embedded album art is excluded when selecting the video stream, preserving real-time playback for MP3 and other audio-only files (thanks @coolguru81, PR #6).
- YouTube playback now lets FFmpeg range-read the signed media URLs directly instead of reopening a loopback proxy stream for every request, reducing startup and seek latency.
- Video stream selection now prefers native resolution and bitrate, while CPU/OpenGL renderers use high-quality bitmap interpolation and bicubic chroma conversion to reduce pixelation when scaling.
- Updated the bundled Windows x64 FFmpeg libraries to the FFmpeg 8.1.2 release branch and filtered the split, non-fatal `Late SEI is not implemented` decoder diagnostic.
- Autoplay no longer decodes and seeks back over the first frame before playback, avoiding a duplicate network read for YouTube sources.
- Video presentation now follows the audio backend clock when available. Audio-master waits use bounded retry/drop thresholds after stalls, while video-only playback follows a pause-aware wall clock until a frame is due.
- Seeks, stops, and source replacements now invalidate decoded and queued callbacks from the previous timeline without corrupting pending-frame accounting.
- YouTube resolution caching retains all in-flight requests for coalescing, bounds completed entries, prunes expired results, and applies a cache-owned timeout.

### Changed
- Removed unused private state, audio conversion code, parameters, dependencies and checked-in build artifacts; generated artifacts are now ignored.
- Corrected legacy OpenGL renderer descriptions to reflect their CPU bitmap implementation while preserving public APIs.

## [2.9.0] - 2026-05-29

### Added
- Audio-only media files can now be opened directly in `VideoPlayerControl`. The control shows an audio placeholder, keeps the timeline usable without a video frame, and drives audio-only progress from a real-time playback clock instead of decoder PTS.

### Fixed
- Fixed OpenAL queue handling in the default OpenTK audio backend. Pending audio chunks are no longer dropped when FFmpeg decodes faster than OpenAL has free buffers, which prevented audio-only files from ending early or sounding too fast.
- Made the OpenAL context current on the audio worker thread before issuing OpenAL calls.

## [2.8.1] - 2026-05-20

### Fixed
- **Avalonia 12.x runtime crash on control initialization** (issue #4). Rebuilt package XAML against Avalonia 12.0.1 to avoid `MissingMethodException` for `ReflectionBindingExtension.ProvideValue` when `VideoPlayerControl` is instantiated in Avalonia 12.x / .NET 10 apps.

## [2.8.0] - 2026-04-24

### Added
- **Linux auto-install** (issue #1). `FFmpegInitializer.Initialize()` now detects `apt-get`, `dnf`, or `pacman` under `/usr/bin` and attempts to install FFmpeg via the system package manager when `autoInstall: true` (the default). Uses `sudo -n` when not already running as root, so it succeeds silently on passwordless-sudo systems (CI, dev machines with NOPASSWD) and fails cleanly elsewhere with the exact command to run manually surfaced via `StatusChanged` and the thrown `FFmpegNotFoundException`. Matches the existing Homebrew flow on macOS so both macOS architectures and the three major Linux distro families "just work" on a fresh machine.

## [2.7.1] - 2026-04-24

### Fixed
- **Slider seeks now land on the exact requested position instead of snapping to keyframes** (issue #3). `av_seek_frame` with `AVSEEK_FLAG_BACKWARD` lands on the nearest keyframe at or before the target, which with typical 1–10 s GOPs meant the slider appeared to "stick" at keyframe boundaries. `Seek()` now records the actual requested time and `ProcessVideoPacket` / `ProcessAudioPacket` / `DecodeSingleFrame` decode-drain through frames between that keyframe and the target without displaying or queueing them. Playback resumes precisely where the user let go of the slider.

## [2.7.0] - 2026-04-24

### Fixed
- **macOS: "System.NotSupportedException: Specified method is not supported." when opening a file** (issue #2). The bundled `runtimes/osx-arm64/native/*.dylib` files had absolute `LC_LOAD_DYLIB` paths pointing at Homebrew-only locations (`/opt/homebrew/opt/<formula>/lib/...`), so `dlopen` silently failed on any Mac without the exact same formulae installed. FFmpeg.AutoGen then left every function as a `NotSupportedException`-throwing stub, and the failure only surfaced later inside `avformat_open_input`.
- Bundled FFmpeg is now validated after configuration by calling `avcodec_version()`. If the probe returns 0 or throws, the candidate is treated as broken and the initializer falls through to system discovery / Homebrew autoinstall instead of silently continuing with unusable bindings.
- Initialization now throws `FFmpegNotFoundException` up front when no candidate actually loaded, instead of reporting "initialized successfully" and crashing at the first FFmpeg call.

### Removed
- Stopped shipping `runtimes/osx-arm64/native/*.dylib` in the NuGet package. macOS users should `brew install ffmpeg` (the initializer can auto-install with `autoInstall: true`, which is the default), or supply their own binaries via `customPath`.

## [2.1.8] - 2025-12-26

### Fixed
- Fixed audio/video synchronization issues - audio and video now properly sync using presentation timestamps (PTS)
- Fixed video playback speed issues - video now plays at correct speed using frame timestamps
- Fixed audio playback stopping prematurely - audio sync threshold relaxed to prevent excessive frame dropping
- Improved pause/resume timing accuracy

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
