# Quickstart: Zone Skydome

Validation guide for `specs/092-zone-skydome`. See [contracts/skydome-shader.md](./contracts/skydome-shader.md)
for the shader interface and [data-model.md](./data-model.md) for the field shapes.

## Prerequisites

- .NET 10 SDK, SDL3 + `cimgui` native libraries, a working SDL_gpu (Vulkan) device.
- `glslc` (from `glslang`) to recompile the shaders when they change.

## Build & test

```sh
dotnet build          # must be warning-free
dotnet test           # the headless suite must be green
```

The new headless tests live in `test/SkydomeTests.fs` (and extensions to `ScenarioForkTests.fs`): the sky
defaults, the staging round-trip, and the `SetSkydome` step — none of which open a window.

## Recompiling the shaders (only when edited)

```sh
glslc shaders/skydome.vert -o shaders/skydome.vert.spv
glslc shaders/skydome.frag -o shaders/skydome.frag.spv
```

The `CopyShaders` target in `fsharp-game.fsproj` copies the `.spv` files to the output directory.

## Manual validation

1. `dotnet run`. The main menu gives way to the gameplay screen.
2. **Default is off.** An unedited zone draws no sky; the scene clears exactly as before this feature
   (SC-001).
3. Click empty space so the Inspector shows the active zone's settings. Toggle **Sky → Enabled** and drag
   the **Zenith** and **Horizon** colour pickers. A gradient appears behind the geometry; the horizon colour
   sits at and below the horizon, the zenith colour overhead. Turning the sun (the Lighting controls) moves
   the sky's sun with it.
4. Orbit/pan and fly through the zone: the sky stays put as an infinitely distant backdrop and never clips
   (US1, acceptance 4).
5. With the sky on, rotate so the sun is below the horizon (light pitch near the horizon): the sun
   disappears and the gradient remains (edge case).
6. Undo each sky edit: one step per committed edit; the sky returns to its previous value (US3).
7. Launch the game with two zones: only the active zone's sky is drawn; switching zones switches the sky.
8. Register the scene as a scenario, clear it, and **Fork** it back: the zone returns with its authored sky
   (SC-002). Fork a scenario naming a present zone with a different sky: the zone is restaged, not
   duplicated (SC-003).

## Expected outcome

A zone authored with a sky renders it behind the scene; an unauthored zone is unchanged; the sky follows
the zone through register/fork; every edit is a single reversible step. The build is warning-free and the
suite is green.
