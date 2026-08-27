module ImGui

// Minimal P/Invoke binding to cimgui's C API (`ig*` functions). This is
// intentionally small — just what the current demo/editor needs — and should be
// grown as widgets are required (see native/ and docs for the rationale).

open System.Runtime.InteropServices

[<DllImport("cimgui", EntryPoint = "igCreateContext", CallingConvention = CallingConvention.Cdecl)>]
extern nativeint createContext(nativeint sharedFontAtlas)

[<DllImport("cimgui", EntryPoint = "igDestroyContext", CallingConvention = CallingConvention.Cdecl)>]
extern void destroyContext(nativeint ctx)

[<DllImport("cimgui", EntryPoint = "igNewFrame", CallingConvention = CallingConvention.Cdecl)>]
extern void newFrame()

[<DllImport("cimgui", EntryPoint = "igRender", CallingConvention = CallingConvention.Cdecl)>]
extern void render()

[<DllImport("cimgui", EntryPoint = "igGetDrawData", CallingConvention = CallingConvention.Cdecl)>]
extern nativeint getDrawData()

[<DllImport("cimgui", EntryPoint = "igShowDemoWindow", CallingConvention = CallingConvention.Cdecl)>]
extern void showDemoWindow(nativeint pOpen)
