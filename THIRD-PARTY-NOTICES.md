# Third-Party Notices

`FFmpegVideoPlayer.Avalonia` is MIT-licensed, but its NuGet package also
redistributes the native libraries identified below. Those libraries remain under
their own copyright and license terms; the project's MIT license does not replace
or restrict those terms.

## FFmpeg 8.0.1 release-branch build

Copyright (c) 2000-2025 the FFmpeg developers and other contributors.

The Windows x64 package payload contains five dynamically loaded FFmpeg libraries.
Each DLL reports version `n8.0.1-23-gf9a3e1b776-20251204`, i.e. FFmpeg's 8.0
release branch at commit
[`f9a3e1b7763669c5a29287b1dadc2f6288677e97`](https://github.com/FFmpeg/FFmpeg/commit/f9a3e1b7763669c5a29287b1dadc2f6288677e97).
Each DLL also reports `LGPL version 3 or later`. Its embedded configure command
contains `--enable-version3 --enable-shared --disable-static`, disables GPL-only
and nonfree components, and identifies the `/ffbuild` Windows x64 build environment.
Accordingly, these particular binaries are distributed under
**GNU LGPL version 3 or later**, not LGPL 2.1.
FFmpeg's official source and licensing pages are
<https://ffmpeg.org/download.html> and <https://ffmpeg.org/legal.html>; its
canonical Git repository is <https://git.ffmpeg.org/ffmpeg.git>.

The configuration and version format are consistent with BtbN's
`win64-lgpl-shared` build family, but that is a reproducibility reference rather
than verified archive provenance. A build-recipe snapshot immediately preceding
the DLL build date is
[`BtbN/FFmpeg-Builds@766a6a2c088ff771a5d383428def6f5791490e56`](https://github.com/BtbN/FFmpeg-Builds/tree/766a6a2c088ff771a5d383428def6f5791490e56).
That recipe records enabled third-party components, source URLs, checksums, and
build commands, but the repository cannot verify that it was the recipe actually
used for these DLLs. The original downloaded archive name, URL, and archive checksum
were not retained when the DLLs were imported, so this notice does not invent them:
the embedded version/configuration and the per-file SHA-256 values below are the
authoritative identifiers for the files actually redistributed by this package.

| Packaged file | Bytes | SHA-256 |
| --- | ---: | --- |
| `runtimes/win-x64/native/avcodec-62.dll` | 78,149,120 | `3EECF00886B7A22C4C2AE4999D9132C989C3170187D254F59FD2006FA79FBF35` |
| `runtimes/win-x64/native/avformat-62.dll` | 21,750,272 | `B689A6101A1AB8286C5D9F2DBF10ABC574EA23CCB98F2B45AFBCC885EB1DF1E2` |
| `runtimes/win-x64/native/avutil-60.dll` | 2,939,904 | `8B6729BAD95392537E0BBA73BCE61587212EFE179E0381DD2AF9574F8717CC23` |
| `runtimes/win-x64/native/swresample-6.dll` | 723,456 | `BAC2C3EC4D27B1112B8F4DFBA924DC272FB611F424FCC80AD7EA0D88368D1C7A` |
| `runtimes/win-x64/native/swscale-9.dll` | 1,910,272 | `D0324617295B3F055FD4C60002A60642D1625CCE91B87C4F6183F63FA7D1FC00` |

License texts are packaged as [`LICENSES/LGPL-3.0-or-later.txt`](LICENSES/LGPL-3.0-or-later.txt)
and, because LGPLv3 incorporates GPLv3, [`LICENSES/GPL-3.0.txt`](LICENSES/GPL-3.0.txt).
FFmpeg's copyright, credits, and file-level licensing details remain in its
corresponding source tree.

## OpenAL Soft 1.23.1

Copyright (c) the OpenAL Soft contributors. OpenAL Soft is distributed under the
**GNU Library General Public License version 2 or, at your option, any later
version** (`LGPL-2.0-or-later`). The complete version 2 text is packaged as
[`LICENSES/LGPL-2.0-or-later.txt`](LICENSES/LGPL-2.0-or-later.txt).

The DLLs are copied without modification at pack time from the signed NuGet package
[`OpenAL.Soft` 1.23.1](https://www.nuget.org/packages/OpenAL.Soft/1.23.1). The
upstream release source is tag `1.23.1`, commit
[`d3875f333fb6abe2f39d82caca329414871ae53b`](https://github.com/kcat/openal-soft/tree/d3875f333fb6abe2f39d82caca329414871ae53b).

Source NuGet package identifiers:

- SHA-256: `7FF3BF3FB4ACEC10347BE1C0F79F630296F42BCBE76E39F09210C891C5630029`
- NuGet SHA-512 (Base64): `0QLOJooC9vW5J+uZKk+NK8O5L2Sztk5P7PCKRc/amgLsg1ACDzhiFMPQcMasxBLDluF+IOOFjOc46RL8cuVUsg==`

| Packaged file | Bytes | SHA-256 |
| --- | ---: | --- |
| `runtimes/win-x64/native/OpenAL32.dll` | 1,085,440 | `51480056B5AC1618F75DEF04CB92935C3543C73F80A8E481051C8D5F588E14E7` |
| `runtimes/win-x86/native/OpenAL32.dll` | 981,504 | `7A9E095055BA9236F4E468D05EE9AA42122C1405E088B34AB8BCC62E613E5907` |
| `runtimes/win-arm64/native/OpenAL32.dll` | 1,062,912 | `AD4CB474034A6700D8FC1B522A43CE8ABF3A2618B1D324D6178994A626A8CE8E` |

## Corresponding source and written offer

Equivalent network access to the source and build material is available here:

- FFmpeg exact source:
  <https://github.com/FFmpeg/FFmpeg/archive/f9a3e1b7763669c5a29287b1dadc2f6288677e97.tar.gz>
- FFmpeg official source repository and license information:
  <https://git.ffmpeg.org/ffmpeg.git> and <https://ffmpeg.org/legal.html>
- FFmpeg Windows build scripts and dependency recipes:
  <https://github.com/BtbN/FFmpeg-Builds/archive/766a6a2c088ff771a5d383428def6f5791490e56.tar.gz>
- OpenAL Soft exact source:
  <https://github.com/kcat/openal-soft/archive/d3875f333fb6abe2f39d82caca329414871ae53b.tar.gz>

For at least three years after the last distribution of a package version containing
these native binaries, any recipient may request the complete corresponding source,
including the scripts needed to control compilation and installation, by opening an
issue at <https://github.com/jojomondag/FFmpegVideoPlayer.Avalonia/issues>. If the
network locations above are no longer available, the project will provide that
material on a medium customarily used for software interchange for no more than the
reasonable cost of physically providing it. Include the package version and the
binary SHA-256 from this notice in the request.

## Replacement and debugging

The package uses the native libraries through a shared-library mechanism. Recipients
may replace the DLLs in their application's runtime output with modified,
interface-compatible builds. The package license terms do not prohibit reverse
engineering performed for debugging modifications to the LGPL-covered libraries.
No warranty is provided for the third-party software; see the complete license texts
for the controlling terms.
