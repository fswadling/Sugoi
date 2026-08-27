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

## Version pinning

`cimgui.cpp` is generated against cimgui's bundled imgui submodule, so the two
must move together. `build.sh` vendors the whole `cimgui` repo (branch
`docking_inter`) with its submodule for this reason. Pin to a specific commit
when the stack stabilises.
