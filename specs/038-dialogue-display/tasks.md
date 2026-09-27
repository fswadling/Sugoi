---

description: "Task list for data-driven dialogue display and the game's 2D layer"
---

# Tasks: Dialogue Display

**Input**: Design documents from `/specs/038-dialogue-display/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: **One new test file** — `test/DialogueTests.fs` — is added (research Decision 14, `plan.md`'s Testing section), so `test/fsharp-game.Tests.fsproj` changes. Only the two pure parts are driven headlessly: the **panel layout** (viewport + measured text → the three rects, with the too-tall refusal) and the **dismiss rule** (shown × pressed × text-focused). The drawing, the font, the SDL_ttf path, the loaded portrait's *result*, and the editor panel need a device, a font file, an asset or a frame, so they are `quickstart.md`'s interactive half — as `036` left its Inspector section and `037` its shader.

**Organization**: Tasks are grouped by user story. Phase 1 adds the four things a build cannot express as code (a vendored `.so`, two shaders, a font). Phase 2 is the two new modules and the wiring that makes the frame compile and show nothing — it is foundational because `Engine.fs` must create the layer and `GameplayState` must hold it before anything draws through it, and because every `.fsproj` compile entry lands in that one build. Phases 3–5 are the three stories. Phase 6 is the whole-suite check, the docs, the reads, and the walkthrough.

**Clarifications folded in** (spec.md `## Clarifications`, session 2026-09-26): a *thought* draws the panel **inverted** (light panel, dark border) — T008; the text box **always reserves the portrait column**, so its left edge and wrap width do not move between kinds — T008; a line whose wrapped box is **taller than the usable viewport height is left undrawn and surfaced in the authoring panel** rather than drawn clipped — T008/T010/T028; a character portrait set with a **gap fails at load** — T011/T012/T015; a **draw-time portrait miss leaves the line undrawn and is surfaced in the authoring panel, never raised** — T013/T027.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`, `editor/`, `render/`, `core/`).
- Compile order is defined by `fsharp-game.fsproj`. **Four files are added**, so that list changes: `render/Ui.fs` after `render/DebugDraw.fs` (it needs `Gpu` and `Texture`); `world/Dialogue.fs` after `render/Ui.fs` and before `world/Actor.fs` (it needs only `cast/Roster.fs`, and must precede `world/Systems.fs`); `world/DialogueRender.fs` after `world/Casting.fs` (it needs `render/Ui.fs`, `world/Assets.fs`, `world/Casting.fs` and `world/Dialogue.fs`); `editor/DialoguePanel.fs` after `editor/ZoneRegistryPanel.fs` and before `editor/Editor.fs`.
- `shaders/ui.vert`/`ui.frag` are compiled to `.spv` with `glslc` (`glslc -fshader-stage=vert shaders/ui.vert -o shaders/ui.vert.spv`) and join the `.fsproj` `CopyShaders` target.
- Tests live in `test/`; this feature adds `test/DialogueTests.fs` and one entry in `test/fsharp-game.Tests.fsproj`.
- All tasks are unchecked (`- [ ]`); this list is generated before implementation.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the four build inputs a `dotnet build` cannot express — a vendored native library, two compiled shaders, and a font — and confirm nothing else about the build changes.

- [X] T001 Confirm the build's assumptions before anything depends on them: `SDL3-CS` 3.4.14.1 is already referenced and already binds SDL_ttf (the `TTF_CreateGPUTextEngine`/`TTF_GetGPUTextDrawData`/`TTF_GPUAtlasDrawSequence`/`TTF_SetTextColor` entry points), so **no `PackageReference` changes**; `libSDL3_ttf.so` (from `ppy.sdl3_ttf-cs`) links only `libSDL3`, `libm`, `libgcc_s` and `libc` — **no FreeType, no second file** — so it needs none of the `libjbig.so.0` preload dance the image library needs; `glslc` is on the path; and `fsharp-game.fsproj`'s existing `CopySdl3ImageLibraries` (`AfterTargets="Build"`, copies to `$(OutDir)`) and `CopyShaders` (copies `.spv` to `$(OutDir)shaders`) are the two targets to extend (research Decisions 8 and 9).

- [X] T002 Vendor `native/libSDL3_ttf.so` from `ppy.sdl3_ttf-cs`'s `runtimes/linux-x64/native/`, add a `CopySdl3TtfLibrary` target to `fsharp-game.fsproj` beside `CopySdl3ImageLibraries` (same `AfterTargets="Build"` / `Condition="Exists(...)"` shape, copying the single file to `$(OutDir)`, because `DllImport("SDL3_ttf")` resolves from the app directory), and record in `native/README.md` how it is vendored and pinned, as the image library already is.

- [X] T003 [P] Add `shaders/ui.vert` and `shaders/ui.frag`: the vertex stage reads `location 0` `position.xy` (pixels), `location 1` `uv` and `location 2` `colour` (`Ubyte4Norm`), and applies one pushed ortho `mat4` at the vertex uniform set (the `line.vert` pattern); the fragment stage samples one texture at the fragment sampler set (`set = 2, binding = 0` — RENDERING_NOTES §4) and outputs `texture(uv) * colour`. Blending is the pipeline's, not the shader's. Compile both with `glslc` and add `shaders/ui.vert.spv;shaders/ui.frag.spv` to the `CopyShaders` `SourceFiles` list in `fsharp-game.fsproj`.

