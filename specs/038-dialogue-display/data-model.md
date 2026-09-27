# Data Model: Dialogue Display

## `Emotion` — `world/Dialogue.fs` (new)

```fsharp
/// A character's expression. The closed set each character's portrait set must cover,
/// so "the portrait is derived" is a checkable claim rather than a hopeful one.
type Emotion =
    | Neutral
    | Happy
    | Sad
    | Angry
    | Surprised
```

The reference's five. A union rather than a string so it can be matched exhaustively (FR-006's
completeness is only checkable over a closed set) and so a misspelling is a compile error. It has no
`[<SymbolicExpansion>]` yet: nothing authors a dialogue line (no cue engine exists), so there is no
manifest to parse and the attribute would be a just-in-case. It is added with the cue op that needs it.

## `Kind` — `world/Dialogue.fs` (new)

```fsharp
/// How the line is presented: the reference's three kinds, with the speaker and expression
/// folded in where they apply, so a portrait line cannot lack a speaker and narration cannot
/// carry an expression.
type Kind =
    | PortraitDialogue of Character * Emotion
    | Dialogue
    | Thought
```

`PortraitDialogue (Akane, Sad)` is the data saying "Akane, sad" — the pair the portrait is looked up
from, and the only handle on it (FR-005). `Dialogue` and `Thought` are the reference's two
speaker-less kinds, differing in presentation: `Dialogue` and `PortraitDialogue` draw a near-black panel with a light border, `Thought` draws the same geometry inverted (a light panel with a dark border), and `Thought` draws no portrait and no name plate (spec.md `## Clarifications`).

## `Dialogue` — `world/Dialogue.fs` (new)

```fsharp
/// What the dialogue layer is showing. Runtime screen state on `GameplayState`: never authored,
/// never persisted, never on an entity.
[<Struct>]
type Dialogue =
    { /// On screen. The player clears it; the system that delivers lines waits for that.
      mutable Shown : bool
      /// The line. `Text` is the whole line — there is no reveal progress and no timer.
      mutable Kind : Kind
      mutable Text : string }

let empty : Dialogue = { Shown = false; Kind = Dialogue; Text = "" }
```

Three fields, and `Shown` is the only one that decides visibility, so there is no `Visible` flag that
can disagree with the other two. The state is `[<Struct>]` with explicit mutation, as the animation
and morph slots are, because the authoring surface writes it field by field and the dismiss writes
`Shown`.

## The dismiss rule — `world/Systems.fs` (new)

```fsharp
/// Clear a shown line when the dismiss key is pressed and no text field has focus, writing the
/// state in place. Still a pure function of its two booleans and that state, so the timing rules
/// are assertable without a window (FR-008).
let dismissDialogue (pressed: bool) (textFocused: bool) (dialogue: byref<Dialogue>) : unit
```

