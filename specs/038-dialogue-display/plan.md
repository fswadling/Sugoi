# Implementation Plan: Dialogue Display

**Branch**: `038-dialogue-display` | **Date**: 2026-09-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/038-dialogue-display/spec.md`

## Summary

The gameplay screen gains a dialogue state — whether a line is shown, its kind, its speaker with an expression, and its text — and the screen draws that line in screen space over the 3D scene: a panel, the speaker's name when the kind has one, the speaker's portrait for the line's expression, and the text wrapped to fit. A shown line stays until the player dismisses it; the editor can put a line there by hand.

Two things make this more than a field and a draw call.

**The portrait is derived, never chosen.** The line names *(character, expression)*; a character that speaks declares one portrait per expression, and declaring none is legal but leaves the character unable to speak a portrait line; the portrait is looked up from that set. So the feature adds a portrait set to the actor asset (the reference declares them on the asset too, one per emotion), and one exhaustive match is the only place that knows how an expression names a portrait.

**The game needs its own 2D layer.** The project's only screen-space drawing today is Dear ImGui, which is the editor's toolkit — its own documentation says it "is NOT designed to create a user interface for games". So this adds `render/Ui.fs`: one pipeline, one grow-only quad batch, and text. Text comes from SDL_ttf's GPU text engine, which hands back laid-out glyph quads in an atlas — so there is no glyph rasteriser, atlas packer, kerning or line breaker to write, and no new managed dependency: `SDL3-CS` 3.4.14.1 already binds SDL_ttf, and `libSDL3_ttf.so` needs no FreeType (it is statically bundled), so the whole native cost is one vendored `.so` and a copy target, exactly as `native/libSDL3_image.so` is already handled.

The drawing is technology-only (`render/Ui.fs` knows `Gpu`, `Texture` and the font, and nothing of characters or dialogue); the view that turns a dialogue state into draw calls is `world/DialogueRender.fs`; the two pure parts — the panel layout arithmetic and the dismiss rule — are asserted headlessly. Two clarified rules bind the layout: the text box always reserves the portrait column, so it does not move between kinds, and a line whose wrapped box would exceed the usable viewport height is left undrawn and surfaced in the authoring panel rather than drawn clipped. No draw-time condition reachable by ordinary authoring raises — only the load-time set check is loud.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **no new managed dependency**. `SDL3-CS` 3.4.14.1 already binds SDL_ttf (323 `TTF_` entry points, including the GPU text engine) and SDL_image, so the text path is reachable with no `PackageReference` change. One **vendored native library** is added — `native/libSDL3_ttf.so`, from the same `ppy.sdl3_ttf-cs` package the image library comes from — copied beside the apphost by a target beside `CopySdl3ImageLibraries`, because `DllImport("SDL3_ttf")` must resolve from the app directory. `ldd` on that `.so` shows only `libSDL3`, `libm`, `libgcc_s` and `libc`: no FreeType, no extra transitive library, unlike the image path which needed `libjbig.so.0`. **`glslc`** compiles the new `ui.vert`/`ui.frag`.

**Storage**: one **font asset** (`assets/fonts/…`, with its licence) and one **portrait image per character per expression** (`assets/actors/<Name>/portraits/…`, which the reference's images can supply). Nothing is persisted at runtime: the dialogue state is screen state.

**Testing**: a new `test/DialogueTests.fs` for the two pure parts, so both `.fsproj` files change (this feature adds files, unlike 036 and 037):
- the **panel layout** — viewport size → panel rect, portrait rect and text origin, as proportions of the viewport, so a resize keeps everything on screen and inside it, with the text box identical whether or not a portrait is drawn, and `None` (left undrawn, and the authoring panel explains why) when the measured box would not fit;
- the **dismiss rule** — a function of (shown, key pressed, text field focused) that clears only when a line is shown, the key is pressed, and no field has focus.

What no headless test reaches is the drawing itself (a device), the font and the SDL_ttf path (a device and a font file), the portrait resolution's *result* (a loaded asset), and the editor panel (a frame). Those are [quickstart.md](./quickstart.md), as `036` and `037` left their Inspector sections and shader.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu.

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold 60 FPS, and the 2D layer must not disturb the 3D frame (SC-007, FR-011). The layer is a handful of draw calls for a whole dialogue panel — the quad batch flushes only when the texture changes, so a panel with its portrait and its text is about three. Nothing allocates per frame: the vertex batch is a reused array (`DebugDraw`'s shape), and the text geometry is built **once per line change** and re-drawn, because SDL_ttf's draw data is a pointer-linked list into the library's own memory and walking it every frame would allocate and waste.

- **Constraints**: the state is screen state, not persisted, not undoable, not on an entity (FR-007); the portrait is derived from *(character, expression)* and no field or surface may name a file (FR-005); a character whose declared portrait set has a gap fails at load, while a draw-time portrait miss leaves the line undrawn and is surfaced in the authoring panel rather than raising (FR-005, FR-006, SC-008); a thought draws the panel inverted (FR-002); the text box always reserves the portrait column and a line taller than the usable viewport height is left undrawn and surfaced in the authoring panel rather than clipping (FR-004); a shown line stays until dismissed, and the dismiss must not fire while a text field has focus (FR-008); no per-frame allocation (FR-010, SC-007); nothing shown must leave the frame identical to before (FR-003, FR-012).

**Scale/Scope**: a new `[<Struct>]` state and its kinds in `world/Dialogue.fs`; a portrait set on the actor asset (manifest field + loader); a new `render/Ui.fs` (one pipeline, one batch, one font) and `shaders/ui.vert`/`ui.frag`; a new `world/DialogueRender.fs` (the view); one field on `GameplayState`; one dismiss system in `world/Systems.fs`; `Key.Space` in `core/Input.fs`; a new `editor/DialoguePanel.fs` + its dock slot + one transient text buffer in `EditorState`; a new `test/DialogueTests.fs`; one vendored native library and its copy target; a font asset and portraits. Both `.fsproj` files change, and `native/cimgui_sdl3.cpp` gains one line — the new panel's dock slot — so `libcimgui.so` is rebuilt with `native/build.sh`. SDL_ttf itself needs no C++: it is reached through the `SDL3-CS` bindings the project already has. No command and no persistence.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one screen-state record with a closed union, one 2D module, one view, one editor panel, one system, one key. No new managed dependency, no new shim function and no new pass in the frame: the only C++ is one line of dock layout. The one native addition (a vendored `.so`) buys an entire text engine — rasteriser, atlas, kerning and line breaking — that would otherwise be ours to write and maintain, so it is the smaller option, not the larger one. |
| II. Fail Loudly | Pass — a character whose portrait set has a gap fails at load, and the font and text engine fail loudly if they cannot initialize. The two draw-time conditions reachable by ordinary authoring are surfaced in the authoring panel instead of raised: a *(character, expression)* pair that does not resolve (an unbound character is normal while authoring, and a crash would lock the author out of the Cast panel that fixes it) and a line too tall for the viewport (reachable by typing). Nothing renders a line with a silently missing portrait, and nothing clips. |
| III. Spec-First | Pass — `specs/038-dialogue-display/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the state is a `[<Struct>]` record with explicit, localised mutation, as the animation and morph slots are; the 2D layer's batch is one contiguous float array with an index buffer, reused per frame, and the text geometry is a contiguous copy of the engine's output rather than a live handle on it. The line's kind is a discriminated union, so an impossible line is unrepresentable. |
| V. Minimise GC Pressure | Pass — the per-frame path writes into reused arrays and allocates nothing (SC-007); the text geometry is rebuilt only when the line changes; the font, the atlas and the portraits are loaded once. |
| N-V. Testing is the Authoring Tool | Not engaged — it governs the narrative subsystem, which is untouched. |