- [X] T004 [P] Add one font under `assets/fonts/` with its licence file beside it (the project ships no font today), covering the story's text. It is opened once by `Ui.create` (T006) and is a load failure if it will not open (research Decision 9).

**Checkpoint**: `libSDL3_ttf.so` resolves from the app directory, `shaders/ui.*.spv` exist and are copied, and a font is on disk with its licence. No managed dependency changed and no code compiles differently yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The dialogue state exists, the 2D layer exists, and the frame owns the layer. Nothing can draw a line until this lands.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 In `world/Dialogue.fs` (new), add the feature's state and its vocabulary, pure and free of GPU and assets: `Emotion = Neutral | Happy | Sad | Angry | Surprised` (the closed set the portrait-set check is over, FR-006), `Kind = PortraitDialogue of Character * Emotion | Dialogue | Thought` (the folded union — a portrait line always has a speaker and an expression, a speaker-less kind never has either, research Decision 2), `[<Struct>] type Dialogue = { mutable Shown : bool; mutable Kind : Kind; mutable Text : string }` and `let empty : Dialogue = { Shown = false; Kind = Dialogue; Text = "" }` (FR-001, FR-002). Add `<Compile Include="world/Dialogue.fs" />` to `fsharp-game.fsproj` after `render/Ui.fs` and before `world/Actor.fs`.

