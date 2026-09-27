# Contract: Dialogue Display

Public surface added or changed by this feature. Signatures are illustrative F#.

## `Dialogue` — `world/Dialogue.fs` (new)

```fsharp
type Emotion = Neutral | Happy | Sad | Angry | Surprised

type Kind =
    | PortraitDialogue of Character * Emotion
    | Dialogue
    | Thought

[<Struct>]
type Dialogue =
    { mutable Shown : bool
      mutable Kind : Kind
      mutable Text : string }

val empty : Dialogue
```

- Compiled after `cast/Roster.fs` (it names a `Character`) and before `world/Systems.fs` (which reads
  it). It depends on nothing else: no GPU, no asset, no presentation.
- `Kind` carries the speaker and the expression, so a portrait line always has both and a
  speaker-less line never has an expression (research Decision 2).

## `DialogueRender` — `world/DialogueRender.fs` (new)

```fsharp
// `Rect` is the 2D layer's (`Ui.Rect`) — the pixel rectangle its batch is built from.
[<Struct>]
type PanelLayout = { Panel : Ui.Rect; Portrait : Ui.Rect; Text : Ui.Rect }

// pure, so it is asserted headlessly; `ValueNone` when the measured box is taller than the usable
// viewport height
val innerWidth : viewportW:float32 -> float32
val layout :
    viewportW:float32 -> viewportH:float32 -> hasPortrait:bool ->
    textW:float32 -> textH:float32 -> PanelLayout voption

// the one place an expression names a portrait field; exhaustive over `Emotion`
val portraitOf : Emotion -> ActorPortraits -> Texture.Texture

// why a line was not drawn: a closed set, so a miss is a value the authoring panel names in the
// author's terms, rather than a message built on the draw path
type MissReason =
    | NotMissed | Unbound | AssetNotLoaded | NoPortraits | TooTallForViewport

// the outcome of resolving a pair. `[<Struct>]`, so the draw path allocates nothing — an
// `option`/`Result` here would allocate an object per call (FR-010)
[<Struct>]
type PortraitResolution =
    | Resolved of texture: Texture.Texture
    | Missed of reason: MissReason

// resolve a pair, or the reason it does not resolve. An expected miss — the author may name a
// character before binding her — so it is a value rather than an exception
val tryPortrait :
    assets:Assets.AssetManager -> cast:Casting.Cast ->
    character:Roster.Character -> emotion:Emotion -> PortraitResolution

// why `dialogue` would not draw as it stands, or `NotMissed` — the single place the view and the
// authoring panel agree, so the panel can explain an absence in the view's own terms
val missReason :
    assets:Assets.AssetManager -> cast:Casting.Cast -> byref<Ui.Ui> ->
    viewportW:float32 -> viewportH:float32 -> Dialogue -> MissReason

// the draw, called from the scene pass after the actors
val draw :
    assets:Assets.AssetManager -> cast:Casting.Cast ->
    viewportW:float32 -> viewportH:float32 -> byref<Ui.Ui> -> Dialogue -> unit
```

- The only place that knows the whole chain — `Character` → the cast binding → the asset's portraits
  → the atlas — and so the only place that maps an `Emotion` to a portrait field. Compiled after
  `world/Assets.fs`, `world/Casting.fs` and `render/Ui.fs`.
- **A draw-time portrait miss leaves the line undrawn, and never raises.** A character the author
  has named but not yet bound is expected, not a bug; crashing would lock her out of the Cast panel
  that fixes it. The resolution runs **before anything is measured or drawn**, so a line that cannot
  resolve draws nothing at all rather than a half-drawn panel (FR-005, FR-006). The view is silent
  and keeps no state: `missReason` is the one place the view and the authoring panel agree on why a
  line is absent, so the panel explains it and nothing is printed per frame. It returns a `[<Struct>]`
  `PortraitResolution` and asks the binding per look rather than enumerating it, so the call allocates
  nothing (FR-010).
