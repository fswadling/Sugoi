# Quickstart: Screen Fade

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- `g++` — only if the native ImGui library is rebuilt (the dock layout changed); it is already present in
  this environment and `native/build.sh` rebuilds `native/libcimgui.so`.
- The headless tests need **no window, no GPU, no device**: `fadeOverlay` is pure.

## Automated coverage (headless)

`dotnet test` runs `test/FadeTests.fs`:

| Story / requirement | Assertion |
|---|---|
| US1, FR-001 — the default | `GameplayState.create`'s `Fade` is `0.0f`; setting it and reading it back holds the value. |
| US1, FR-003 — no-op at 0 | `fadeOverlay w h 0.0f` is `ValueNone` (the frame is unchanged). |
| US1, FR-003 — full black at 1 | `fadeOverlay w h 1.0f` is a black quad at alpha `1.0f`. |
| US1, FR-002/FR-003 — the range | for a spread of sizes and intermediate levels, the overlay spans `(0, 0, w, h)` and its alpha equals the level. |
| FR-006/SC-004 — build/test | `dotnet build` clean, `dotnet test` green, no device. |

## Interactive walkthrough

Run the app (`dotnet run`), enter the editor's gameplay screen, and use the Misc panel:

1. Dock: the **Miscellaneous** tab is in the right column, beside **Inspector**, **Cue** and **Advent**.
   `Ctrl+Tab` cycles to it.
2. Tune: drag the **Fade** slider from 0 upward. The 3D scene and any shown dialogue/prompt darken
   gradually; at 1 the game view is solid black. Drag back to 0 and the view is exactly as before the
   feature.
3. Panels stay usable: at every level the docked panels remain fully readable, so the slider can be dragged
   while watching the fade.
4. Screen-scoped: switch zones from the Outliner while the level is raised — the new zone is darkened to the
   same degree (the level is the screen's, not the zone's).

Then the properties that are about *shape*:

- **No-op at 0** — with the slider at 0 the frame is indistinguishable from a build without the feature.
- **Coverage** — a shown dialogue line and a shown interact prompt are darkened by the overlay too.
- **Editor above** — the panels never darken, at any level.
- **Main menu** — leaving for the main menu shows an unfaded menu (the overlay is a gameplay-screen effect).

## Expected outcome

- `dotnet test` is green, including the default and the pure overlay rule.
- In the editor, the Miscellaneous tab docks on the right and its Fade slider darkens the gameplay view from
  untouched to fully black and back, with the panels readable throughout.
- A zone switch does not change the level, and the main menu is unaffected.