- [X] T006 In `render/Ui.fs` (new), add the game's 2D layer — technology-only, knowing `Gpu`, `Texture` and the font and nothing of characters or dialogue (research Decision 7): one **blended, depth-less** pipeline (`srcAlpha`/`oneMinusSrcAlpha`; `HasDepthStencilTarget = false`, as `DebugDraw`'s depth-off variant sets it), one **grow-only indexed quad batch** in reused arrays and one reused staging/uniform array (`DebugDraw`'s shape), one own **clamp-to-edge linear sampler** for the glyph atlas (`Texture.createSampler` is `private` and `Repeat`), one `TTF_Font` opened once and sized from the viewport, re-derived each frame (a change re-sizes the font and rebuilds the slots), and one `TTF_TextEngine` (the GPU engine). Implement `create` (fails loudly if the font or the engine will not initialize — research Decision 9), `begin`, `rect`, `image`, `text`, `setLine` (creates/updates the `TTF_Text` for the current line with its wrap width, walks `TTF_GetGPUTextDrawData` **once per build** into a reused `TextGeometry`, never per frame — FR-010), `upload`, `draw` (flushes per texture change) and `release`. Add the `<Compile Include="render/Ui.fs" />` entry after `render/DebugDraw.fs`.

- [X] T007 Wire the layer's lifetime and the state's home: in `Engine.fs` create `let ui = Ui.create device swapchainFormat "assets/fonts/…"` beside `debugDraw`/`gizmoDraw`, thread it through `ScreenManager.init` (`screen/ScreenManager.fs`), and release it at shutdown; in `state/GameplayState.fs` add `Ui : Ui.Ui` (held as the two debug drawers are) and `mutable Dialogue : Dialogue.Dialogue` seeded `Dialogue.empty`, and thread the layer through `GameplayState.create` (contracts; FR-001, FR-003). Depends on T006.

**Checkpoint**: `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` are green with every existing assertion unmodified (FR-012, SC-001); with `Shown = false` the frame is the pre-feature frame because nothing reads the dialogue state yet.

---

## Phase 3: User Story 1 - A line appears on screen (Priority: P1) 🎯 MVP

**Goal**: A shown line draws a panel over the scene carrying the speaker's name (when the kind has one) and the whole text, wrapped to fit and fully on screen at any viewport size.

**Independent Test**: put a line into the screen's dialogue state and confirm the panel and its text appear over the scene; confirm the text wraps and stays inside the panel and the panel stays on screen after a resize; clear it and confirm the frame returns to what it was — `quickstart.md` steps 1, 2, 3 and 5, plus the layout assertions.

- [X] T008 [US1] In `world/DialogueRender.fs` (new), add the view — the only place that knows the whole chain — and its pure layout: `[<Struct>] type Rect`, `[<Struct>] type PanelLayout = { Panel : Rect; Portrait : Rect; Text : Rect }`, the pure `innerWidth` (the panel inner width at a viewport, the width the text is wrapped to) and the pure `layout` (viewport + `hasPortrait` + measured text size → the three rects, or `None` when they would not fit; every figure a fraction of the viewport, so a resize keeps everything on screen and inside the panel — FR-004, SC-003). Two clarified rules live here: the **text box always reserves the portrait column**, so `Text.X` and the wrap width are the same for every kind and `hasPortrait` only decides whether `Portrait` is populated (spec Clarifications Q3); and a measured box **taller than the usable viewport height returns `None`** rather than a clipped layout (spec Clarifications Q4), which `draw` reports and leaves undrawn (T028). Then `draw : assets:AssetManager -> cast:Casting.Cast -> viewportW:float32 -> viewportH:float32 -> byref<Ui.Ui> -> Dialogue -> unit`, which **early-returns when `Shown` is false** (so nothing is measured or emitted — FR-003, SC-001), draws the panel with the **kind-dependent colours** (a near-black panel and light border for `Dialogue`/`PortraitDialogue`, an inverted light panel and dark border for `Thought` — spec Clarifications Q2), draws the speaker name from `Roster` when the kind has one (FR-002), and draws the text through `Ui.setLine`/`Ui.text` (FR-004; FR-010). Add the `<Compile Include="world/DialogueRender.fs" />` entry after `world/Casting.fs`.

- [X] T009 [US1] In `screen/GameplayScreen.fs`, call the view from `renderScene` **after the actors and still inside the existing scene pass** (`do DialogueRender.draw shared.Assets viewportW viewportH &state.Ui state.Dialogue`), passing the viewport size the screen already has. No pass is added and `Engine.fs`'s pass order is unchanged (research Decision 10; FR-003, FR-011).

- [X] T010 [US1] Add `test/DialogueTests.fs` and its `<Compile Include="DialogueTests.fs" />` entry in `test/fsharp-game.Tests.fsproj`, with the **layout** assertions (FR-004, SC-003): for a range of viewport sizes the panel, the portrait and the text box are inside the viewport and inside the panel; the panel and its contents scale with the viewport so a resize keeps everything on screen; a taller measured text grows the panel rather than overflowing it; the **text box is identical whether or not a portrait is drawn** (the reserved column — Clarifications Q3) and `Portrait` is empty only when `hasPortrait` is false; and a measured box taller than the usable viewport height **returns `None`** (Clarifications Q4/T028).

**Checkpoint**: a line put into the state draws over the scene; an empty state leaves the frame as it was; the layout is proven headlessly against viewport sizes and the two clarified rules.

---

## Phase 4: User Story 2 - The speaker's portrait (Priority: P2)

**Goal**: A portrait line draws the speaker's portrait for the line's expression, looked up from that character's declared portraits; nothing ever names a portrait, and a set with a gap fails at load.

**Independent Test**: show a portrait line and confirm that character's image for that expression appears; change the expression and confirm it changes; show a speaker-less kind and confirm no portrait; remove one expression from an actor manifest and confirm the app fails at load — `quickstart.md` steps 4 and 8.

- [X] T011 [US2] In `world/ActorAsset.fs`, add the portrait set and its completeness check: the private manifest record `[<SymbolicExpansion>] type PortraitPaths = { Neutral : string; Happy : string; Sad : string; Angry : string; Surprised : string }` (five **named** fields, matching the reference shape — research Decision 4), the runtime `type ActorPortraits = { Neutral : Texture.Texture; … }`, and `Portraits : ActorPortraits option` on `ActorAsset` (`None` when the manifest declares no `[Portraits …]` block). Add a pure `requireComplete : PortraitPaths -> unit` (or equivalent) that raises naming the missing expression when any path is `""`, called only when a block is present. This is a real trap, not a hypothetical: `Prime` defaults an absent manifest field to `""` (verified in 037), so a `[Portraits …]` block missing `Sad` yields an empty path rather than a parse error (FR-006, SC-008).

- [X] T012 [US2] In `world/Assets.fs`, point the actor load at the portraits: the private `Manifest` gains `Portraits : PortraitPaths option` and `readManifest` passes it through; when the block is present `loadActors` calls T011 `requireComplete` (**an absent field is a load failure**, FR-006, SC-008) and resolves and loads the five textures through the existing texture cache, storing `Some` set, and when it is absent it stores `None` — so a portrait-less actor still loads and simply cannot speak a portrait line. Loaded **once**, never re-read per line or per frame (FR-010; research Decision 5).

- [X] T013 [US2] In `world/DialogueRender.fs`, resolve and draw the portrait: the one `portraitOf : Emotion -> ActorPortraits -> Texture.Texture` match (exhaustive over the closed `Emotion`, so a sixth expression fails to compile — FR-005), resolve the character asset through the existing cast binding as a spawn does, draw it with `Ui.image` into the `Portrait` rect and pass `hasPortrait = true` for `PortraitDialogue`, and never expose a file or portrait name (FR-005, research Decision 4). *(The `Emotion ->` match is the only place the mapping exists.)* Resolution is `tryPortrait`, a `Result` rather than a raise — see T027.

- [X] T014 [US2] Add one character's portrait assets: an image per expression under `assets/actors/<Name>/portraits/` and the `[Portraits [[Neutral "…"][Happy "…"][Sad "…"][Angry "…"][Surprised "…"]]]` block in that actor's `.manifest` (the reference's images can supply these). This is what the walkthrough's step 4 selects (research Decision 12).

- [X] T015 [US2] Extend `test/DialogueTests.fs` with the portrait set's pure half (FR-006, SC-008): a complete `PortraitPaths` passes, and a set with any one field empty **raises** naming that expression — over string payloads only, no texture load; and T027's draw-time cases.

**Checkpoint**: a portrait line draws the character's image for its expression; a different expression draws a different image; a speaker-less kind draws none; a manifest with a gap fails at load rather than drawing a line without a portrait.

---

## Phase 5: User Story 3 - The player dismisses it, the author drives it (Priority: P3)

**Goal**: A shown line stays until the player dismisses it with SPACE (never while a text field has focus), and the author can set every field from a Dialogue panel and watch it appear.

**Independent Test**: show a line, leave it, confirm it stays; dismiss it and confirm it goes; set each field from the panel and confirm the screen reflects it; confirm SPACE does not dismiss while typing — `quickstart.md` steps 6, 7 and 9, plus the dismiss-rule assertions.

- [X] T016 [US3] In `core/Input.fs`, add `Space` to the `Key` union (**bit 21**; bits 0–20 are taken), its `mask` case, and the `SDL.Scancode.Space` mapping entry. Nothing else about the key behaviour changes — `isPressed` already gives the edge the dismiss needs (FR-008; research Decision 12).

- [X] T017 [US3] In `world/Systems.fs`, add the dismiss rule: `let dismissDialogue (pressed: bool) (textFocused: bool) (dialogue: byref<Dialogue>) : unit` clears `dialogue.Shown` only when `dialogue.Shown && pressed && not textFocused`, and otherwise leaves the state as it is. It writes by reference, as `cameraFollow`/`syncActors`/`updateTriggers` do, rather than returning state for the caller to assign (FR-008; research Decision 12).

- [X] T018 [US3] In `screen/GameplayScreen.fs`, apply the rule each frame from `update`, **inside the play-mode block**, so the dismiss is a play-mode control and the editor's SPACE is its own: `do Systems.dismissDialogue (Input.isPressed Input.Key.Space frame.Input) (ImGui.wantTextInput()) &state.Dialogue`. The `ImGui.wantTextInput()` gate is what keeps SPACE from dismissing while a field has focus (FR-008; contract).

- [X] T019 [US3] Extend `test/DialogueTests.fs` with the **dismiss rule** (FR-008, SC-004): a shown line clears only when pressed and not text-focused; pressing with nothing shown changes nothing; pressing while a text field has focus changes nothing; a frame with no press leaves a shown line shown (0 changes without input).

- [X] T020 [US3] In `state/EditorState.fs`, add the panel's transient text scaffolding beside `NameBuffer`/`PromptBuffer`/`NewZoneName`: `DialogueTextBuffer : byte[]` and `mutable DialogueTextEditing : bool`. This is UI plumbing for a text field, not dialogue state, so FR-009's "stores nothing beyond the state itself" holds (research Decision 13).

- [X] T021 [US3] Add `editor/DialoguePanel.fs` (new) and wire it in `editor/Editor.fs`: `val draw : SharedState.SharedState -> byref<GameplayState.GameplayState> -> unit`, a dock window like `CastPanel`/`ZoneRegistryPanel` carrying a shown checkbox, a kind combo (three), a speaker combo over `Roster.characters`, an expression combo (five) and a text field, all writing `state.Dialogue` **directly** — no command, nothing persisted (FR-009). No new ImGui binding is needed (`checkbox`, `beginCombo`/`selectableBool`/`endCombo`, `inputText` and `text` all exist). The speaker and expression rows are meaningful only for `PortraitDialogue`, so they are shown for that kind; and this panel, not the Inspector, hosts the surface because the whole body of the Inspector is inside `match state.Editor.Selection` and this state is not a selection (research Decision 13). The panel also surfaces why a configured line would not draw — the draw-time miss of FR-005 and the too-tall case of FR-004 — by asking `DialogueRender.missReason`; the view leaves the line undrawn and reports nothing. Add the `<Compile Include="editor/DialoguePanel.fs" />` entry after `editor/ZoneRegistryPanel.fs`, add `"Dialogue"` to the `panelNames` Ctrl+Tab cycle in `Editor.fs`, and call `DialoguePanel.draw shared &state` from `Editor.draw`.

- [X] T022 [US3] Add one line to `native/cimgui_sdl3.cpp`'s `cimgui_setup_dock_layout` docking the new **Dialogue** window (the dock layout is built in C++, and `io.IniFilename` is `nullptr`, so there is no persisted layout to fall back on), and rebuild `libcimgui.so` with `native/build.sh`; no new shim function is added (contract).

**Checkpoint**: a shown line is dismissed by SPACE and never while typing; the author sets the shown flag, the kind, the speaker, the expression and the text from the Dialogue panel and sees each take effect, with no undo step and nothing persisted.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation, the lines this feature makes false, the properties it claims, and the walkthrough.

- [X] T023 [P] Correct the lines this feature makes false in the repository docs: `docs/PROJECT_STRUCTURE.md` gains the new `render/Ui.fs`, `world/Dialogue.fs` and `world/DialogueRender.fs` modules and the Dialogue panel; `docs/EDITOR_DESIGN.md` D-011's direct-write list gains the Dialogue panel (alongside the cast binding, the zone registry and the actor animation/morph sections); `docs/RENDERING_NOTES.md` gains a note on the 2D layer, its blend state and the SDL_ttf text path.

- [X] T024 [P] Confirm the structural properties this feature claims by reading rather than assuming: `render/Gpu.fs` gains no pipeline (the layer owns its own, as `DebugDraw` does); `render/Mesh.fs`, `render/StaticMesh.fs`, `render/InstanceBuffer.fs`, `world/EnvironmentBatch.fs` and the mesh/static shaders are untouched (FR-011); `state/Command.fs` gains no command and no entity's captured state gains a field (FR-007); `render/ImGui.fs` gains no binding; `cast/Roster.fs` still names no portrait, asset or actor; and `world/Systems.fs` gains only the pure dismiss rule. Record the reads.

- [X] T025 Run `dotnet build fsharp-game/fsharp-game.fsproj --no-incremental` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the build is clean with no new warnings and the suite passes **with every pre-existing assertion unmodified** (FR-012, SC-001). `--no-incremental` is the build to run — an incremental build hides warnings on unchanged files. Confirm the existing per-frame allocation checks still observe zero bytes per frame with a line shown (SC-007). Record the counts and how many tests were added.

- [X] T026 Recompile the shaders (`glslc -fshader-stage=vert shaders/ui.vert -o shaders/ui.vert.spv`, and `frag`) and walk `quickstart.md` steps 1–9 in the editor: nothing shown is an unchanged frame; a line appears and wraps; the name shows only for a portrait line; the portrait follows the expression and nothing names a file; a thought draws distinctly (no name, no portrait, an inverted panel); SPACE dismisses and never while typing; a manifest missing one expression fails at load; and a dialogue change records no undo step and persists nothing. **Not run if the agent environment cannot be driven interactively** — and if it is not run, say so rather than implying the shaders, the SDL_ttf path or the panel were exercised.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks every user story**. T005 → T007 and T006 → T007 (the frame reads both the state and the layer); T005 and T006 both add a `fsharp-game.fsproj` compile entry, so they are one file's worth of edits.
- **User Stories (Phases 3–5)**: all depend on Phase 2. US1 (T008–T010) is the pure layout and the panel/name/text draw; US2 (T011–T015) is the portrait set, its load, and its draw; US3 (T016–T022) is the dismiss and the authoring panel.
- **Polish (Phase 6)**: depends on all of the above; T026 additionally depends on T004 (the font), T014 (the portraits) and T022 (the rebuilt dock layout).

### Within each phase

- Phase 2: T005 ∥ T006 (different files, until the shared `.fsproj` edit), then T007.
- Phase 3: T008 → T009 and T008 → T010.
- Phase 4: T011 → T012 → T013 (the loader needs the field, the draw needs the asset's set); T014 ∥ T013 (assets vs code); T015 after T011.
- Phase 5: T016 → T017 → T018; T019 after T017; T020 ∥ T021's start, then T021 → T022.
- Phase 6: T023 ∥ T024; T025 → T026.

### Parallel Opportunities

- **T003 ∥ T004**: the shaders (`shaders/`, plus a `CopyShaders` edit) and the font (`assets/fonts/`) are different files.
- **T005 ∥ T006**: the state (`world/Dialogue.fs`) and the layer (`render/Ui.fs`) are different files — the only contention is the `fsharp-game.fsproj` compile entry.
- **T013 ∥ T014**: the portrait draw (`world/DialogueRender.fs`) and the portrait assets (`assets/actors/…`) are different files.
- **T023 ∥ T024** in the polish phase: docs and reads, no dependencies.

### Same-file constraint

`fsharp-game.fsproj` is edited by T002, T003, T005, T006 and T008, so those compile-entry edits run in order. `world/DialogueRender.fs` is written by T008 (US1) and extended by T013 (US2), so US2 follows US1. `screen/GameplayScreen.fs` is touched by T009 (US1) and T018 (US3). `test/DialogueTests.fs` is added by T010 and extended by T015 and T019, so those run in priority order (US1 → US2 → US3).

---

## Implementation Strategy

### MVP

**US1 is the MVP.** It is the capability — a line appears whole on screen, wrapped and fully visible at any viewport size — and it covers the whole path from the state to the drawn panel and text. US2's portrait and US3's dismiss and authoring panel add the content and the controls to it.

1. Complete Phase 1 (the `.so`, the shaders, the font) and Phase 2 (the state, the layer, the wiring).
2. Complete Phase 3 (US1) — the pure layout, the panel/name/text draw, and the assertions.
3. **STOP and VALIDATE**: `dotnet test`, then put a line in the state and confirm the panel appears and the empty state is unchanged.

### Incremental Delivery

1. Setup + Foundational → the state and the 2D layer exist and the frame owns the layer.
2. US1 → a line appears, wrapped and on screen (MVP).
3. US2 → the speaker's portrait resolves from *(character, expression)* and a gap fails at load.
4. US3 → SPACE dismisses and the author drives every field from the Dialogue panel.
5. Polish → the whole-suite check, the docs, the reads, and the walkthrough.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- **The clarified spec is the target, not `plan.md`'s older sketch**: `data-model.md` and `contracts/dialogue-display.md` were written before the clarify session, so where they disagree, the four `## Clarifications` entries win — the thought's inverted panel, the always-reserved portrait column (the `Text` rect is the same for every kind; `hasPortrait` only populates `Portrait`), the loud refusal of a too-tall line, and the load-failure on a portrait-set gap. T008 implements the first three and T011/T012 the fourth.
- **Phase 2 is one build, not three**: `Engine.fs` must create the `Ui` before `ScreenManager.init` and `GameplayState.create` can take it, and `GameplayState`'s new fields break its constructor — so the state, the layer and the wiring are only green together. This is the same shape as `037`'s T002–T007.
- **The layout is the only dialogue arithmetic that is asserted**, so T008's `layout` must stay pure (viewport size + `hasPortrait` + the measured text size, nothing else) — the measuring step is the view's, through the font, because the font is the layer's and the device's.
- **The text must never be re-measured per frame**: `Ui.setLine` rebuilds the glyph geometry only when the line or the wrap width changes, or when the font is re-sized to a new viewport-derived size, because SDL_ttf's draw data is a pointer-linked list into the library's own memory (FR-010; research Decision 8). A rebuild every frame would allocate and marshal, which is exactly what SC-007 forbids.
- **The shaders must be recompiled before the app shows anything**: the `.spv` is loaded from disk at startup and the `.fsproj` copies it, so a stale `.spv` renders the old path — T026 leads with the two `glslc` steps for that reason.
- **The font and the portraits are assets, not code**: T004 and T014 add real files, so the walkthrough's steps 4, 5 and 6 need them; without the font nothing measures and without the portraits a portrait line has nothing to draw.
- Verify `dotnet test` passes after each phase; commit per task or logical group.

---

## Validation notes (implementation)

- `dotnet build fsharp-game.fsproj --no-incremental` — clean, 0 warnings, 0 errors.
- `dotnet build test/fsharp-game.Tests.fsproj --no-incremental` — clean, 0 warnings, 0 errors.
- `dotnet test test/fsharp-game.Tests.fsproj --no-build` — **296 passed, 0 failed, 0 skipped**
  (285 before this feature; **11 added**), with every pre-existing assertion unmodified.
- `glslc` compiled `shaders/ui.vert`/`shaders/ui.frag`; `native/build.sh` rebuilt `libcimgui.so`
  with the Dialogue dock slot; `libSDL3_ttf.so` and both `ui.*.spv` are copied to the output dir.
- **The interactive walkthrough (T026) was NOT run.** This environment cannot drive the windowed
  app, so the drawing, the font, the SDL_ttf text path, the loaded portrait's result and the editor
  panel are unverified by eye — they are `quickstart.md`'s steps and need a GPU and a person.
- The actor manifest gained its `[Portraits …]` block; the five Akane portraits and `DejaVuSans.ttf`
  (with its licence) are checked in under `assets/`.

---

## Follow-up fix (2026-09-26) — a draw-time portrait miss must not crash

- [X] T027 [US2] `DialogueRender.tryPortrait` returns `Result<Texture.Texture, string>` instead of
  raising, and `draw` resolves it **before** measuring or drawing anything: on `Error` a portrait
  line is not drawn at all and the reason is reported once per change (`eprintfn`), so an author who
  names a character before binding her never crashes the game — she can still reach the Cast panel
  that fixes it (FR-005, FR-006, SC-002; spec.md `## Clarifications`). `test/DialogueTests.fs` gains
  the unbound-character and no-portraits cases.
- [X] T028 [US1] A line too tall for the viewport no longer raises either: `DialogueRender.layout`
  returns `PanelLayout option` and `draw` reports once and leaves the line undrawn when it is `None`,
  so an author who types a very long line — reachable by typing, and by shrinking the window —
  cannot crash the game. `test/DialogueTests.fs`'s too-tall assertion becomes `IsNone` (FR-004,
  SC-003; spec.md `## Clarifications`).
- [X] T029 [US3] Both systems now update **by reference** rather than returning state for the caller
  to assign, matching `cameraFollow`/`syncActors`/`playerInput`: `Systems.dismissDialogue (pressed)
  (textFocused) (dialogue: byref<Dialogue>) : unit`, and `Systems.updateTriggers (zone) (closest:
  byref<EntityRef voption>) : unit` (the latter replaces its `EntityRef voption` return, which also
  adjusts a spec-029 surface — its contract, data model and research decision were updated to match).
  Callers (`GameplayScreen.update`) and tests (`DialogueTests`, `SystemsTests`) updated; both remain
  assertable headlessly.
- [X] T030 [US3] Both the dismiss and the animation clock now run **only while playing**: in
  `GameplayScreen.update`, `advanceActorAnimations` and `Systems.dismissDialogue` moved inside the
  `if not state.Editing` block, so the editor is passive (its own comment said so), SPACE is the
  editor's in edit mode, and an actor stands still while it is placed. FR-008, the US3 narrative and
  the walkthrough step 7 say so; the two walkthrough steps this makes false were corrected where they
  live (036's step 8 and 037's step 8 both asserted an actor *playing* its clip during the edit-mode
  walkthrough).

---

## Phase 7: Convergence

- [X] T031 Make the dialogue draw path allocate nothing per frame per FR-010, SC-007, Constitution V (`contradicts`). Probing the built assembly found every per-frame allocation in it: `DialogueRender.layout` allocates 64 B/call — the `Some` of its `PanelLayout option` return — and is called every frame a line is shown (640,000 B per 10,000 calls); `DialogueRender.tryPortrait` allocates ~416 B/call in the unbound case — its `Seq.filter`/`Seq.sortBy` pipeline materialises a sorted array and closes over the cast, and `Result` is a reference type — and is called every frame a portrait line is shown (4,162,864 B per 10,000 calls). `DialogueRender.speaker`'s `string option` returns are the same class (not measured; the JIT may elide them — make them `ValueOption` regardless). Fix by returning `voption` or a `[<Struct>]` result, replacing the `Seq` pipeline with a `for` loop over `cast.Bindings` that tracks the best look with no closure and no sort, and deferring any message formatting to the report path (which runs only when the report changes). Add allocation guards to `test/DialogueTests.fs` for `layout` and `tryPortrait` — both are pure and headless-callable, and `test/ActorTests.fs` has the `GC.GetAllocatedBytesForCurrentThread` pattern to copy — so this cannot regress.
- [X] T032 Reconcile `contracts/dialogue-display.md` with the implementation per plan: the surfaces it sketches (`contradicts`). §`Ui` still shows `create` without its `depthFormat` parameter, `begin` (the code is `beginBatch`, since `begin` is a keyword), a `Color` type where the code takes `Vector4`, `setLine text wrapWidth` (the code takes a slot and returns `(int * int)`), `text` without its colour, no `line`, `upload`/`draw` in the other parameter order, and a `TextGeometry` without `VertexCount`/`Vertices`/`Indices`/`TextRun`s; §`GameplayState` shows `Ui : Ui.Ui` where the code has it `mutable`; the `ui.vert` comment says the colour is `ubyte4 norm` where the code writes `Float4`; and `Rect` is declared in `DialogueRender` by the §`DialogueRender` sketch but lives in `Ui`. Update the contract to the implemented signatures or record the deviations in an Outcomes section, as `037`'s tasks did for `buildMorphDeltas`, and add the clause `research.md` Decision 12 lacks (the rule now updates by reference and runs only while playing).
- [X] T033 Reconcile `data-model.md`'s `Ui`, `TextGeometry` and panel-layout sketches with the implementation per plan (`partial`). The `Ui` record it sketches has `Draws : Draw[]`/`DrawCount`/`Text : TextGeometry`/`StagingBytes`/`Vertices`, where the code has `Runs`/`RunCount` plus `TextGeoms`/`TextW`/`TextH`/`TextObjects` (the text-slot pool that replaced one cached string), `VertexBytes`/`IndexBytes`, and `VertexBindings`/`SamplerBindings`/`White`; `TextGeometry` carries `VertexCount`/`Vertices`/`Indices`/`Runs : TextRun array` rather than `Runs : (Gpu.Texture * int * int) array`; and `Rect` is attributed to `DialogueRender` but lives in `Ui`. `layout`/`innerWidth` were already corrected — the record around them was not.
- [X] T034 Resolve the Dialogue panel's dock against `docs/EDITOR_DESIGN.md`'s Workspace rule per the plan's panel decision (`contradicts`). The doc says the sub-editors — Cast, Advent, Cue, Scenario, Zone Registry — dock as tabs in the left panel alongside the Outliner, and its Workspace table lists only Viewport/Outliner/Inspector/Asset Browser; the code docks Dialogue at the bottom (`native/cimgui_sdl3.cpp`, beside the Asset Browser) and no doc mentions it. Either move it to the left dock with the other sub-editors, or add it to the table and record the bottom dock as a deliberate exception — a line of text wants the bottom dock's width, where the 20 % left column would wrap it.
- [X] T035 Drop the unread `Ui.TextRun` fields per Constitution I (`unrequested`). `FirstVertex` and `VertexCount` are written in `rebuild` (`render/Ui.fs`) and read nowhere: `Ui.text` uses only `Texture`, `FirstIndex` and `IndexCount`, because the geometry is copied whole and re-based on `TextGeometry.VertexCount`. Remove the two fields, or use them to copy a run's own vertices — but do not keep data nothing reads.
- [X] T036 Record or remove the Dialogue panel's advisory line per FR-009 (`unrequested`). `editor/DialoguePanel.fs` prints "(no actor asset bound for this character — assign one in the Cast panel)" when the chosen speaker has no binding — the affordance that tells an author why a portrait line is not appearing (T027) — but no artifact describes it: FR-009 names only the five fields. Add it to the contract's panel bullet, or remove it if the surface is to only set fields.

**T031 outcome.** The draw path is allocation-free and now guarded by two tests in
`test/DialogueTests.fs` (`layout allocates nothing per call`, `tryPortrait allocates nothing per
call`): measured **0 bytes over 10,000 calls each**, against 64 B and ~416 B per call before. What
changed: `layout` returns `PanelLayout voption` and `speaker` returns `string voption` (a `Some` on a
struct record or a reference allocates); `tryPortrait` returns a `[<Struct>]` `PortraitResolution`
carrying a closed `MissReason` instead of a `Result`, and asks the binding per look (`ContainsKey` +
indexer) instead of a `Seq.filter`/`Seq.sortBy` pipeline; the report is keyed on that value, so no
message is built unless the report changes. One subtlety worth remembering: `match … TryGetValue …
with | true, v -> …` allocates a **32-byte tuple per call**, which was the last 64 B in the guard —
`ContainsKey` + the indexer is the allocation-free spelling. The resolution's tie-break is now the
first look in declaration order rather than the alphabetically first, which is equally deterministic
and needs no sort.

**T032–T036 outcomes.** The convergence batch is closed:

- **T032** — `contracts/dialogue-display.md` now sketches the implemented surfaces: §`Ui` carries the
  real field set, `create` with its `depthFormat`, `beginBatch`, `Vector4` colours, `setLine`'s slot
  and return, `line`, and `upload`/`draw` in the code's parameter order; §`GameplayState`'s `Ui` is
  `mutable`; the `ui.vert` comment says `float4 — 32 bytes a vertex`; `Rect` is `Ui.Rect` in both
  places it is used (the §`DialogueRender` sketch and the `Engine` example); and `research.md`
  Decision 12 now records that `dismissDialogue` writes by reference and runs only while playing.
- **T033** — `data-model.md`'s `Ui` record is the real one (the text-slot pool, `VertexBytes`/
  `IndexBytes`, `Runs`/`RunCount`, `White`, the two binding arrays); `TextGeometry` shows
  `VertexCount`/`Vertices`/`Indices`/`Runs : TextRun array`; the vertex table is 32 bytes with a
  `Float4` colour; and the panel-layout section no longer declares its own `Rect`.
- **T034** — the **bottom** dock was the right call and the doc was wrong: `docs/EDITOR_DESIGN.md`
  gained a Workspace row and a paragraph recording the Dialogue panel's bottom dock as a deliberate
  exception (a line of text wants the dock's width, where the 20 % left column would wrap it). The
  code is unchanged.
- **T035** — `Ui.TextRun.FirstVertex` and `.VertexCount` were written in `rebuild` and read nowhere
  (the geometry is copied whole, so only `FirstIndex`/`IndexCount` are needed); both fields are
  removed per Constitution I. Builds stay at 0 warnings, 301 tests pass.
- **T036** — the Dialogue panel's unbound-speaker advisory now has a home: the contract's panel
  bullet records it as the affordance that explains the draw-time portrait miss (FR-005), and notes
  it is a note rather than a second validation rule.

## Post-implement follow-ups (2026-09-27)

Changes made in review after `/speckit-implement`; where the task text above disagrees, these supersede
it.

- **A draw-time miss is surfaced in the authoring panel, not the console.** T027/T028/T031 describe
  reporting to `eprintfn` once per change, through a `Report` value threaded from `GameplayState` (and
  T036 describes the panel advisory as unbound-only). That is gone. `DialogueRender.draw` now keeps no
  state and prints nothing: it resolves the portrait to a `Texture voption`, leaves the line undrawn on
  a miss or when it does not fit, and returns. `editor/DialoguePanel.fs` asks the new
  `DialogueRender.missReason` and prints the reason — unbound speaker, asset not loaded, no portraits,
  or the line taller than the viewport — in the author's terms. `Report`, `GameplayState.DialogueReport`
  and the `&state.DialogueReport` threading were removed. `spec.md` gained a `## Clarifications` entry
  (session 2026-09-27) and FR-004/FR-005/FR-006 and SC-002/SC-003 were reworded to "surfaced in the
  authoring panel"; `plan.md`, `research.md` (Decision 6) and `quickstart.md` were reworded to match.
- **`Ui.line` was removed.** Reading a slot's cached geometry back is a plain field access
  (`ui.TextGeoms.[slot]`), and the one-line accessor obscured it. T032's note that the contract *gained*
  `line` is superseded: the contract's §`Ui` signature and `data-model.md`'s sketch no longer list it.
- **Names were made descriptive.** In `render/Ui.fs`, single-letter arguments and locals (`u`, `r`, `c`,
  `b`, `i`, `o`, `s`, `k`, and the `vb`/`ib`/`vBytes`-style abbreviations) were renamed (`ui`, `rect`,
  `colour`, `baseVertex`, `baseIndex`, `offset`, `srcOffset`, `vertexBindings`, `indexBinding`, …). In
  `world/DialogueRender.fs`, the private `Scheme` type became `Colours` (it holds only RGBA colours) and
  the `l` layout binding became `layout`.
- **Resource release was completed** — a general fix, not specific to 038, but it touches `Ui.release`.
  `Texture.release` was added (sampler and image); `Ui.release` now also frees its glyph sampler and the
  white placeholder; `world/Assets.fs` gained `AssetManager.Textures` and `Assets.release` (the
  placeholder and per-path textures, actor meshes and morph buffers, and static submeshes), called from
  `Engine.fs`; and shutdown now tears each zone down through `Zone.release`, so per-instance actor and
  prop buffers are freed too.

## Phase 8: Convergence

- [ ] T037 Review the resource-lifetime cleanup added outside spec 038's scope and either remove it or record it as a deliberate, separately-scoped fix, reconciling `plan.md`'s Structure Decision, which states "Nothing is added to `Gpu.fs`, `Mesh.fs` or the static path" and scopes `Assets.fs` to reading the portrait block only (`unrequested`). Evidence: `Texture.release` (`render/Texture.fs`), `Gpu.releaseBuffer` (`render/Gpu.fs`), `Mesh.release`/`StaticMesh.release` (`render/Mesh.fs`, `render/StaticMesh.fs`), `AssetManager.Textures` + `Assets.release` (`world/Assets.fs`), the extra frees in `Ui.release`, and `Engine.fs`'s shutdown now calling `Assets.release` and routing zones through `Zone.release`. None of it is called for by an FR, SC or task; it was done deliberately as a leak fix, so the likely resolution is to keep it and record the deviation rather than revert.