- `draw` early-returns when `Shown` is false, so nothing is measured or emitted and the frame is the
  pre-feature frame (FR-003, SC-001); it draws the panel with kind-dependent colours (near-black for `Dialogue` and `PortraitDialogue`, inverted/light for `Thought`) and the text box is the same for every kind, because the portrait column is always reserved (spec.md `## Clarifications`).

## `Ui` — `render/Ui.fs` (new)

```fsharp
// Opaque SDL_ttf handles, aliased as `Gpu` aliases the SDL_gpu handles.
type Font = nativeint        // TTF_Font
type TextEngine = nativeint  // TTF_TextEngine
type TextObject = nativeint  // TTF_Text

// `Rect` is here, not in the view: the layer owns the pixel rectangle its batch is built from.
[<Struct>]
type Rect = { X : float32; Y : float32; W : float32; H : float32 }

val create : device:Gpu.Device -> swapchainFormat:SDL.GPUTextureFormat -> depthFormat:SDL.GPUTextureFormat -> fontPath:string -> Ui

val beginBatch : byref<Ui> -> viewportW:float32 -> viewportH:float32 -> unit  // `begin` is an F# keyword
val rect       : byref<Ui> -> Rect -> Vector4 -> unit
val image      : byref<Ui> -> Rect -> Texture.Texture -> Vector4 -> unit
val text       : byref<Ui> -> Rect -> TextGeometry -> Vector4 -> unit
val setLine    : byref<Ui> -> slot:int -> text:string -> wrapWidth:int -> int * int
val upload     : Ui -> Gpu.CommandBuffer -> unit
val draw       : Ui -> Gpu.CommandBuffer -> Gpu.RenderPass -> unit
val release    : Ui -> unit
```

- Knows `Gpu`, `Texture` and the font. It does **not** know `Character`, `Emotion`, `Dialogue`, or
  that dialogue exists (research Decision 7), so the HUD and the interact prompt can reuse it
  unchanged.
- One pipeline, blended and depth-less; one grow-only indexed quad batch in reused arrays; one
  clamp-to-edge linear sampler for the glyph atlas; one `TTF_Font` and one `TTF_TextEngine`.
- The font's **size follows the viewport**: it is a fraction of the viewport height, re-derived each
  frame in `beginBatch`. A size change re-sizes the font and drops each slot's cached geometry (whose
  atlas handles are stale too), so the text scales with the panel and survives a resize.
- **A small pool of text slots**, not one cached string: a frame draws two independent strings (a
  name plate and a line), and a single cache would rebuild both every frame (FR-010). `setLine` lays
  the string out in a slot and answers its measured `(width, height)`; the slot's `TextGeoms.[slot]`
  is the geometry it cached, which `text` then copies into the batch.
- **Colours are `Vector4`**, and the vertex colour is `Float4` (32 bytes a vertex), rather than the
  `Ubyte4Norm` this contract first sketched — the batch is built by hand, so the simpler layout wins.
- `setLine` rebuilds a slot's glyph geometry only when its line or wrap width changes; `upload`/`draw`
  never walk SDL_ttf's draw data (FR-010).
- `create` fails loudly if the font or the text engine will not initialize, and takes `depthFormat`
  because the pipeline's target info must name the depth format of the pass it draws in, as
  `DebugDraw` does.

## `ui.vert` / `ui.frag` — `shaders/` (new, compiled to `.spv`)

```glsl
// ui.vert: location 0 position.xy (pixels), location 1 uv, location 2 colour (float4 — 32 bytes a
//          vertex); one pushed ortho mat4 at set = 1, binding = 0 (the line.vert pattern).
// ui.frag: location 0 uv, location 1 colour; one sampler at the fragment sampler set
//          (set = 2, binding = 0 — RENDERING_NOTES §4); out = texture(uv) * colour.
```