## Complexity Tracking

None. One addition is worth recording rather than justifying as a violation, because it is a *dependency* decision: **a vendored `libSDL3_ttf.so` and a font asset**. The alternatives were a build-time atlas plus our own wrapping and kerning (more code, and a tool to maintain), a render-to-surface path (fewer glyph-level controls and a texture per line), or reusing ImGui's font atlas (new C++ shim functions and a coupling to the editor's toolkit). The vendored library needs no FreeType, is already bound by the dependency the project has, and is copied the way the image library already is.

## Project Structure

### Documentation (this feature)

```text
specs/038-dialogue-display/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── dialogue-display.md  # Phase 1 output
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
shaders/
└── ui.vert(.spv)        # NEW: a screen-space sprite shader — position.xy + uv + colour,
    ui.frag(.spv)        #   one ortho uniform, one sampler (fragment set), texture × colour.
                         #   Compiled with `glslc`; the `.fsproj` copy target gains both.
render/
└── Ui.fs                # NEW: the 2D layer. One blended, depth-less pipeline; one grow-only
                         #   indexed quad batch (reused, `DebugDraw`'s shape); an SDL_ttf font
                         #   and GPU text engine; `rect`, `image`, `text`, `upload`, `draw`,
                         #   `release`. Knows Gpu/Texture/the font only — no game vocabulary.
world/
├── Dialogue.fs          # NEW: `Emotion` (five cases), `Kind` (the folded union), and the
│                        #   `[<Struct>]` `Dialogue` state. Pure; no GPU, no asset.
├── DialogueRender.fs    # NEW: the view — resolves the name from `Roster` and the portrait
│                        #   from `Casting`/the asset, and emits `Ui` draw calls for the panel.
├── ActorAsset.fs        # CHANGED: the actor manifest's portrait block and the asset's
│                        #   portrait textures, loaded once.
├── Assets.fs            # CHANGED: `loadActors` reads the portrait block and loads the five
│                        #   portraits through the existing texture cache (no per-line reload).
└── Systems.fs           # CHANGED: `dismissDialogue` — the dismiss rule applied by reference, called every
                         #   frame from the screen.
core/
└── Input.fs             # CHANGED: `Key.Space` (bit 21), its mask case and its scancode.
state/
├── GameplayState.fs     # CHANGED: `mutable Dialogue : Dialogue.Dialogue` beside the camera
│                        #   and the editor state.
└── EditorState.fs       # CHANGED: one transient text buffer for the panel's text field,
                         #   beside `NameBuffer`/`PromptBuffer`.
screen/
└── GameplayScreen.fs    # CHANGED: the dismiss step in `update` (play mode); the dialogue
                         #   draw in `renderScene`, after the actors and inside the scene pass.
editor/
├── DialoguePanel.fs     # NEW: the authoring surface — shown, kind, speaker, expression, text.
└── Editor.fs            # CHANGED: draw the panel; add it to the Ctrl+Tab cycle.
Engine.fs                # CHANGED: create and release the `Ui` beside `debugDraw`/`gizmoDraw`.
native/
├── libSDL3_ttf.so       # ADD: vendored (from `ppy.sdl3_ttf-cs`), no FreeType dependency.
├── cimgui_sdl3.cpp      # CHANGED: one line — the Dialogue panel's dock slot; `libcimgui.so`
│                        #   is rebuilt with `native/build.sh`.
└── README.md            # CHANGED: how the ttf library is vendored, as the image one is.
fsharp-game.fsproj       # CHANGED: the new compile entries (Ui, Dialogue, DialogueRender,
                         #   DialoguePanel) and a target copying `native/libSDL3_ttf.so`.
test/
├── DialogueTests.fs     # NEW: the panel layout and the dismiss rule.
└── fsharp-game.Tests.fsproj  # CHANGED: the new test entry.
assets/
├── fonts/…              # ADD: a font with its licence.
└── actors/<Name>/portraits/… # ADD: one image per expression for a character, and the
                         #   portraits block in that actor's manifest.
docs/
├── PROJECT_STRUCTURE.md # CHANGED: the new render/world/editor modules.
├── EDITOR_DESIGN.md     # CHANGED: D-011's direct-write list gains the dialogue panel.
└── RENDERING_NOTES.md   # CHANGED: a note on the 2D layer, the blend state and SDL_ttf.
```

