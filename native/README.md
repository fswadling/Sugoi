# Native ImGui backend

`cimgui_sdl3.cpp` is a thin `extern "C"` shim over Dear ImGui's
`imgui_impl_sdl3` + `imgui_impl_sdlrenderer3` backends, so they can be
P/Invoked from F# (`ImGuiSdl3.fs`).

It compiles to `libcimgui_sdl3.so`, linked against the `libcimgui.so` that
ships with the `ImGui.NET` NuGet package. That library's SONAME is `cimgui.so`,
so the dynamic linker reuses the already-loaded ImGui core instead of loading a
second copy — `ImGuiNET.ImGui` and this backend share one ImGui context.

## Build

Prerequisites: `git`, a C++ compiler (`g++`/`clang++`), and SDL3 development
headers (`/usr/include/SDL3/SDL.h`).

```sh
./native/build.sh
```

The result is `native/libcimgui_sdl3.so`. The project's `.fsproj` copies it next
to the apphost at build time so `DllImport("cimgui_sdl3")` resolves it.

## Version pinning

The shim must be compiled against the **same imgui configuration** as the
bundled `libcimgui.so`, or ImGui aborts on a struct-layout mismatch:

- imgui **docking** branch, tag `v1.91.6-docking`
- `IMGUI_DISABLE_OBSOLETE_FUNCTIONS=1`

Both are enforced by `build.sh`.
