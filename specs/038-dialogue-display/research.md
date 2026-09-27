# Research: Dialogue Display

Decisions derived from the feature spec and the code as it stands (`state/GameplayState.fs`,
`state/SharedState.fs`, `state/EditorState.fs`, `render/Gpu.fs`, `render/Texture.fs`,
`render/DebugDraw.fs`, `core/Input.fs`, `render/ImGui.fs`, `world/Assets.fs`, `world/ActorAsset.fs`,
`world/Casting.fs`, `cast/Roster.fs`, `world/Systems.fs`, `editor/Editor.fs`, `editor/*Panel.fs`,
`screen/GameplayScreen.fs`, `Engine.fs`, `native/{build.sh,README.md,.gitignore}`, and
`docs/RENDERING_NOTES.md`), with `odin-game`'s `src/exposition.odin`, `src/entity.odin`,
`src/systems.odin` and `src/cue.odin` as the reference.

## Decision 1 — The dialogue state is the gameplay screen's

- **Decision**: the state is one field on `GameplayState`, beside `Camera`, `Editor` and
  `InteractableTrigger`. It is not on a zone, not on an entity, and not on `SharedState`.
- **Rationale**: it is *screen* state: what is on screen right now, like the camera and the editor's
  selection. `SharedState` is the wrong home because it is the cross-screen singleton set (device,
  assets, physics, the cast binding and the zone registry), and the dialogue is per-screen. A zone is
  the wrong home because dialogue is not scene content — the reference keeps `exposition` on the
  gameplay screen's cold state for the same reason — and because `Zone` owns entity slot maps and a
  batch, which this has nothing to do with. An entity is impossible: the command model forbids an
  entity's captured authored state holding anything the editor cannot record, and FR-007 says this
  state records no command and is never persisted.
- **Alternatives considered**: on `Zone` (then a zone switch swaps it, and the state sits beside
  entity data it does not belong to); on `SharedState` (then it survives a screen change, which is
  wrong — it would linger over the main menu); a module-level singleton (`Editor.fs`'s comment
  already rejects those: "transient editor state lives on `EditorState`, not in module-level
  singletons").

## Decision 2 — The line's kind folds the speaker and the expression together

- **Decision**: `Kind = PortraitDialogue of Character * Emotion | Dialogue | Thought`, and the state
  holds `Kind`, `Text` and `Shown`. There is no separate `Speaker` field and no separate `Emotion`
  field.
- **Rationale**: it makes the two impossible lines unrepresentable — a portrait line with no speaker,
  and narration carrying an expression — which is the same argument `cast/Roster.fs` makes for its
  own union ("a union rather than a string so that a name cannot be misspelled, so matching on a
  character is exhaustive"). It also collapses the reference's three fields (`kind`, `speaker_name`,
  `emotion`) into one that carries exactly the data each kind needs: the reference's `speaker_name`
  and `emotion` are only meaningful when `kind == .PortraitDialogue`, and its own editor enforces
  that by hand, clearing them when the kind changes.
- **Alternatives considered**: three flat fields (`Kind` + `Speaker : Character option` +
  `Emotion`), which reads closer to the reference but admits `Speaker = None, Emotion = Sad`; a
  `Line` record plus a separate kind, which is two types where one union does.

## Decision 3 — `Emotion` is the dialogue module's, and a closed set

- **Decision**: `Emotion` is a five-case union in `world/Dialogue.fs`, beside the kind that uses it —
  the reference's five (`Neutral`, `Happy`, `Sad`, `Angry`, `Surprised`).
- **Rationale**: an expression is the *line's* property, so it belongs with the line. It must be a
  closed set because each character declares one portrait per expression (FR-006): a complete,
  checkable set is what makes "the portrait is derived" safe, and a six-case union breaks every
  match that handles expressions, which is the guarantee we want. It cannot be a string for the same
  reason a speaker is not one.
- **Alternatives considered**: an open set (`string`), which cannot be declared complete and cannot be
  matched exhaustively; a fixed number of expressions the game happens to use, which is what this is.

## Decision 4 — The portrait set is declared on the actor asset, per expression

- **Decision**: an actor's manifest gains a portrait block, one image per expression, and the loaded
  asset carries the five portraits. The `ActorAsset` field is **five named texture fields**, and one
  exhaustive match on `Emotion` in the view picks between them.
- **Rationale**: the reference declares portrait paths on the actor asset's manifest
  (`"portraits": { "neutral": …, "sad": … }` in `actor.json`) and looks one up by switching on the
  emotion (`actor_portrait_path`). This port must put them somewhere, and the asset is the only
  correct place: `cast/Roster.fs` is deliberately asset-free — "nothing here names a portrait, an
  asset or an actor" — and `world/Casting.fs` already binds a cast member to an actor asset, so the
  chain `Character → cast binding → asset → portraits → expression` uses machinery that exists.
  The field is five **named** textures rather than an `Emotion`-keyed lookup because
  `world/ActorAsset.fs` is compiled at `fsharp-game.fsproj:50` and the dialogue module has to sit
  before `world/Systems.fs` (`:65`) so the system can read the state — so the asset cannot name
  `Emotion`. Five named fields is in any case the reference's own shape
  (`Actor_Portrait_Paths`), and the exhaustive match lives in the one view that already knows both.
- **Alternatives considered**: portraits keyed by `Emotion` on the asset, which needs `Emotion`
  defined before `ActorAsset.fs` and so drags a dialogue concept into the asset layer's compile
  order; portraits keyed by a bare ordinal, which is an order coupling with no compile check;
  portraits on `Roster.Character`, which breaks that module's stated contract.

## Decision 5 — The portraits are loaded once, through the existing texture cache

- **Decision**: `Assets.loadActors` reads the portrait block and loads each image through the
  `Loader`'s `Textures` dictionary, which already caches by absolute path. The asset holds the
  loaded textures; nothing reloads.
- **Rationale**: the reference's `set_exposition_portrait_path` unloads the previous texture and
  loads the new one from disk **every time the portrait changes** — a decode per line. FR-010 and the
  feature's edge cases forbid that. The loader already has exactly the cache this needs, and a
  portrait is a texture like any other of the project's images (PNG through SDL_image, with a mip
  chain), so no new loading machinery is warranted.