- Blending is the pipeline's, not the shader's. Both files join the `.fsproj` `CopyShaders` target
  and are compiled with `glslc`.

## `native/libSDL3_ttf.so` (new) and its copy target

- Vendored from `ppy.sdl3_ttf-cs`'s `runtimes/linux-x64/native/libSDL3_ttf.so`. It needs no FreeType
  (research Decision 8), so unlike the image library it adds no second file.
- `fsharp-game.fsproj` gains a target beside `CopySdl3ImageLibraries` that copies it to `$(OutDir)`,
  because `DllImport("SDL3_ttf")` resolves from the app directory.
- `native/README.md` records how it is vendored and pinned, as it does for `libSDL3_image.so`.

## `ActorAsset` — `world/ActorAsset.fs` (changed) and `Assets` — `world/Assets.fs` (changed)

```fsharp
// ActorAsset.fs
[<SymbolicExpansion>]
type private PortraitPaths =
    { Neutral : string; Happy : string; Sad : string; Angry : string; Surprised : string }

type ActorPortraits =
    { Neutral : Texture.Texture; Happy : Texture.Texture; Sad : Texture.Texture
      Angry : Texture.Texture; Surprised : Texture.Texture }

type ActorAsset = { … ; Portraits : ActorPortraits option }   // CHANGED; None when undeclared
```

- The private actor `Manifest` gains `Portraits : PortraitPaths option`; `loadActors` resolves the five
  paths against the assets root, loads each through the existing `Textures` cache, and
  stores `Some` set — or `None` when the block is absent (FR-010).
- **An absent portrait *field* within a present block is a load failure**, not an empty path: `Prime`
  defaults a missing field to `""` (verified in 037), so the loader checks for it rather than
  attempting `""` as an image (FR-006, SC-008). An absent *block* is legal and yields `None`.
- Nothing else about the asset changes, and `Roster` still names no portrait.

## `Systems` — `world/Systems.fs` (changed)

```fsharp
val dismissDialogue : pressed:bool -> textFocused:bool -> byref<Dialogue> -> unit
```

- Updates the state **by reference**, as the file's other systems do (research Decision 12), rather
  than returning it for the caller to assign; the call site supplies `Input.isPressed Key.Space` and
  `ImGui.wantTextInput()`. The reference dismisses with SPACE and does not gate on text focus; this
  does, because the editor's text fields take the same keys.

## `Input` — `core/Input.fs` (changed)

```fsharp
type Key = … | Space                  // CHANGED: bit 21; 0..20 are taken
val mask : Key -> KeyMask             // CHANGED: a Space case
// and one mapping entry: SDL.Scancode.Space, Key.mask Key.Space
```

- No other key behaviour changes. `isPressed` already provides the edge the dismiss needs.

## `GameplayState` — `state/GameplayState.fs` (changed)

```fsharp
[<Struct>]
type GameplayState =
    { … ; mutable Dialogue : Dialogue.Dialogue   // CHANGED
      … ; mutable Ui : Ui.Ui }                   // CHANGED: `mutable`, as the two debug drawers are
```

- `Dialogue` is seeded `Dialogue.empty`, so a fresh screen shows nothing (FR-003).
- `Ui` is held as `DebugDraw` and `GizmoDraw` are: a per-screen renderer the screen draws with.

## `EditorState` — `state/EditorState.fs` (changed)

```fsharp
{ … ; DialogueTextBuffer : byte[]              // CHANGED
  … ; mutable DialogueTextEditing : bool }
```

- One keystroke buffer and its editing flag, beside `NameBuffer`, `PromptBuffer` and `NewZoneName`.
  This is transient UI scaffolding for a text field, not dialogue state, and it is why FR-009's
  "stores nothing beyond the state itself" is not violated: no command and nothing persisted
  (research Decision 13).

## `GameplayScreen` — `screen/GameplayScreen.fs` (changed)

