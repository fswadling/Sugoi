# Quickstart: Actor Morph Targets

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- **`glslc`** (the shader compiler the project already uses): `shaders/mesh.vert.spv` must be rebuilt
  from the changed `mesh.vert`.
- An actor asset carrying a morph sidecar (`assets/actors/…`, manifest + data) — the fixture this
  feature lands, since nothing in the repository can currently produce one (research Decision 12).

## Build & test

```sh
glslc -fshader-stage=vert shaders/mesh.vert -o shaders/mesh.vert.spv   # recompile the vertex shader
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Automated coverage (headless)

`test/ActorTests.fs` gains tests over strings and byte arrays — no GPU and no filesystem:

1. **Manifest parse (FR-002)** — a well-formed manifest parses into names and targets; an unknown version
   raises; a malformed document raises.
2. **Delta packing (FR-001, SC-001)** — a target's offsets land at `v * morphCount + m` for each vertex in
   its range and every other sample is zero; a morph with no normal deltas packs a zero normal; a target
   running past the vertex count raises, as does a data file too short for the offsets named (FR-003,
   SC-004).
3. **Weight algebra (FR-005, FR-006, FR-007)** — a weight clamps to 0..1; setting a morph absent adds it
   and setting one held adjusts it; `removeMorph` frees; a morph index outside the asset's morphs raises;
   a ninth morph raises (SC-003).
4. **Zero displacement (FR-008)** — with no morphs held, the weight payload the renderer would push is all
   zeros and the count is 0, so nothing is displaced.

## Interactive walkthrough (SC-006, SC-007)

Rebuild the shader, run the app, press backtick, and select an actor whose asset carries morphs.

1. **Section shows (FR-009)** — the Inspector shows a **Morphs** header listing the morphs the actor
   holds (none, on a fresh actor) with a weight for each. Select a prop, waypoint, trigger or the
   environment: no Morphs section appears (US3 scenario 4).
2. **Add (FR-009)** — click a morph's button under the add row: it joins the list at weight 0.0 and the
   actor's shape does **not** change yet.
3. **Weight (FR-004, SC-001)** — drag the weight to 1: the morphed region moves by exactly the morph's
   offsets. Drag to 0.5: the displacement halves. Drag back to 0: the base shape returns.
4. **Several at once (FR-005)** — add a second morph and set both to 1: the two displacements add.
5. **Remove (FR-009)** — remove a morph: its displacement is gone and the actor is left with what the
   remaining morphs give.
6. **Degenerate cases (FR-010, SC-003)** — with an actor whose asset carries no morphs the section reads
   "(asset has no morphs)"; with every morph held, "(all morphs added)"; with eight held, "(maximum 8
   morphs)". None offers an add that would fail.
7. **No undo, no persistence (FR-011, SC-007)** — move the actor (one scene edit), change its morphs,
   then Ctrl+Z once: the actor moves back and its morphs are as you left them. Restart: the actor holds
   no morphs.
8. **Existing actors unchanged (FR-008, SC-002)** — an actor whose asset carries no morphs still plays
   its Idle clip and poses exactly as before, and the animation and skinning suites pass unchanged.

## Automated coverage boundaries

- The parse, the packing and the weight algebra are pure and fully covered headlessly (above).
- The shader, the pipeline's descriptor counts, and the Inspector section need a GPU and a frame, so they
  are covered by the rebuild and the walkthrough, as `036` left its section and `035` its panel.
- SC-005 (deltas uploaded once; no per-frame allocation) is structural — the upload happens in the loader
  and the push writes into a reused array — and is confirmed by inspection plus the existing
  allocation checks still observing zero bytes per frame.
- SC-001's *displacement* half is only observable in the running app, because the displacement is in the
  shader: T009 asserts the packed offsets, and walkthrough step 3 carries the on-screen half. FR-005's
  order-independence is likewise structural — the pushed payload is indexed by morph index — and is
  asserted through that layout rather than by comparing two slot orders.

## Expected outcome

- An actor carrying an asset with morphs renders its base shape displaced by `Σ weight · offset`, with the
  displacement applied before the animation poses it, and up to eight morphs at once.
- An actor with no morph data, or every weight at 0, renders exactly as it did before the feature, and the
  existing suites pass unchanged.
- The actor Inspector has a Morphs section that adds, removes and re-weights morphs live, records no undo
  step, and persists nothing.
- `mesh.vert.spv` is rebuilt, `dotnet build` is clean, and `dotnet test` passes.