- **Alternatives considered**: resolving the path per frame and letting a cache miss load (a disk
  read on the frame it first appears); storing *paths* on the asset and keeping a path→texture cache
  in the view (a second cache beside the loader's, for no gain).

## Decision 6 — A character's portrait set must be complete, or the asset fails

- **Decision**: every character that can speak a portrait line declares a portrait for every
  expression, and a manifest with a gap is a load failure. Declaring no portraits is legal. A pair
  that does not resolve at draw time — the character is unbound, or the asset declares none — is an
  expected authoring state: the line is left undrawn and the reason is shown in the authoring panel, never raised.
- **Rationale**: the state can name any expression for any character, so a partial set makes a line
  that silently draws no portrait — the failure mode the spec's edge cases call out, and the one the
  reference has (it logs a warning and draws nothing). Failing loudly at **load** is the constitution's
  second principle and it is what makes SC-008 observable. A **draw-time** miss is different in kind:
  an author legitimately names a character before binding her, so crashing the game would make the
  Cast panel unreachable — the one place the mistake can be fixed. The reference's behaviour (log and
  draw nothing) is therefore the right model for that case, while the load check stays loud.
- **Alternatives considered**: allowing a partial set and drawing no portrait (the reference's
  behaviour: silent, and the reader cannot tell a deliberate omission from a broken asset); a
  per-expression fallback to `Neutral` (invents content).

## Decision 7 — The 2D layer is its own module, and knows nothing about dialogue

- **Decision**: a new `render/Ui.fs` holds the pipeline, the batch, the font and the text engine and
  offers `rect`, `image` and `text` over a texture, a rectangle and a colour. It knows `Gpu`,
  `Texture` and the font. It does not know `Character`, `Emotion`, `Dialogue`, or that dialogue
  exists.
