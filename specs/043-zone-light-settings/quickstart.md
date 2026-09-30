# Quickstart: Zone Light Settings

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- **Nothing else is new.** No package, no native library, no shader (`glslc` is not needed — no
  `.spv` is recompiled), no render target. `igColorEdit3` is already in the vendored
  `libcimgui.so`.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

A clean `dotnet build` and a green suite (the existing assertions unmodified) are SC-006.

## Automated coverage (headless)

New `test/LightTests.fs` covers the one pure derivation (research D10):

1. **The convention** — `toLight` maps `(pitch, yaw)` to the reference's direction: `(0, 0)` →
   `(0, 0, 1)`; `(90, 0)` → `(0, 1, 0)`; `(0, 90)` → `(1, 0, 0)`; the result is unit length for a
   spread of angles.
2. **The default reproduces the replaced constant** — `toLight defaultLight` equals
   `{ Direction = normalize(0.4, 0.8, 0.4); Color = (1,1,1); Ambient = (0.15,0.15,0.15) }` to float
   precision (FR-004, SC-002).
3. **Ambient expands neutrally** — a scalar `a` yields `(a, a, a)` (research D5).

`test/ZoneTests.fs` gains a case that `captureTemplate` records the zone's light and a case that
`Command.apply`/`Command.undo` of `SetLight` set and restore it; `test/SpawningTests.fs` gains a case
that `instantiateZone` sets the zone's light to the recorded one and that **undo** restores the
previous light (in the same step as the environment, FR-007, SC-004).

## Interactive walkthrough (SC-001 … SC-006)

Build and run; in the editor, load the ArtRoom and spawn an actor.

1. **The controls exist and drive the scene (FR-001, FR-002, FR-003, SC-001)** — select the zone in
   the Outliner's Zones list; the Inspector's "Lighting" section then shows Sun Pitch, Sun Yaw,
   Light Colour and Ambient. Drag the pitch and yaw: the shading of the environment, the props
   **and** the actor changes as you drag. Drag the ambient and pick a colour: the shading follows.
   Select an entity instead and the lighting controls are gone (the two subjects are exclusive).
   Release a control after changing it: one Ctrl+Z restores the previous light (FR-006, SC-007).
2. **The default is unchanged (FR-004, SC-002)** — before touching anything, the scene looks the
   same as before this feature (a white sun from upper-front-right, a dim grey ambient).
3. **The light is the zone's (FR-005, SC-003)** — give "main" one light, add a second zone, give it
   a different one, and switch between them: each switch brings the new zone into the Inspector and
   the scene lights under each zone's own light; a light changed in one zone is unchanged in the
   other. Change a zone's light with no environment loaded: the controls still work and the change
   still applies.
4. **The face light is untouched (FR-010, SC-005)** — spawn Akane and turn her while dragging the
   sun yaw: her cel bands keep a fixed direction (the idealised face light of spec 040); only their
   colour follows the sun. The outline (spec 041) is unchanged.
5. **A registered place keeps its light (FR-007, SC-004)** — set a distinctive light, Register the
   active zone against a place, change the light away, then Instantiate that place: the zone's
   light returns to the recorded one, together with the environment and props, in one step. Press
   Ctrl+Z once: the environment, props **and** light all revert to what they were before the
   instantiation.
6. **No regression (FR-011, FR-012, FR-013, FR-014, SC-006)** — environments, props, actors,
   portraits, the UI and the other editor panels are otherwise unchanged; `dotnet build` is clean
   and `dotnet test` passes with the existing assertions unmodified.

## Automated coverage boundaries

- **The derivation and the defaults** are pure and fully covered headlessly (above).
- **The recorded light** is pure data and is covered through the existing capture/instantiate
  harness.
- The **rendered result**, the **Inspector's committed edit**, the **colour picker** and the
  **face light's immunity** need a GPU device and a window, so they are steps 1–4. Every prior
  rendering spec follows the same split.
- **Not verified by automation**: that the frame adds no allocation (structural — one small struct
  built from the zone, and a reused editor buffer; the per-frame draw log is otherwise unchanged),
  and the exact look the authored settings produce (a person's judgement).

## Expected outcome

- Each zone owns a sun pitch, sun yaw, colour and ambient, authored from the Inspector (with the
  zone selected) and applied to the scene's one sun for static geometry and actors alike; each
  committed change is one undo step, and an unedited zone renders exactly as before.
- A registered place carries its light, and instantiating it restores the light with the
  environment and props in one undoable step.
- One authored record, one pure derivation, a `mutable` field on two records, one command variant,
  one new ImGui binding and one reused editor buffer were added; no shader, no new pass, no
  dependency.
- `dotnet build` is clean and `dotnet test` passes.
