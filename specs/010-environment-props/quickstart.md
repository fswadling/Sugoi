# Quickstart: Environment + Props

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).
- Environment and prop model files under `assets/` (glTF + external `.bin`),
  copied from odin-game's `assets/{environments,props}`, referenced by manifests
  via a `Model` field, e.g.:
  - `assets/environments/island.manifest` →
    `[[Id "island"] [Name "Island"] [Model "environments/Island.gltf"] [OrientationDegrees (0 0 0)]]`
  - `assets/props/beech.manifest` →
    `[[Id "beech"] [Name "Beech"] [Model "props/Beech.gltf"]]`
- The compiled static shaders `shaders/static.vert.spv` + `shaders/static.frag.spv`.

## Build & test

```sh
dotnet build
dotnet test   # includes test/GltfTests.fs and the updated test/AssetsTests.fs
```

## Validation scenarios

1. **Environment** — from the Asset Browser, spawn the `island` environment.
   Confirm its static geometry (not a wireframe box) is visible in the viewport,
   at the manifest's orientation.
2. **Depth** — orbit/zoom the camera; the environment is correctly depth-sorted
   against itself (no back-face/front-face draw-order artifacts).
3. **Prop spawn** — spawn a `beech` prop; its mesh renders at the spawn position.
4. **Prop transform** — move/rotate/scale a spawned prop; the mesh follows the
   entity transform.
5. **Multiple instances** — spawn two `beech` props at different positions; both
   render, share one loaded model, and differ only in transform.
6. **Composition** — spawn an environment + a prop + the Akane actor; all three
   render together with correct depth ordering, and the reference grid/gizmos
   remain visible.
7. **Selection/gizmo on props** — select a prop and move it with the transform
   gizmo; the mesh follows, and undo/redo restores the move.
8. **Fail loudly** — point an environment/prop manifest `Model` at a missing or
   non-glTF file and confirm the failure is reported loudly (not a silent empty
   mesh).

## Expected outcome

- The `island` environment is visible as a static, flat/Lambert-lit mesh.
- At least two props of one asset render simultaneously, sharing one loaded model.
- The Akane actor, the reference grid, markers, and gizmos still render in the
  same viewport.
- `dotnet build` is clean; `dotnet test` passes (existing suite + glTF parser +
  asset loading).