- **Rationale**: FR-011 and the spec's assumptions make it general — the HUD, the interact prompt and
  menus are its future consumers — and a module that knows dialogue cannot serve them without being
  edited. It also keeps the compile order honest: `render/` is compiled before `world/`, so anything
  in `render/Ui.fs` that named `Roster` would not compile at all.
- **Alternatives considered**: putting the whole dialogue panel in `render/` (impossible — it cannot
  see `Roster`, `Casting` or `Assets`); drawing straight from `screen/GameplayScreen.fs` with no
  separate layer (then the *layer* is the screen, and the next consumer edits the screen).

## Decision 8 — Text comes from SDL_ttf's GPU text engine, and its geometry is built once per line

- **Decision**: text is drawn from SDL_ttf. A font is opened once; a `TTF_Text` object is created for
  the current line with its wrap width set to the panel's inner width; the **first** frame the line
  changes, the geometry returned by `TTF_GetGPUTextDrawData` — a linked list of
  `TTF_GPUAtlasDrawSequence { atlas_texture; xy; uv; num_vertices; indices; num_indices }` — is copied
  into the layer's reused vertex and index buffers, and every frame after that only draws it.
- **Rationale**: three verified facts make this the cheapest path by a wide margin.
  `SDL3-CS` 3.4.14.1 — already referenced, with **no package dependencies** — binds SDL_ttf: 323
  `TTF_` entry points including `TTF_CreateGPUTextEngine`, `TTF_GetGPUTextDrawData` and
  `TTF_GPUAtlasDrawSequence`. The native `libSDL3_ttf.so` needs no FreeType (`ldd` shows only
  `libSDL3`, `libm`, `libgcc_s`, `libc`), so vendoring one file is the whole native cost, and
  `fsharp-game.fsproj` already has the pattern (`CopySdl3ImageLibraries`, which vendors
  `libSDL3_image.so` and its `libjbig.so.0`). And the engine returns *laid-out* quads in an atlas, so
  there is no rasteriser, no atlas packer, no kerning and no line breaker to write — the parts that
  make a text renderer a project.
  Building the geometry once per line rather than per frame is a requirement, not an optimisation:
  the draw data is a pointer-linked list into SDL_ttf's own memory, so walking it every frame would
  marshal structs and allocate, which FR-010 forbids.
- **Alternatives considered**: `TTF_RenderText_Blended_Wrapped` to a surface, uploaded as one texture
  per line (less code, but no glyph-level geometry, one texture per line, and a rebuild on every
  colour change); a build-time baked atlas plus our own layout (no native addition, but we own
  wrapping, kerning and metrics, and a generation tool); reusing ImGui's font atlas (needs new
  `cimgui_sdl3.cpp` shim functions and couples the game layer to the editor's toolkit).

## Decision 9 — The font is an asset, its atlas is sampled by our own sampler, and text is tinted by the vertex colour

- **Decision**: one font file under `assets/fonts/` with its licence, opened once at screen init. Its
  size is not fixed: it is **derived from the viewport** — a fraction of the viewport height — and
  re-derived each frame, so the text scales with the panel (itself a fraction of the viewport) and
  follows a window resize. A size change re-rasterises the glyphs and **drops the text slots' cached
  geometry** rather than re-keying it, because the atlas handles inside it are stale too. The layer
  creates its own **clamp-to-edge, linear** sampler for the atlas. Text colour comes from the vertex
  colour the shader multiplies in; `TTF_SetTextColor` is not used.
