# Third-Party Notices

`FFmpegVideoPlayer.Avalonia` is MIT-licensed, but its NuGet package also
redistributes the native libraries identified below. Those libraries remain under
their own copyright and license terms; the project's MIT license does not replace
or restrict those terms.

## FFmpeg 8.1.2 release-branch build

Copyright (c) 2000-2026 the FFmpeg developers and other contributors.

### Windows x64

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

### Linux x64

The Linux x64 package payload contains the same five FFmpeg libraries, built from
the same FFmpeg source commit as the Windows DLLs. `av_version_info()` reports
`n8.1.2-34-g9b6c8969e0-20260731`: FFmpeg's 8.1 release branch at commit
[`9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b`](https://github.com/FFmpeg/FFmpeg/commit/9b6c8969e05b4f0b29f0f85cd501be6b3e582e6b);
only the build-date suffix differs from the Windows build (`20260731` instead of
`20260810`). Library versions are libavutil 60.26.102, libavcodec 62.28.102,
libavformat 62.12.102, libswresample 6.3.102 and libswscale 9.5.102. Each library
reports `LGPL version 3 or later`. Its embedded configure command contains
`--enable-version3 --enable-shared --disable-static`, disables GPL-only and nonfree
components (for example `--disable-libx264 --disable-libx265 --disable-libfdk-aac`),
and identifies the `/ffbuild` Linux x64 build environment
(`--cross-prefix=x86_64-ffbuild-linux-gnu- --target-os=linux --extra-version=20260731`).
Accordingly, these binaries are also distributed under
**GNU LGPL version 3 or later**.

The binaries were imported from BtbN's
[`ffmpeg-n8.1.2-34-g9b6c8969e0-linux64-lgpl-shared-8.1.tar.xz`](https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-07-31-14-10/ffmpeg-n8.1.2-34-g9b6c8969e0-linux64-lgpl-shared-8.1.tar.xz)
archive (release `autobuild-2026-07-31-14-10`). The downloaded archive SHA-256 was
`C882A80F06617149198A98A07A0880A7E881953AE9F9CB931F5BE09A4F93CAAE`.
This dated build was used instead of the moving `ffmpeg-n8.1-latest-linux64-lgpl-shared-8.1.tar.xz`
because, at import time (2026-11-18), the latest build had moved to `n8.1.3`.

Two changes were made, because NuGet does not preserve symbolic links:

1. Each real file `lib/<name>.so.<major>.<minor>.<micro>` was copied under its
   ELF `SONAME` (`libavutil.so.60`, `libavcodec.so.62`, `libavformat.so.62`,
   `libswresample.so.6`, `libswscale.so.9`), which is also the name FFmpeg.AutoGen loads.
2. `patchelf 0.19.2 --set-rpath '$ORIGIN'` added a `DT_RUNPATH` of `$ORIGIN`, so the
   libraries resolve each other from their own folder. The upstream files had no
   `RUNPATH`; no code or other dynamic entries were changed.

Repeating those two steps on the archive reproduces the packaged files byte for byte.
Besides each other, the libraries need only base system libraries (`libc.so.6`,
`libm.so.6`, `libmvec.so.1`, `libpthread.so.0`, `libdl.so.2`, `librt.so.1`,
`ld-linux-x86-64.so.2` and `libgcc_s.so.1`); the newest symbol versions they require
are `GLIBC_2.28` and `GCC_4.2.0`, so they need **glibc 2.28 or newer**.

| Packaged file | Bytes | SHA-256 |
| --- | ---: | --- |
| `runtimes/linux-x64/native/libavcodec.so.62` | 71,677,641 | `FE90FA807753E8583A5BFC9AF1FD64B2417EEF04A791EF9F7E43B1C8F7F41569` |
| `runtimes/linux-x64/native/libavformat.so.62` | 24,640,961 | `BCF62CA76555C859D26FCFD4675E1D289BD3FB778CA8DA459F0C20D0F060B028` |
| `runtimes/linux-x64/native/libavutil.so.60` | 8,404,073 | `B6025447A5BBBEE1E2A846665983D7E67491CEA2130CBB359681DC4CDF96888D` |
| `runtimes/linux-x64/native/libswresample.so.6` | 504,089 | `DF897461DEE6A92393F6C2D6BF3DA96A937454E54C144719B079FB3A547D9826` |
| `runtimes/linux-x64/native/libswscale.so.9` | 12,800,377 | `499BF171F66ECD71F1567C4C967ACAF9D9B6CE736C2FE41FFEA1972522592D10` |

The unmodified upstream files, before renaming and `RUNPATH`:

| Archive file | Bytes | SHA-256 |
| --- | ---: | --- |
| `lib/libavcodec.so.62.28.102` | 71,655,248 | `E764CB8E423B837B7C600F23CEE7188E548B1451EC190B4691B95386EA870FC3` |
| `lib/libavformat.so.62.12.102` | 24,615,520 | `FAD88584A80C883B7C1D159B90B9E3B26A2DF060DE42099F709A75BB61DD7EDA` |
| `lib/libavutil.so.60.26.102` | 8,377,032 | `19E62B5ED04178807B0489D7CFF8AA69D8F9BF99429A0740F9B438220ED5D248` |
| `lib/libswresample.so.6.3.102` | 498,632 | `BB6E402D91995F38A80D650B0C166C44F0618F3611175EB3FCA047D491DD337D` |
| `lib/libswscale.so.9.5.102` | 12,791,784 | `88C41A7E04A4A686FFB695480DD885B59F91044B9ED268CA287F1ADC21D33815` |

### License texts

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
- FFmpeg Linux x64 binary archive:
  <https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-07-31-14-10/ffmpeg-n8.1.2-34-g9b6c8969e0-linux64-lgpl-shared-8.1.tar.xz>
- FFmpeg build scripts (BtbN):
  <https://github.com/BtbN/FFmpeg-Builds>
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
may replace the DLLs or shared objects in their application's runtime output with modified,
interface-compatible builds. The package license terms do not prohibit reverse
engineering performed for debugging modifications to the LGPL-covered libraries.
No warranty is provided for the third-party software; see the complete license texts
for the controlling terms.