`pressed` is `Input.isPressed Input.Key.Space`; `textFocused` is `ImGui.wantTextInput()` at the call
site; the rule clears `dialogue.Shown` only when `dialogue.Shown && pressed && not textFocused`, and
otherwise leaves the state as it is. It updates **by reference**, as the file's other systems
(`cameraFollow`, `syncActors`, `updateTriggers`) do, rather than returning state for the caller to
assign; the rule is still a pure function of its inputs, which is what makes SC-004 ("0 changes
without input, 1 frame to clear") testable headlessly rather than by eye.

## The portrait set — `world/ActorAsset.fs` (changed), authored in the actor manifest

```fsharp
/// The portraits an actor asset declares, one per expression, resolved from the manifest
/// relative to the assets root and loaded once through the loader's texture cache.
[<SymbolicExpansion>]
type private PortraitPaths =
    { Neutral : string; Happy : string; Sad : string; Angry : string; Surprised : string }

type ActorPortraits =
    { Neutral : Texture.Texture; Happy : Texture.Texture; Sad : Texture.Texture
      Angry : Texture.Texture; Surprised : Texture.Texture }
```

and `Portraits : ActorPortraits option` on `ActorAsset` — `None` when the manifest declares no `[Portraits …]` block. The authored form, in the actor `.manifest`:

```
[[Id "akane"] [Name "Akane"] [Model "actors/Akane/Akane.iqm"] [Morphs "actors/Akane/akane.morphs"]
 [Portraits [[Neutral "actors/Akane/portraits/AkaneNeutral.png"] …]]]
```

Five **named** fields rather than an `Emotion`-keyed lookup, because `world/ActorAsset.fs` is compiled
before the dialogue module (research Decision 4) and because it is the reference's own shape. The one
place that maps an expression to a field is the view:

```fsharp
let private portraitOf (e: Emotion) (p: ActorPortraits) : Texture.Texture =
    match e with
    | Neutral -> p.Neutral
    | Happy -> p.Happy
    | Sad -> p.Sad
    | Angry -> p.Angry
    | Surprised -> p.Surprised
```

Matching on `Emotion` means a sixth expression fails to compile here rather than silently drawing the
wrong portrait.

## The panel layout — `world/DialogueRender.fs` (new), pure

```fsharp
[<Struct>]
type PanelLayout =
    { Panel : Ui.Rect      // `Rect` lives in `render/Ui.fs`, not here
      Portrait : Ui.Rect   // populated only when the kind draws one; the text box reserves its column either way
      Text : Ui.Rect }

/// The panel's inner width at this viewport — the width the text is wrapped to.
val innerWidth : viewportW:float32 -> float32

/// The panel, the portrait and the text box, from the viewport and the measured text height, or `None` when the measured box is taller than the usable viewport height (FR-004).
val layout : viewportW:float32 -> viewportH:float32 -> hasPortrait:bool -> textW:float32 -> textH:float32 -> PanelLayout voption
```

Every figure is a fraction of the viewport (a side margin, a bottom margin, a panel-height floor, a
portrait side), so the panel and its contents stay on screen at any size (FR-004, SC-003). The order
is: `innerWidth` from the viewport, **measure** the line at that width (the one impure step, through
the font), then `layout` with the measured height. The panel is content-sized, so the text cannot
overflow it (research Decision 11). The text box is the same whether or not a portrait is drawn — the portrait column is always reserved — and `layout` returns `None` instead of a box taller than the usable viewport height, which the view leaves undrawn (the authoring panel explains why).

## The 2D layer — `render/Ui.fs` (new)

```fsharp
[<Struct>]
type Ui =
    { Device : Gpu.Device
      Pipeline : Gpu.Pipeline
      VertexShader : Gpu.Shader
      FragmentShader : Gpu.Shader
      VertexBuffer : Gpu.Buffer
      IndexBuffer : Gpu.Buffer
      VertexTransfer : Gpu.TransferBuffer
      IndexTransfer : Gpu.TransferBuffer
      Sampler : Gpu.Sampler                 // clamp-to-edge, linear: the glyph atlas
      White : Texture.Texture               // the 1x1 placeholder the panel and name plate draw with
      Font : Font                           // = nativeint, TTF_Font (aliased like Gpu's handles)
      TextEngine : TextEngine               // = nativeint, TTF_TextEngine
      FontSize : int                        // the px size the font is currently set to
      TextObjects : TextObject array        // per slot: the TTF_Text the line is measured through
      TextGeoms : TextGeometry array        // per slot: the cached glyph geometry
      TextW : int array                     // per slot: the measured width
      TextH : int array                     // per slot: the measured height
      Vertices : float32 array              // reused; 8 floats per vertex
      Indices : uint32 array                // reused
      VertexBytes : byte array              // reused; the upload staging copy
      IndexBytes : byte array               // reused
      Runs : Run array                      // one per texture run, flushed on a texture change
      mutable RunCount : int
      mutable VertexCount : int
      mutable IndexCount : int
      UniformBytes : byte array             // the ortho matrix, reused
      VertexBindings : SDL.GPUBufferBinding array
      SamplerBindings : SDL.GPUTextureSamplerBinding array }

val create : device:Gpu.Device -> swapchainFormat:SDL.GPUTextureFormat -> depthFormat:SDL.GPUTextureFormat -> fontPath:string -> Ui
val beginBatch : byref<Ui> -> viewportW:float32 -> viewportH:float32 -> unit  // clears the batch (`begin` is a keyword)
val rect   : byref<Ui> -> Rect -> Vector4 -> unit
val image  : byref<Ui> -> Rect -> Texture.Texture -> Vector4 -> unit
val text   : byref<Ui> -> Rect -> TextGeometry -> Vector4 -> unit
val setLine : byref<Ui> -> slot:int -> text:string -> wrapWidth:int -> int * int  // rebuilds that slot; returns the measurement
val upload : Ui -> Gpu.CommandBuffer -> unit
val draw   : Ui -> Gpu.CommandBuffer -> Gpu.RenderPass -> unit
val release : Ui -> unit
```

A line is measured and drawn through a small **slot pool** (`textSlots` = 2: a name plate and a
body line) rather than one cached string, so both can be laid out in a frame without thrashing the
glyph cache. `setLine` is a no-op when its slot's line and wrap width are unchanged.

The font's **size follows the viewport**: `beginBatch` re-derives it from the viewport height and,
when it changes, re-sizes the font and drops each slot's cached geometry — whose atlas handles are
stale too — so `setLine` rebuilds at the new size and text scales with the panel on a resize.

**Vertex layout** — 32 bytes, a `Float4` colour to match the shader's `float4`:

| Offset | Field | Format |
|---|---|---|
| 0 | `position.xy` (in pixels; the ortho uniform maps it to clip space) | `Float2` |
| 8 | `uv` | `Float2` |
| 16 | `colour.rgba` | `Float4` |

**Draw runs** — the batch records `{ Texture; FirstIndex; IndexCount }` and flushes on a texture
change, so a dialogue panel is about three draws (the white placeholder for the panel and the name
plate, the portrait, the glyph atlas). One ortho matrix is pushed at vertex uniform slot 0, as
`DebugDraw` pushes its view-projection.

**`TextGeometry`** — the line's glyph quads, copied out of the engine once per line:

```fsharp
type TextGeometry =
    { Source : string             // the line this was built for
      WrapWidth : int             // the width it was wrapped to
      VertexCount : int
      IndexCount : int
      Vertices : float32 array    // 4 floats a vertex (xy in the engine's y-up space, then uv)
      Indices : uint32 array
      Runs : TextRun array }      // { Texture; FirstIndex; IndexCount }: one per atlas the line spans
```

Built by `setLine` and reused until the line, the wrap width or the font generation changes. The
engine's `TTF_GetGPUTextDrawData` is walked **once per build**, never per frame: it is a
pointer-linked list into the library's memory, so reading it every frame would marshal and allocate
(FR-010). The pool lives in the layer's own reused arrays, so a rebuild allocates only when the
`Runs` array outgrows its previous size.

## Authored versus runtime boundary

| | Authored | Runtime |
|---|---|---|
| A character's portraits | yes — the actor manifest's `[Portraits …]` block | no |
| The font | yes — a file under `assets/fonts/` | no |
| The dialogue state | **no** | yes — `GameplayState.Dialogue`, never persisted, never undone |
| A dialogue line's text | not yet — no cue engine exists, so lines are entered in the editor panel | — |

Nothing else is added to any authored format. In particular there is no dialogue manifest: the
feature's writer is the editor panel (FR-009), and the authored form arrives with the cue op that
delivers lines.

## Validation rules (load time)

- **The portrait block is optional, and complete when present.** When an actor manifest declares
  `[Portraits …]`, every one of the five fields must resolve to a non-empty path; an absent field is a load failure (FR-006, SC-008), while an actor with no `[Portraits …]` block loads with no portraits and cannot speak a portrait line. This is a real trap rather than a
  hypothetical: `Prime` defaults an absent manifest field to `""` — verified while settling 037's
  `Morphs` field — so a `[Portraits …]` block missing `Sad` yields an empty string, not an error.
- **Every portrait must load.** A path that does not resolve to a decodable image fails loudly, as
  every other texture in `Assets` does.
- **The font must load** and its text engine must initialize; either failing is a load failure.

## Validation rules (runtime)

- **A pair that does not resolve leaves the line undrawn.** Asking for a *(character, expression)*
  whose character is not bound to an actor asset, or whose asset declares no portraits, is an
  expected authoring state rather than a bug: nothing is drawn and the authoring panel shows why
  (FR-005, FR-006). It never raises — a half-set-up scene must not crash the game. (A pair the
  *set* does not cover cannot arise, because a declared set is complete at load.)
- **Text that will not measure or lay out fails loudly** rather than drawing an empty panel.
- **With `Shown = false` nothing is drawn and nothing is measured** — the frame is the pre-feature
  frame, and the cheapest way to guarantee that is to take the early exit before any font work.
