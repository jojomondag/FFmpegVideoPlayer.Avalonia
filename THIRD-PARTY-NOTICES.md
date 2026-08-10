# Third-Party Notices

`FFmpegVideoPlayer.Avalonia` is MIT-licensed, but its NuGet package also
redistributes the native libraries identified below. Those libraries remain under
their own copyright and license terms; the project's MIT license does not replace
or restrict those terms.

## FFmpeg 8.1.2 release-branch build

Copyright (c) 2000-2026 the FFmpeg developers and other contributors.

The Windows x64 package payload contains five dynamically loaded FFmpeg libraries.
Each DLL reports version `n8.1.2-34-g9b6c8969e0-20260810`, i.e. FFmpeg's 8.1
release branch at commit
[`9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b`](https://github.com/FFmpeg/FFmpeg/commit/9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b).
Each DLL also reports `LGPL version 3 or later`. Its embedded configure command
contains `--enable-version3 --enable-shared --disable-static`, disables GPL-only
and nonfree components, and identifies the `/ffbuild` Windows x64 build environment.
Accordingly, these particular binaries are distributed under
**GNU LGPL version 3 or later**, not LGPL 2.1.
FFmpeg's official source and licensing pages are
<https://ffmpeg.org/download.html> and <https://ffmpeg.org/legal.html>; its
canonical Git repository is <https://git.ffmpeg.org/ffmpeg.git>.

The binaries were imported from BtbN's
[`ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip`](https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip)
archive. The downloaded archive SHA-256 was
`40F8BCCCD4838A8C9A94C63DE976501A7BF044899BCBC6E02BCAF3C680EF7D35`.
The embedded version/configuration and the per-file SHA-256 values below identify
the files actually redistributed by this package.

| Packaged file | Bytes | SHA-256 |
| --- | ---: | --- |
| `runtimes/win-x64/native/avcodec-62.dll` | 70,883,840 | `76B3BEEC1E74A37B7BB64B717241DA1D6F63F6AC4B7ED3C6B051B084AA6F8E5C` |
| `runtimes/win-x64/native/avformat-62.dll` | 22,077,440 | `8188D265D5AE78EF0681990D5BBF44421D3C563D58B088A85229EE5CF2444BD2` |
| `runtimes/win-x64/native/avutil-60.dll` | 2,937,856 | `CD5F740954B77DE0751F0A7A99E792DD6BF3078888EDCEC185AD147F98172115` |
| `runtimes/win-x64/native/swresample-6.dll` | 723,968 | `984EA83CC14FE7337275A4882F8F392984A34D8749A2EA706E470FCA1BF03E34` |
| `runtimes/win-x64/native/swscale-9.dll` | 12,570,624 | `460218915ACFB8EEDA061E7BA1A5F5D1CA19F7617A736762E03BAA74FF25656A` |

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
  <https://github.com/FFmpeg/FFmpeg/archive/9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b.tar.gz>
- FFmpeg official source repository and license information:
  <https://git.ffmpeg.org/ffmpeg.git> and <https://ffmpeg.org/legal.html>
- FFmpeg Windows binary archive:
  <https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip>
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