**Structure Decision**: the split follows what each part needs to know. The **state** (`world/Dialogue.fs`) needs only `Roster`, so it sits early and stays free of the asset and the GPU — which is what lets `GameplayState` hold it and a system read it. The **2D layer** (`render/Ui.fs`) needs `Gpu` and `Texture` and is deliberately blind to characters, expressions and dialogue, so the HUD and prompts can reuse it; it is compiled with the other render modules. The **view** (`world/DialogueRender.fs`) is the only place that knows the whole chain — a `Character`, the cast binding, the asset's portraits, the font — so it is compiled after `Assets.fs` and `Casting.fs`, beside `world/EnvironmentRender.fs`, which is the existing precedent for a renderer living in `world/`. The **authoring surface** is its own docked panel rather than an Inspector section, because the Inspector's whole body is inside `match state.Editor.Selection` and the dialogue state is not a selection. `Engine.fs` owns the `Ui`'s lifetime exactly as it owns `DebugDraw`'s, and `GameplayState` holds it as it holds those two. Nothing is added to `Gpu.fs`, `Mesh.fs` or the static path.

## Phase 0: Research

See [research.md](./research.md). It settles why the state is the screen's, why the kind folds the speaker and expression together, why the portrait set lives on the actor asset while `Emotion` lives with the dialogue, why the 2D layer is its own technology-only module, why SDL_ttf's GPU text engine beats the alternatives and why its geometry is built once per line, how the panel is sized and placed, how the dismiss key is gated, and why the authoring surface is a panel.

## Phase 1: Design

- [data-model.md](./data-model.md) — `Emotion`, `Kind`, `Dialogue`, the portrait set on the asset, the draw-order description the view emits, and the authored-versus-runtime boundary.
- [contracts/dialogue-display.md](./contracts/dialogue-display.md) — the surfaces that change: `render/Ui.fs`, `world/Dialogue.fs`, `world/DialogueRender.fs`, the asset's portraits, `Assets`, `Systems`, `Input`, `GameplayState`, `EditorState`, `GameplayScreen`, the editor panel, the shaders, `Engine.fs`, and what explicitly does not change.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite, the shader compile, the native library and font, and the interactive walkthrough.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: one state record, one 2D module, one view, one panel, one system, one key, one vendored library. |
| II. Fail Loudly | Pass — a portrait-set gap and a font or engine that will not initialize fail loudly; the two draw-time conditions reachable while authoring (an unresolvable portrait, a line too tall for the viewport) are surfaced in the authoring panel and leave the line undrawn instead of raising. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a `[<Struct>]` state with localised mutation, a closed union for the line, and a contiguous reused batch for the drawing. |
| V. Minimise GC Pressure | Pass — nothing allocates per frame; the text geometry is rebuilt only on a line change. |
| N-V. Testing is the Authoring Tool | Not engaged. |