- **Rationale**: the project ships no font, so one must be added (and the reference's font is
  raylib's default, which we do not have). The atlas is the engine's own texture, so its sampler must
  be ours: `render/Texture.fs`'s `createSampler` is private and `Repeat`, which would bleed one glyph
  into its neighbour. Tinting through the vertex colour works because the GPU engine's atlas is
  `TTF_IMAGE_ALPHA` — white RGB with the coverage in alpha — so one atlas serves every colour and the
  text stays independent of the font's own colour API. `TTF_SetFontSDF` (crisp scaling) is deliberately
  not used yet: the panel's text is rasterised at the one size the viewport asks for, so a bitmap atlas is exact.
- **Alternatives considered**: rendering the text colour into the atlas (a font size and a colour per
  atlas, and no tinting); reusing `Texture.createSampler` (wrong address mode, and `private`).

## Decision 10 — One blended, depth-less pipeline, drawn inside the existing scene pass

- **Decision**: the 2D layer's pipeline enables alpha blending
  (`srcAlpha` / `oneMinusSrcAlpha`) and disables depth testing; it is drawn at the tail of the scene
  pass, after the actors, from `GameplayScreen.renderScene`. No new pass and no change to `Engine.fs`'s
  pass order.
- **Rationale**: blending is the one genuinely new pipeline state — `mesh`, `static` and `line` are
  all opaque ("BlendState stays default: opaque") — and it is required, because a panel with a
  portrait and text must composite over the scene. Drawing inside the existing scene pass is already
  proven: `DebugDraw` creates a depth-test-off variant whose target info sets
  `HasDepthStencilTarget = false`, and the grid draws with it inside the depth-attached scene pass.
  Drawing before the ImGui pass is what keeps the artefact order right: the editor's panels lay over
  the dialogue (`GPULoadOp.Load`), which is what we want while authoring.
- **Alternatives considered**: a separate UI pass after the scene (another pass, another boundary,
  for no visual difference); drawing in the ImGui pass before ImGui's draw data (couples the layer to
  the editor backend and to `ImGuiSdl3`); a depth-tested layer (then the dialogue would be occluded by
  geometry, against FR-011).

## Decision 11 — The panel is content-sized, bottom-anchored, in viewport proportions

- **Decision**: the panel's width is a fraction of the viewport width with proportional margins; its
  height is `max(wrapped text height, portrait size) + insets`, anchored so the panel's bottom edge
  holds a proportional margin; the portrait sits left of the text; the text's wrap width is the
  panel's inner width. Every one of those numbers is a proportion of the viewport, computed by one
  pure function of the viewport size and the text's measured size.
- **Rationale**: FR-004 requires the panel and its text to stay fully on screen across a resize, and
  the reference cannot: `draw_exposition` draws a fixed 172 px (176 with a portrait) panel at a fixed
  24 px offset from the top, full width minus its margins, so a resize shrinks the panel's *content
  area* relative to its text and eventually clips. Sizing the panel to its content removes the
  overflow case by construction — the box is defined by what is in it — and the wrap width bounds the
  line count.
  The placement differs from the reference deliberately and is worth recording: the reference draws a
  banner at the **top** of the screen; this draws at the **bottom**, which is where subtitles and
  dialogue conventionally sit, and where a portrait beside the text does not fight the upper screen
  where faces usually are.
