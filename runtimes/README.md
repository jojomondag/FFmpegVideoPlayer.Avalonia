Place FFmpeg native binaries in this folder using the standard NuGet RID layout:

- `runtimes/win-x64/native/*.dll` (bundled: FFmpeg 8.1, see `THIRD-PARTY-NOTICES.md`)
- `runtimes/linux-x64/native/*.so.<major>` (bundled: FFmpeg 8.1, glibc 2.28 or newer)
- `runtimes/win-x86/native/*.dll`
- `runtimes/osx-arm64/native/*.dylib`
- `runtimes/osx-x64/native/*.dylib`
- `runtimes/linux-arm64/native/*.so.<major>`

These files are packed into the NuGet so the control is self-contained on each platform. The runtime loader will automatically prefer these copies before falling back to system installations.

## Linux shared objects: real files, SONAME names, `$ORIGIN`

NuGet does not preserve symbolic links, so ship each library as a **real file named
by its ELF `SONAME`** (`readelf -d <file> | grep SONAME`): `libavutil.so.60`,
`libavcodec.so.62`, `libavformat.so.62`, `libswresample.so.6`, `libswscale.so.9`.
FFmpeg.AutoGen loads exactly these `lib<name>.so.<major>` names.

The libraries must find each other in their own folder. The bundled linux-x64 files
have `RUNPATH` `$ORIGIN`, added with `patchelf --set-rpath '$ORIGIN' <file>`. Changing
`LD_LIBRARY_PATH` at run time does not help, because glibc reads it only at process
start. Check a new set with `readelf -d` (SONAME, RUNPATH), `ldd -r` (siblings resolve
from the same folder; everything else is a base system library) and
`objdump -T <file> | grep -o 'GLIBC_[0-9.]*' | sort -uV | tail -1` for the minimum
glibc. Record the archive and per-file SHA-256 in `THIRD-PARTY-NOTICES.md`.

## macOS dylibs: beware of hardcoded paths

Do **not** just copy dylibs out of `/opt/homebrew/Cellar/ffmpeg/*/lib`. Those files have
absolute `LC_LOAD_DYLIB` entries pointing at `/opt/homebrew/opt/<formula>/lib/...`, so
`dlopen` silently fails on any Mac that doesn't have the exact same set of Homebrew
formulae installed. FFmpeg.AutoGen swallows the failure and replaces every function with
a stub that throws `NotSupportedException("Specified method is not supported.")` at call
time (e.g. during `avformat_open_input`).

If you want to bundle dylibs, you must rewrite them with
`install_name_tool -id @rpath/...` and `install_name_tool -change <abs> @loader_path/...`
for every transitive dependency, and ship all of those dependencies alongside them. The
simpler, more reliable option (and the one this project uses by default) is to **not**
bundle macOS binaries and require end users to `brew install ffmpeg`. The initializer
will auto-install via Homebrew on macOS when `autoInstall: true` (the default).
