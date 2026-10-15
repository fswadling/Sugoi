# Quickstart: Editor-Only Reference Grid

Validation guide for `specs/093-editor-only-grid`. See [data-model.md](./data-model.md) for the
accumulator/gate shapes and [research.md](./research.md) for why the gate sits where it does.

## Prerequisites

- .NET 10 SDK and the native SDL3 / `cimgui` libraries (as for any run of the game).

## Build & test

```sh
dotnet build          # must be warning-free
dotnet test           # the headless suite must be green
```

The new headless assertions live in `test/ViewportTests.fs`: the grid accumulator is non-empty while
editing, empty while playing, and empty on the play-transition frame — none of which opens a window.

## Manual validation

1. `dotnet run`. From the main menu, enter the gameplay screen. The editor is active by default
   (`GameplayState.create` sets `Editing = true`), so the reference grid is drawn (US1 acceptance 1).
2. Press **backtick** to enter play mode. The grid is **gone** — the scene shows against its sky and
   clear colour with no lattice or origin axis lines (US1 acceptance 2, FR-001, and the Clarifications'
   "axes hide too"). Nothing of the previous editing frame's grid remains on the switch frame
   (FR-002, D4).
3. Walk/orbit around in play: no grid lines appear at any camera angle, and no geometry is missing or
   changed (SC-003).
4. Press **backtick** again to return to the editor. The grid is **back**, with the same colours,
   extent and orientation as before (US1 acceptance 3, US2, FR-003/FR-004).
5. Repeat the toggle a few times to confirm the grid never lingers into a play frame and never fails to
   return (SC-001/SC-002).

## Expected outcome

The reference grid draws only while the editor is active; play-mode frames contain none of it and are
otherwise identical to today's frames. Its appearance in the editor is unchanged. The build is
warning-free and the suite is green.
