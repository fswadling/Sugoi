module ImGuiSdl3

// P/Invoke declarations for the native SDL3 + SDL_Renderer Dear ImGui backend.
//
// The native side lives in `native/cimgui_sdl3.cpp` and is compiled to
// `libcimgui_sdl3.so`, linked against the `libcimgui.so` bundled by ImGui.NET.
// Because that library's SONAME is `cimgui.so`, the dynamic linker reuses the
// already-loaded ImGui core, so `ImGuiNET.ImGui` and these functions share one
// context.

open System.Runtime.InteropServices

[<DllImport("cimgui_sdl3", EntryPoint = "cimgui_sdl3_init", CallingConvention = CallingConvention.Cdecl)>]
extern bool init(nativeint window, nativeint renderer)

[<DllImport("cimgui_sdl3", EntryPoint = "cimgui_sdl3_shutdown", CallingConvention = CallingConvention.Cdecl)>]
extern void shutdown()

[<DllImport("cimgui_sdl3", EntryPoint = "cimgui_sdl3_new_frame", CallingConvention = CallingConvention.Cdecl)>]
extern void newFrame()

[<DllImport("cimgui_sdl3", EntryPoint = "cimgui_sdl3_process_event", CallingConvention = CallingConvention.Cdecl)>]
extern bool processEvent(nativeint ev)

[<DllImport("cimgui_sdl3", EntryPoint = "cimgui_sdl3_render_draw_data", CallingConvention = CallingConvention.Cdecl)>]
extern void renderDrawData(nativeint drawData, nativeint renderer)