- **Alternatives considered**: the reference's fixed panel (clips on resize); a panel sized to the
  viewport with the text clipped inside it (violates "the whole line is visible" and needs a rule for
  what to do when it does not fit); top placement (the reference's, and a reasonable second choice).

## Decision 12 — The dismiss is a pure rule, and `Input` gains `Space`

- **Decision**: a shown line is cleared by a key press, and the rule is a pure function of *(shown,
  key pressed, a text field focused)* so it is testable headlessly. The rule writes the cleared line
  back **by reference** — `Systems.dismissDialogue` returns `unit` and takes `byref<Dialogue>`, as
  `cameraFollow`/`syncActors`/`playerInput` do — and runs **only while playing**: `GameplayScreen.update`
  calls it inside its `if not state.Editing` block, so in edit mode the author clears a line by
  unticking **Shown** in the panel and SPACE stays the editor's. `core/Input.fs` gains
  `Key.Space` — the union case, its mask bit (21) and its `SDL.Scancode.Space` mapping — because the
  key is not mapped today; the mapped set is Escape, Tab, Backtick, Delete, W/A/S/D/Q/E/F/Z/Y, the
  digits 1–3, Ctrl and Shift, using bits 0–20 of a `uint32`.
- **Rationale**: the reference dismisses with SPACE (`systems.odin`: `if rl.IsKeyPressed(.SPACE) &&
  screen.exposition.show`), which is the right shape — one key, and the cue op waits until the line
  goes away — but it does not gate on text input, and this project's editor has text fields that take
  the same keys. Gating on `ImGui.wantTextInput()` is the established pattern here: `Editor.updateNudge`
  and `Editor.handleShortcuts` both check it before taking a keystroke. Making the rule a pure
  function of three booleans is what lets SC-004's "0 changes without input" and "1 frame to clear" be
  asserted without a window.
- **Alternatives considered**: a mouse click to dismiss (the mouse is already the editor's and the
  camera's, and gating on `wantCaptureMouse` is a second rule to get right); dismissing in
  `Editor.handleShortcuts` (editor-only; the player dismisses too); gating nothing (a space typed into
  the panel's own text field would dismiss the line being authored — the exact bug the edge case
  names).

## Decision 13 — The authoring surface is its own docked panel

- **Decision**: a new `editor/DialoguePanel.fs`, a dock window like `CastPanel`/`ZoneRegistryPanel`,
  added to `Editor.draw`'s panel list and to `Editor.panelNames` for the Ctrl+Tab cycle and to the
  dock layout. It offers a shown checkbox, a kind combo, a speaker combo over the roster, an
  expression combo, and a text field.
- **Rationale**: the Inspector cannot host it — its entire body is inside
  `match state.Editor.Selection`, and the dialogue state is not a selection, so a section there would
  only appear with something selected, which is the wrong lifetime for a preview. A standalone panel
  is the existing shape for a surface that is not a selection (`CastPanel` is a panel precisely
  because a cast slot is not a selectable entity), and it needs **no new ImGui binding**:
  `checkbox`, `beginCombo`/`selectableBool`/`endCombo`, `inputText` and `text` all exist.
  The text field needs a keystroke buffer, which goes in `EditorState` beside `NameBuffer`,
  `PromptBuffer` and `NewZoneName` — every text field in the editor has one there. That is transient
  UI scaffolding, not dialogue state, and it does not contradict FR-009's "records no command and
  stores nothing beyond the state itself", which is about commands and persistence.
  Like every other direct-write surface, it records no command and nothing is persisted, so it joins
  `docs/EDITOR_DESIGN.md`'s D-011 list.
- **Alternatives considered**: a section in the Inspector (needs a selection, wrong lifetime); adding
  the fields to the Debug window (`DebugOverlay.fs`, which is for readouts and has no text field);
  driving the state only from code (then nothing can be demonstrated, since no cue engine exists).

## Decision 14 — Tests cover the two pure parts; the rest is quickstart

- **Decision**: a new `test/DialogueTests.fs` asserts the panel layout (viewport size and measured
  text size → the panel, portrait and text rectangles, and their containment) and the dismiss rule.
  The drawing, the font, the SDL_ttf path, the portrait resolution's result and the editor panel are
  [quickstart.md](./quickstart.md).
- **Rationale**: the layout arithmetic is exactly the part the reference gets wrong on a resize, and
  it is pure — it is viewport and measurement in, rectangles out — so it is worth asserting before it
  is drawn. The dismiss rule is three booleans in, a state out. Everything else needs a device, a font
  file, or a frame, which is the same boundary `036` and `037` drew around their Inspector sections
  and the shader.
- **Alternatives considered**: extending an existing test file (`ActorTests.fs` has nothing to do
  with dialogue; `AssetsTests.fs` scans directories, and the portrait block is authored data that
  could go there — a separate file is clearer for a new module); no headless tests at all, and a
  purely manual quickstart (the layout is the fiddly part; it should not be checked by eye alone).

## No unresolved clarifications

The spec left two things open on purpose, and both are settled here: the authoring surface's host
(Decision 13 — a panel, because the Inspector is selection-driven) and the dismiss control
(Decision 12 — SPACE, gated on text focus, added to `Input`). The spec's one judgement call for
`/speckit-clarify` to challenge — that a gap in a portrait set is a load failure (FR-006) — is
confirmed by Decision 6, with the reasoning recorded there.
