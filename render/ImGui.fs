module ImGui

// Minimal P/Invoke binding to cimgui's C API (`ig*` functions). This is
// intentionally small — just what the current demo/editor needs — and should be
// grown as widgets are required (see native/ and docs for the rationale).

open System.Runtime.InteropServices

// ImGuiDockNodeFlags_PassthruCentralNode (1 << 3): the central node is
// transparent so the 3D viewport drawn behind ImGui shows through it.
// (NoDockingOverCentralNode is 1 << 2 — don't confuse the two.)
let passthruCentralNode = 8

// ImGuiInputTextFlags for the Inspector's name field (values from cimgui.h):
// submitting the field reports true, and opening it selects the existing text so
// typing replaces the name in one gesture.
let enterReturnsTrue = 1 <<< 6
let autoSelectAll = 1 <<< 12

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

[<DllImport("cimgui", EntryPoint = "igSmallButton", CallingConvention = CallingConvention.Cdecl)>]
extern bool smallButton(string label)

[<DllImport("cimgui", EntryPoint = "igCheckbox", CallingConvention = CallingConvention.Cdecl)>]
extern bool checkbox(string label, bool& v)

[<DllImport("cimgui", EntryPoint = "igSameLine", CallingConvention = CallingConvention.Cdecl)>]
extern void sameLine(float32 offsetFromStartX, float32 spacing)

[<DllImport("cimgui", EntryPoint = "igText", CallingConvention = CallingConvention.Cdecl)>]
extern void text(string fmt)

[<DllImport("cimgui", EntryPoint = "igSeparator", CallingConvention = CallingConvention.Cdecl)>]
extern void separator()

[<DllImport("cimgui", EntryPoint = "igCollapsingHeader_TreeNodeFlags", CallingConvention = CallingConvention.Cdecl)>]
extern bool collapsingHeader(string label, int flags)

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

// A single-line text field over a caller-owned NUL-terminated UTF-8 buffer
// (cimgui's igInputText). Returns true while the field is being edited.
[<DllImport("cimgui", EntryPoint = "igInputText", CallingConvention = CallingConvention.Cdecl)>]
extern bool inputText(string label, byte[] buf, unativeint bufSize, int flags, nativeint callback, nativeint userData)

// True for the frame in which the last item was deactivated after its value was
// edited (cimgui's igIsItemDeactivatedAfterEdit). Read immediately after the widget;
// merely focusing and leaving a field without editing does not set it.
[<DllImport("cimgui", EntryPoint = "igIsItemDeactivatedAfterEdit", CallingConvention = CallingConvention.Cdecl)>]
extern bool isItemDeactivatedAfterEdit()

// True while the last item is the active one (cimgui's igIsItemActive), i.e. it still has
// the keyboard or mouse focus, so an edit of it may be in progress. Read straight after the
// widget, like `isItemDeactivatedAfterEdit`.
[<DllImport("cimgui", EntryPoint = "igIsItemActive", CallingConvention = CallingConvention.Cdecl)>]
extern bool isItemActive()

// True for the frame a popup (or child) window first appears (cimgui's igIsWindowAppearing): a filtered combo
// uses it to clear its filter and focus the filter field each time the dropdown opens.
[<DllImport("cimgui", EntryPoint = "igIsWindowAppearing", CallingConvention = CallingConvention.Cdecl)>]
extern bool isWindowAppearing()

// Focus the *next* widget drawn (cimgui's igSetKeyboardFocusHere), so an opened combo's filter field takes
// typing without a click.
[<DllImport("cimgui", EntryPoint = "igSetKeyboardFocusHere", CallingConvention = CallingConvention.Cdecl)>]
extern void setKeyboardFocusHere(int offset)

// A draggable float field over a caller-owned value (cimgui's igDragFloat). Returns true
// while the value is being dragged; a caller wanting one commit per drag reads
// `isItemDeactivatedAfterEdit` straight after it.
[<DllImport("cimgui", EntryPoint = "igDragFloat", CallingConvention = CallingConvention.Cdecl)>]
extern bool dragFloat(string label, float32& v, float32 speed, float32 min, float32 max, string format, int flags)

// A bounded slider over a caller-owned value (cimgui's igSliderFloat). Returns true while the value is
// being dragged; the write is in place.
[<DllImport("cimgui", EntryPoint = "igSliderFloat", CallingConvention = CallingConvention.Cdecl)>]
extern bool sliderFloat(string label, float32& v, float32 min, float32 max, string format, int flags)

// A colour edit widget over a caller-owned `float[3]` (cimgui's igColorEdit3). The array marshals
// as a writable pointer, as `inputText`'s byte buffer does. Returns true while the colour is being
// edited; a caller wanting one commit per edit reads `isItemDeactivatedAfterEdit` straight after it.
[<DllImport("cimgui", EntryPoint = "igColorEdit3", CallingConvention = CallingConvention.Cdecl)>]
extern bool colorEdit3(string label, float32[] col, int flags)

// Visually disable (and block) the widgets drawn between these two calls.
[<DllImport("cimgui", EntryPoint = "igBeginDisabled", CallingConvention = CallingConvention.Cdecl)>]
extern void beginDisabled(bool disabled)

[<DllImport("cimgui", EntryPoint = "igEndDisabled", CallingConvention = CallingConvention.Cdecl)>]
extern void endDisabled()

// Push/pop an ID onto ImGui's ID stack (cimgui's igPushID_Str / igPopID). Widgets are keyed by
// their label plus this stack, so the cast panel pushes a member's name and then the look's before
// drawing a row: both repeat — the look labels under every member, and a row's own widgets under
// each of a member's looks — and without this they would share one ID.
[<DllImport("cimgui", EntryPoint = "igPushID_Str", CallingConvention = CallingConvention.Cdecl)>]
extern void pushId(string strId)

[<DllImport("cimgui", EntryPoint = "igPopID", CallingConvention = CallingConvention.Cdecl)>]
extern void popId()

// A drop-down over a caller-supplied preview (cimgui's igBeginCombo). Returns true while the popup
// is open, in which case the caller draws `selectableBool` items and calls `endCombo`. The label is
// what the widget is keyed by, and an empty preview draws nothing where the current value goes.
[<DllImport("cimgui", EntryPoint = "igBeginCombo", CallingConvention = CallingConvention.Cdecl)>]
extern bool beginCombo(string label, string previewValue, int flags)

[<DllImport("cimgui", EntryPoint = "igEndCombo", CallingConvention = CallingConvention.Cdecl)>]
extern void endCombo()

// Close the popup the current window is in (cimgui's igCloseCurrentPopup): a filtered combo uses it when Enter
// takes the top match, so the dropdown dismisses like a click would.
[<DllImport("cimgui", EntryPoint = "igCloseCurrentPopup", CallingConvention = CallingConvention.Cdecl)>]
extern void closeCurrentPopup()

// A selectable row (cimgui's igSelectable_Bool), returning true when it is clicked. `selected`
// marks the row as the current value; `size` of (0, 0) spans the available width.
[<DllImport("cimgui", EntryPoint = "igSelectable_Bool", CallingConvention = CallingConvention.Cdecl)>]
extern bool selectableBool(string label, bool selected, int flags, ImVec2 size)
