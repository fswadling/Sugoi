# Quickstart: Environment Parts as Props

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- The multi-node `ArtRoom` environment copied from odin-game under
  `assets/environments/ArtRoom/` (`.gltf` + `.bin`) and an
  `assets/environments/artroom.manifest` referencing it, e.g.
  `[[Id "artroom"] [Name "ArtRoom"] [Model "environments/ArtRoom/ArtRoom.gltf"]]`.
- Existing `island` environment and `beech`/`oak`/`oak2` props (phase 4).

## Build & test

```sh
dotnet build
dotnet test   # GltfTests (parts/local-space), AssetsTests, EntityTests, CommandTests
```

## Validation scenarios

1. **Import a multi-node environment** — from the Asset Browser, spawn `artroom`.
   The outliner shows many props (desks/chairs/windows/doors/blackboards), not a
   single environment mesh.
2. **Authored placement** — each imported part sits at its node's world transform
   (position/rotation/scale), composing correctly under transform-only parent
   nodes.
3. **Independent editing** — select one desk and move it; only that desk moves.
   Undo restores it.
4. **Single-node environment** — spawn `island`; it still renders as one part
   (the environment has one mesh node), correctly scaled.
5. **Placed props unchanged** — spawn a `beech` prop alongside an imported
   environment; it renders as a single entity at its spawn transform.
6. **Replacement** — spawn `island`, then `artroom`; the island's part is removed
   and the art-room parts are spawned. Placed props added before the swap are kept.
7. **Undo/redo environment** — after spawning an environment, undo removes it (the
   previous environment — or none — returns); redo re-applies it.
8. **Environment readout** — with nothing selected, the Inspector shows
   `Environment: <name>` (or `(none)`) above the zone's Lighting, and the Asset Browser
   groups assets into Actors / Environments / Props.
9. **Composition** — import an environment, spawn a prop, and spawn Akane; all
   render together with correct depth, and grid/markers/gizmos remain visible.
10. **Fail loudly** — point an environment manifest `Model` at a missing or
   non-glTF file and confirm a loud failure (not a silent empty scene).

## Expected outcome

- Importing `artroom` spawns its furniture as individual, movable props.
- `island` still renders (as its single part), and `beech`/`oak`/`oak2` props are
  unchanged.
- The Akane actor, reference grid, markers, and gizmos still render.
- `dotnet build` is clean; `dotnet test` passes (existing suite + new part tests).
