# Quickstart: Animated Character on Screen

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).
- The Akane IQM model under `assets/actors/Akane.iqm`, referenced by
  `assets/actors/akane.manifest` via a `Model` field:
  `[[Id "akane"] [Name "Akane"] [Model "actors/Akane.iqm"]]`.
- The compiled mesh shaders `shaders/mesh.vert.spv` + `shaders/mesh.frag.spv`.

## Build & test

```sh
dotnet build
dotnet test   # includes test/IqmTests.fs and test/ActorTests.fs (54 tests pass)
```

## Validation scenarios

1. **Static mesh** — from the Asset Browser, spawn the Akane actor. Confirm its
   geometry (not a wireframe box) is visible in the viewport, lit so its
   silhouette reads in 3D, at the spawn position.
2. **Transform** — move/rotate/scale the spawned actor; the mesh follows the
   entity transform.
3. **Depth** — orbit/zoom the camera; the mesh is correctly depth-sorted against
   itself (no back-face/front-face draw-order artifacts).
4. **Grid + gizmos coexist** — confirm the reference grid, entity markers, and
   the transform gizmo still render in the same viewport alongside the mesh.
5. **Skinning** — the character is posed by its skeleton, with no vertices stuck
   at the origin or collapsed to a point (a sign the bone palette exceeded the
   4 KiB pushed-uniform cap).
6. **Animation loop** — the `Idle` animation advances and loops continuously; no
   per-frame hitch or re-parse.
7. **Multiple instances** — spawn Akane twice; both animate, share the model, and
   animate independently.
8. **Undo/redo** — spawn Akane, delete it, undo; the restored actor keeps its
   model (still renders as the Akane mesh).
9. **Fail loudly** — point the manifest `Model` at a missing/non-IQM file and
   confirm the failure is reported loudly (not a silent empty mesh).

## Expected outcome

- The Akane actor is visible as a lit, skinned, looping-animated mesh.
- The debug grid, markers, and gizmos remain visible.
- `dotnet build` is clean (2 expected native-pointer warnings).
- `dotnet test` passes (54 tests: IQM parser + pose-sampling + asset manager).
