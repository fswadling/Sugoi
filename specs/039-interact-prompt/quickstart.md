# Quickstart: Interact Prompt

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- **Nothing else is new.** No shader to compile, no native library to vendor, no `native/build.sh`
  run: the prompt rides the 2D layer and the font that spec 038 already added.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Automated coverage (headless)

`test/PromptTests.fs` is new and covers the one pure part — the layout — with no GPU, no font and no
window (FR-007):

1. **Centred** — for a range of viewport and measured sizes, the text block's horizontal centre is the
   viewport's centre.
2. **On screen** — the block is inside the viewport: its left edge is at or right of 0, its right edge
   at or left of the viewport width, and its bottom edge is above the viewport's bottom and above the
   bottom margin.
3. **Proportional** — the block scales with the viewport, so a resize keeps it on screen.

## Interactive walkthrough (SC-001 … SC-005)

Build, run, press backtick for the editor, and place a trigger (spec 029). In the trigger's Inspector,
tick **Interactable** and give it a **Prompt** — "Open the door".

1. **The prompt appears on entering (FR-001, SC-001)** — enter play mode and walk the player into the
   trigger: "Open the door" appears over the scene, centred near the bottom. Walk out: it goes, within
   a frame.
2. **It reads as game UI (FR-006)** — the prompt is drawn over the scene, not as an ImGui window: it
   does not appear in the dock layout, cannot be moved or closed, and no `Status` debug window appears
   over the game.
3. **It stays on screen (FR-007, SC-003)** — resize the window: the prompt stays centred and fully on
   screen. Give it a long prompt: it wraps and stays on screen rather than running off an edge.
4. **It is legible (FR-008, SC-003)** — stand where the prompt is over bright scene content, then over
   dark: it stays readable in both (the shadow carries it).
5. **Nothing to show, nothing drawn (FR-005, SC-002)** — enter a trigger that is not interactable, and
   one whose prompt is empty: nothing is drawn. With no trigger entered, the prompt draws nothing and
   the frame is otherwise unchanged (the editor's old read-out is gone).
6. **The nearest wins (FR-004, SC-004)** — overlap two interactable triggers with different prompts
   and move between them: only the nearer one's prompt is shown.
7. **The line takes the screen (FR-012)** — with the player inside the trigger, show a dialogue line
   (the Dialogue panel, spec 038): the prompt is suppressed while the line is up, and returns when the
   line is dismissed.
8. **Edit mode is silent (FR-001)** — back in edit mode, walk the (editor) camera into the trigger:
   no prompt is drawn; the trigger's prompt is read in its Inspector.
9. **No per-frame cost (FR-010, SC-005)** — the per-phase frame log is unchanged when standing in a
   trigger, and the existing suite passes unmodified.

## Automated coverage boundaries

- The **layout** is pure and is fully covered headlessly (above).
- The **drawing**, the **font** and the **`textSlots` bump** need a GPU device and a font file, so they
  are steps 1–5.
- The **trigger detection** is spec 029's and already covered by its tests; this feature reads its
  result.
- SC-005's "no per-frame allocation" is structural — the prompt is read, not rebuilt, and measured
  through the layer's cached slot — and is confirmed by inspection plus the per-phase log.
- **Not verified by automation**: that the shadow and the text coalesce into a single draw run (needs a
  device; the run count is visible in the `draws` column of the frame log if it matters).

## Expected outcome

- Entering an interactable trigger draws its authored prompt as one centred, legible line near the
  bottom of the screen, over the scene; leaving removes it within a frame.
- With no interactable trigger entered, or while a dialogue line is shown, nothing is drawn.
- The editor's `Status` debug overlay is gone; no new shader, pipeline, pass or native file was added;
  `dotnet build` is clean and `dotnet test` passes.
