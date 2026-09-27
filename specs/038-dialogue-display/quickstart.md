# Quickstart: Dialogue Display

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- **`glslc`** — `shaders/ui.vert.spv` and `shaders/ui.frag.spv` must be compiled from the new shaders.
- **`native/libSDL3_ttf.so`** vendored into `native/` (from `ppy.sdl3_ttf-cs`'s
  `runtimes/linux-x64/native/`), so `DllImport("SDL3_ttf")` resolves from the app directory.
- **`native/build.sh`** must be re-run once, because `cimgui_sdl3.cpp`'s dock layout gains the new
  panel's slot and `libcimgui.so` embeds it.
- **A font** under `assets/fonts/`, with its licence.
- **One character's portraits** under `assets/actors/<Name>/portraits/`, one image per expression,
  with the `[Portraits …]` block added to that actor's manifest.

## Build & test

```sh
glslc -fshader-stage=vert   shaders/ui.vert -o shaders/ui.vert.spv      # new
glslc -fshader-stage=frag   shaders/ui.frag -o shaders/ui.frag.spv      # new
native/build.sh                                                         # the dock slot
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Automated coverage (headless)

`test/DialogueTests.fs` is new and covers the two pure parts — no GPU, no font, no window:

1. **The panel layout (FR-004, SC-003)** — for a range of viewport sizes the panel, the portrait and
   the text box are inside the viewport and inside the panel; the portrait is only non-empty when the
   kind draws one; a taller measured text grows the panel rather than overflowing it; the panel and
   its contents scale with the viewport, so a resize keeps everything on screen.
2. **The dismiss rule (FR-008, SC-004)** — a shown line clears only when the key is pressed and no
   text field has focus; the key with nothing shown changes nothing; the key while a field has focus
   changes nothing; a frame with no press leaves the line shown.

## Interactive walkthrough (SC-001 … SC-008)

Build, run, press backtick for the editor, and open the **Dialogue** panel.

1. **Nothing shown is invisible (FR-003, FR-012, SC-001)** — with the panel's Shown unticked, the
   scene is exactly what it was before the feature, and the existing suite passes unchanged.
2. **A line appears (FR-001 … FR-003, SC-001)** — tick Shown and type a line: a panel appears over
   the scene carrying that text. Untick it: nothing is drawn.
3. **The speaker's name (FR-002, SC-001)** — set the kind to portrait dialogue and pick a speaker:
   the speaker's name appears in the panel. Set the kind to dialogue or thought: no name.
4. **The portrait follows the expression (FR-005, SC-002)** — first assign the character an actor
   asset in the **Cast** panel (the binding is session state, so it starts empty), then with portrait
   dialogue and that speaker, pick each expression and confirm the portrait changes to that
   character's image for it. Nothing in the panel names an image file. Select the speaker *before*
   binding her and the line is simply not drawn, with the Dialogue panel printing why — the game
   keeps running; bind her and it appears.
5. **The text fits (FR-004, SC-003)** — type a line longer than the panel is wide: it wraps and the
   whole line is visible. Then resize the window: the panel and its text stay fully on screen. A line long enough that it cannot fit the viewport height is left undrawn and the Dialogue panel says so rather than drawn clipped (FR-004).
6. **Thought is presented distinctly (FR-002)** — switch the kind to thought: no name plate, no
   portrait, and the panel inverted (a light panel with a dark border, against the near-black panel of a dialogue line or a portrait line).
7. **The player dismisses it (FR-008, SC-004)** — enter play mode with a line shown and press
   SPACE: the line goes. With nothing shown, SPACE does nothing. With the panel's text field focused,
   SPACE types a space and does **not** dismiss. In edit mode SPACE does nothing at all (the dismiss
   is a play-mode control) — untick Shown in the Dialogue panel to clear a line while authoring.
8. **A gap in a character's portraits fails loudly (FR-006, SC-008)** — remove one expression's line
   from an actor manifest and restart: the app fails at load naming the missing portrait, rather than
   running and drawing a line without one. Removing the whole block instead leaves the actor loading with no portraits, unable to speak a portrait line.
9. **No undo step, nothing persisted (FR-007, FR-009, SC-006)** — move an actor (one scene edit),
   change the dialogue, then Ctrl+Z once: the actor moves back and the dialogue is as you left it.
   Restart: nothing is shown.

## Automated coverage boundaries

- The **layout** and the **dismiss rule** are pure and fully covered headlessly (above).
- The **drawing**, the **font** and the **SDL_ttf text path** need a GPU device and a font file, so
  they are the shader compile plus steps 2, 5 and 6.
- The **portrait resolution's result** needs a loaded asset, so it is steps 4 and 8; the match itself
  is compile-checked (`Emotion` is a closed union, so a sixth expression fails to build until the
  portrait match handles it).
- The **editor panel** needs a frame, so it is every step.
- SC-007 (no per-frame allocation) is structural — the batch is a reused array and the glyph geometry
  is built once per line, never per frame — and is confirmed by inspection plus the existing
  allocation checks still observing zero bytes per frame. **Not verified by automation**: that the
  SDL_ttf draw data is walked only on a line change, since that needs a device.
- SC-001's "identical to the pre-feature frame" half is only observable in the running app: with
  `Shown = false` the view takes an early exit before any font work, and step 1 carries the
  on-screen half.
- **Present mode is vsync** (`native/cimgui_sdl3.cpp`), so a frame that misses a vblank paces to a
  multiple of the refresh period; the layer's cost (about three draw calls) is far below that
  boundary and should not be measurable.

## Expected outcome

- A shown line draws a panel over the scene with the speaker's name when the kind has one, the
  speaker's portrait for the line's expression, and the whole text wrapped inside it; with nothing
  shown the frame is the pre-feature frame.
- The portrait is always the one the *(character, expression)* pair names, and a portrait set with a
  gap fails at load rather than drawing a line without one.
- A shown line stays until the player dismisses it with SPACE, and dismissing never fires while a
  text field has focus; the editor panel sets every field and records no command and no persistence.
- `ui.vert.spv`/`ui.frag.spv` are compiled, `libcimgui.so` is rebuilt, `dotnet build` is clean, and
  `dotnet test` passes.
