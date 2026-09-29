# Quickstart: Multisample Anti-Aliasing

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- **Nothing else is new.** No package, no native library, no shader change, no new authored file. This is
  a GPU pipeline/target change only.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

No shader recompile: `shaders/*` are untouched (MSAA changes target and pipeline state, not shader code).
The suite must pass **unmodified** (FR-012) — there is no new pure logic to test (research D5).

## Automated coverage (headless)

**None new.** MSAA is entirely GPU pipeline/target state, which the test project cannot exercise (it
cannot open a GPU device). The existing 319 tests must stay green; nothing is added.

## Interactive walkthrough (SC-001 … SC-006)

Build and run; spawn Akane beside the ArtRoom environment, with the editor overlay available.

1. **The outline is smooth (FR-001, SC-001)** — look at Akane's outline: both its **inner** and **outer**
   boundaries are now smooth gradients, not a single-pixel staircase. This is the artefact that motivated
   the feature (spec Origin).
2. **Overlapping-surface edges are smooth (FR-004, SC-002)** — turn Akane so a limb crosses the torso (and
   so hair overlaps the body): the nearer surface's edge against the further one is anti-aliased, not
   stepped.
3. **Cutouts stay clean (FR-005, SC-003)** — the hair/eye alpha-cutout edges are anti-aliased and the
   visible cut shape is unchanged; no new cel or outline artefact appears around them.
4. **The overlays are unaffected (FR-007, SC-004)** — open the editor panel and look at the game's 2D UI:
   their text and lines are as crisp as before — not blurred and not re-resolved.
5. **The rest of the frame is unchanged (FR-011, SC-002)** — the cel bands, the face light, the outline
   colour and width, the environment and props, and the portraits all look the same as before, apart from
   the smoothing itself. The outline does not bleed or thin (the depth bias is untouched, FR-006).
6. **Resize and cost (FR-008, SC-005)** — resize the window (including a DPI/scale change): the frame
   stays correct, and the frame's draw log shows no per-frame allocation; the targets are recreated only
   on the resize.
7. **No regression (FR-012, FR-013, SC-006)** — the main menu still clears and presents correctly;
   `dotnet build` is clean and `dotnet test` passes with the existing assertions unmodified.

## Automated coverage boundaries

- **Everything here is GPU state** — the targets, the sample counts, the resolve and the fail-loudly check
  need a GPU device and a window, so they are steps 1–7. Every prior rendering spec follows the same
  split.
- **Not verified by automation**: the exact smoothness (it is qualitative), and that the change adds no
  per-frame allocation (structural — the targets are created once and the frame's byte arrays are reused).
- **Fail loudly (FR-009)** is the one path not reachable on a normal desktop: 4× is near-universal, so the
  error branch is exercised only on a device that reports no 4× support for the swapchain colour or depth
  format.

## Expected outcome

- The 3D scene renders through a 4× multisampled colour target and multisampled depth target, resolved
  into the swapchain each frame; every scene-pass pipeline shares the sample count.
- The actor outline (spec 041) and every geometry silhouette in the frame have smooth edges; the hair/eye
  cutout edges are smooth and unchanged in shape; the editor overlay and the 2D UI are as crisp as before.
- `dotnet build` is clean and `dotnet test` passes with the existing assertions unmodified; no shader,
  screen or world module changed.
