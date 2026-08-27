module ImGuiSdl3

// P/Invoke declarations for the native SDL3 platform + SDL_gpu renderer Dear
// ImGui backend (native/cimgui_sdl3.cpp, built into libcimgui.so).

open System.Runtime.InteropServices

[<DllImport("cimgui", EntryPoint = "cimgui_sdl3_init", CallingConvention = CallingConvention.Cdecl)>]
extern bool init(nativeint window, nativeint device)

[<DllImport("cimgui", EntryPoint = "cimgui_sdl3_shutdown", CallingConvention = CallingConvention.Cdecl)>]
extern void shutdown()

[<DllImport("cimgui", EntryPoint = "cimgui_sdl3_new_frame", CallingConvention = CallingConvention.Cdecl)>]
extern void newFrame()

[<DllImport("cimgui", EntryPoint = "cimgui_sdl3_process_event", CallingConvention = CallingConvention.Cdecl)>]
extern bool processEvent(nativeint ev)

[<DllImport("cimgui", EntryPoint = "cimgui_sdl3_prepare_draw_data", CallingConvention = CallingConvention.Cdecl)>]
extern void prepareDrawData(nativeint drawData, nativeint commandBuffer)

[<DllImport("cimgui", EntryPoint = "cimgui_sdl3_render_draw_data", CallingConvention = CallingConvention.Cdecl)>]
extern void renderDrawData(nativeint drawData, nativeint commandBuffer, nativeint renderPass)
