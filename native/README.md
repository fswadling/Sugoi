# Native ImGui library

`cimgui_sdl3.cpp` is a thin `extern "C"` shim over Dear ImGui's SDL3 platform
backend (`imgui_impl_sdl3`) and SDL_gpu renderer backend (`imgui_impl_sdlgpu3`),
so both can be P/Invoked from F#.

`build.sh` compiles a single `libcimgui.so` containing:

- cimgui's C API (`ig*` wrappers, from the `cimgui` repo)
- imgui core (docking branch)
- the SDL3 platform + SDL_gpu renderer backends (+ shaders)
- the `cimgui_sdl3.cpp` shim

The F# side P/Invokes `ig*` (`ImGui.fs`) and the shim (`ImGuiSdl3.fs`) against
this one library.

## Build

Prerequisites: `git`, a C++ compiler (`g++`/`clang++`), and SDL3 development
headers (`/usr/include/SDL3/SDL.h`, `SDL_gpu.h`).

```sh
./native/build.sh
```

The result is `native/libcimgui.so`. The project's `.fsproj` copies it next to
the apphost at build time so `DllImport("cimgui")` resolves it.

## Vendored SDL3_ttf (the game's text)

`libSDL3_ttf.so` is vendored into `native/` from the `ppy.sdl3_ttf-cs` NuGet
package's `runtimes/linux-x64/native/` (currently `2026.302.0`). The managed
bindings need no new package reference: `SDL3-CS` 3.4.14.1 already binds SDL_ttf,
including the GPU text engine (`TTF_CreateGPUTextEngine`,
`TTF_GetGPUTextDrawData`). Only the native library is missing, and
`DllImport("SDL3_ttf")` resolves it from the app directory.

Unlike `libSDL3_image.so`, it needs no FreeType or any other transitive library —
`ldd` reports only `libSDL3`, `libm`, `libgcc_s` and `libc` — so it is a single
file with no second dependency to preload. `fsharp-game.fsproj`'s
`CopySdl3TtfLibrary` target copies it next to the apphost, as
`CopySdl3ImageLibraries` does for the image library.

## Version pinning

`cimgui.cpp` is generated against cimgui's bundled imgui submodule, so the two
must move together. `build.sh` vendors the whole `cimgui` repo (branch
`docking_inter`) with its submodule for this reason. Pin to a specific commit
when the stack stabilises.
