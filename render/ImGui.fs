module ImGui

// Minimal P/Invoke binding to cimgui's C API (`ig*` functions). This is
// intentionally small — just what the current demo/editor needs — and should be
// grown as widgets are required (see native/ and docs for the rationale).

open System.Runtime.InteropServices

// ImGuiDockNodeFlags_PassthruCentralNode (1 << 3): the central node is
// transparent so the 3D viewport drawn behind ImGui shows through it.
// (NoDockingOverCentralNode is 1 << 2 — don't confuse the two.)
let passthruCentralNode = 8

// cimgui's `ImVec2_c` (see native/cimgui/cimgui.h): a blittable 8-byte pair of
// floats passed by value into widget calls.
[<Struct>]
type ImVec2 =
    val mutable x : float32
    val mutable y : float32
    new (x: float32, y: float32) = { x = x; y = y }

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

[<DllImport("cimgui", EntryPoint = "igBegin", CallingConvention = CallingConvention.Cdecl)>]
extern bool beginWindow(string name, nativeint pOpen, int flags)

[<DllImport("cimgui", EntryPoint = "igEnd", CallingConvention = CallingConvention.Cdecl)>]
extern void endWindow()

[<DllImport("cimgui", EntryPoint = "igButton", CallingConvention = CallingConvention.Cdecl)>]
extern bool button(string label, ImVec2 size)

[<DllImport("cimgui", EntryPoint = "igText", CallingConvention = CallingConvention.Cdecl)>]
extern void text(string fmt)

[<DllImport("cimgui", EntryPoint = "igDockSpaceOverViewport", CallingConvention = CallingConvention.Cdecl)>]
extern uint32 dockSpaceOverViewport(uint32 dockspaceId, nativeint viewport, int flags, nativeint windowClass)

[<DllImport("cimgui", EntryPoint = "cimgui_enable_docking", CallingConvention = CallingConvention.Cdecl)>]
extern void enableDocking()

[<DllImport("cimgui", EntryPoint = "cimgui_setup_dock_layout", CallingConvention = CallingConvention.Cdecl)>]
extern void setupDockLayout(uint32 dockspaceId)

[<DllImport("cimgui", EntryPoint = "igBeginMainMenuBar", CallingConvention = CallingConvention.Cdecl)>]
extern bool beginMainMenuBar()

[<DllImport("cimgui", EntryPoint = "igEndMainMenuBar", CallingConvention = CallingConvention.Cdecl)>]
extern void endMainMenuBar()

[<DllImport("cimgui", EntryPoint = "igBeginMenu", CallingConvention = CallingConvention.Cdecl)>]
extern bool beginMenu(string label, bool enabled)

[<DllImport("cimgui", EntryPoint = "igEndMenu", CallingConvention = CallingConvention.Cdecl)>]
extern void endMenu()

[<DllImport("cimgui", EntryPoint = "igMenuItem_Bool", CallingConvention = CallingConvention.Cdecl)>]
extern bool menuItem(string label, string shortcut, bool selected, bool enabled)

[<DllImport("cimgui", EntryPoint = "cimgui_want_text_input", CallingConvention = CallingConvention.Cdecl)>]
extern bool wantTextInput()

[<DllImport("cimgui", EntryPoint = "cimgui_set_window_focus", CallingConvention = CallingConvention.Cdecl)>]
extern void setWindowFocus(string name)

[<DllImport("cimgui", EntryPoint = "cimgui_is_window_active", CallingConvention = CallingConvention.Cdecl)>]
extern bool isWindowActive(string name)

// True while any ImGui control has captured the mouse (io.WantCaptureMouse).
// Context-safe (false with no current context).
[<DllImport("cimgui", EntryPoint = "cimgui_want_capture_mouse", CallingConvention = CallingConvention.Cdecl)>]
extern bool wantCaptureMouse()

// Main viewport (window) size in display pixels (io.DisplaySize). Context-safe
// ((0, 0) with no current context).
[<DllImport("cimgui", EntryPoint = "cimgui_display_size", CallingConvention = CallingConvention.Cdecl)>]
extern ImVec2 displaySize()