```fsharp
// update: inside the play-mode block, so the dismiss is a play-mode control and the editor's SPACE
// is its own. Gated on a text field having focus too, because the editor's fields take the same keys.
do Systems.dismissDialogue (Input.isPressed Input.Key.Space frame.Input) (ImGui.wantTextInput()) &state.Dialogue

// renderScene: after the actors, still inside the scene pass (depth off; the layer is on top).
do DialogueRender.draw shared.Assets viewportW viewportH &state.Ui state.Dialogue
```

- The screen passes the viewport size it already has.
- No pass is added: the layer draws at the tail of the existing scene pass (research Decision 10).

## `DialoguePanel` — `editor/DialoguePanel.fs` (new) and `Editor` — `editor/Editor.fs` (changed)

```fsharp
// DialoguePanel.fs — a dock window like CastPanel/ZoneRegistryPanel.
val draw : SharedState.SharedState -> byref<GameplayState.GameplayState> -> unit

// Editor.fs
let private panelNames = [| …; "Dialogue" |]        // CHANGED: the Ctrl+Tab cycle
// Editor.draw:  do DialoguePanel.draw shared &state // CHANGED
```

- A shown checkbox, a kind combo (three), a speaker combo over `Roster.characters`, an expression
  combo (five) and a text field, all writing `state.Dialogue` directly. **No new ImGui binding**:
  `checkbox`, `beginCombo`/`selectableBool`/`endCombo`, `inputText` and `text` all exist. The panel
  asks `DialogueRender.missReason` and, when the line would not draw, prints the reason in the
  author's terms — unbound speaker, asset not loaded, no portraits declared, or the line taller than
  the viewport. This is the sole surface for a draw-time miss (FR-005, FR-006): the view leaves the
  line undrawn and reports nowhere, so the panel is what tells an author why a configured line is not
  appearing. The advisory is a note, not a second validation rule: it does not block the edit.
- Records no command and persists nothing, so it joins `docs/EDITOR_DESIGN.md` D-011's direct-write
  list (FR-009).

## `Engine` — `Engine.fs` (changed)

```fsharp
// beside debugDraw/gizmoDraw
let ui = Ui.create device swapchainFormat gpu.DepthFormat "assets/fonts/…"
let mutable manager = ScreenManager.init gpu debugDraw gizmoDraw ui      // CHANGED
// at shutdown
do Ui.release ui                                                          // CHANGED
```

- The layer's lifetime is the device's, as `DebugDraw`'s is. `ScreenManager.init` and
  `GameplayState.create` thread it through as they thread the two debug drawers.

## `native/cimgui_sdl3.cpp` (changed) — one line

- `cimgui_setup_dock_layout` docks the new window, so the panel opens docked like every other rather
  than floating (the dock layout is built in C++, and `io.IniFilename` is null so there is no
  persisted layout to fall back on). `libcimgui.so` is rebuilt with `native/build.sh`; no shim
  function is added.

## Assets (new)

- `assets/fonts/…` — one font with its licence, covering the story's text.
- `assets/actors/<Name>/portraits/…` — one image per expression, and that actor manifest's
  `[Portraits …]` block (the reference's images can supply these).

## Not changed

- `render/Gpu.fs` — no new pipeline in the shared context; `render/Ui.fs` owns its own, as
  `DebugDraw` owns the line pipeline.
- `render/Mesh.fs`, `render/StaticMesh.fs`, `render/InstanceBuffer.fs`, `world/EnvironmentBatch.fs`,
  the mesh and static shaders — the 3D scene and its draw path are untouched (FR-011).
- `render/ImGui.fs` — no new binding: every widget the panel needs is already exposed.
- `cast/Roster.fs` — still names no portrait, no asset and no actor.
- `state/Command.fs` — no command, and no entity's captured state gains a field (FR-007).
- The frame's pass order in `Engine.fs` — the dialogue draws inside the existing scene pass.
