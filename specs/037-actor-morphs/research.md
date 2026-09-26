# Research: Actor Morph Targets

Decisions derived from the feature spec and the code as it stands (`shaders/mesh.vert`,
`render/Gpu.fs`, `render/Mesh.fs`, `world/Actor.fs`, `world/ActorAsset.fs`, `world/Assets.fs`,
`world/ActorRender.fs`, `state/EditorState.fs`, `state/Command.fs`, `editor/Inspector.fs`, and
`test/ActorTests.fs`), with `odin-game`'s `src/actor.odin` and `src/assets.odin` as the reference.

## Decision 1 — The offsets are applied in the vertex stage, not on the CPU

- **Decision**: `mesh.vert` reads the morph offsets from a storage buffer and displaces `aPosition`
  (and `aNormal`) by `Σ weight · offset` immediately before the existing bone blend. Nothing morphs on
  the CPU.
- **Rationale**: this port skins on the GPU — `Mesh.create` interleaves the vertices into one 40-byte
  buffer uploaded once, and `mesh.vert` does the skinning from a bone storage buffer. odin can reset and
  re-add CPU vertex arrays because raylib skins from those arrays and re-uploads them; here there is no
  CPU array after load, and adding one would mean a dynamic vertex buffer and a full re-upload — 20 005
  vertices × 40 bytes ≈ 800 KB **per actor per frame** — which is precisely the cost `023` removed from
  this path. The displacement is the same arithmetic in the same order (base, then `+ delta · weight`),
  just at the stage where a vertex exists.
- **Alternatives considered**: a CPU morph pass + dynamic vertex buffer (odin's shape; simple to port,
  and it re-introduces the CPU work and the bandwidth the port deliberately left behind); a compute pass
  writing a morphed vertex buffer (another pipeline, another buffer, more than one morph set needs).

## Decision 2 — One per-asset delta buffer, dense and per-vertex

- **Decision**: `ActorAsset` gains one storage buffer of deltas, `16` bytes per (vertex, morph) pair — a
  `vec4` position and a `vec4` normal — laid out so that morph *m*'s sample for vertex *v* sits at index
  `v * morphCount + m`. The buffer is built once at load and is shared by every actor drawn from the
  asset. A target that supplies no normal offsets contributes a zero normal.
- **Rationale**: the shader indexes by `gl_VertexIndex`, so the samples for one vertex must be
  contiguous; a dense per-vertex layout turns the lookup into `buffer[gl_VertexIndex * morphCount + m]`
  with no indirection and reads as `vec4` pairs rather than scalar floats. Size is
  `vertices × morphCount × 32` bytes — named characters are ~20 k vertices, so under ~5 MB per asset at
  8 morphs, uploaded once. odin's targets are sparse per-mesh ranges because raylib keeps one array per
  mesh; this port has one combined vertex buffer, so the sparse ranges are resolved to absolute vertex
  indices **at load** and the shader sees only the dense result.
- **Alternatives considered**: scalar `float3` samples (three unaligned reads instead of one aligned
  pair); separate position and normal buffers (two bindings and two loops for no gain); a sparse
  per-vertex index list (an indirection per morph per vertex to save memory the budget does not need);
  keeping odin's per-source-mesh ranges in the shader (the shader has no notion of the source mesh).

## Decision 3 — The weights travel in a second pushed vertex uniform

- **Decision**: the per-actor weights are pushed as a second vertex-stage uniform — `vec4 weights[2]`
  (the ≤ 8 weights) and a `vec4 info` whose `x` is the morph count — at set 1, binding 1, from a reused
  48-byte array on `ActorRender`. `Morphs` is bound at vertex storage slot 1 (the bone palette is slot 0).
- **Rationale**: the weights are per-actor and tiny, so a push is the right carrier — it is how `PerDraw`
  already travels — and a second slot leaves `PerDraw`'s layout and `Mesh.draw`'s signature untouched,
  where appending to `PerDraw` would put actor-specific weights into the mesh's scratch. `vec4` pairs
  keep the array 16-byte aligned so the shader indexes `weights[m / 4][m % 4]`; the count lets the loop
  run only over the morphs an asset actually has.
- **Alternatives considered**: appending the weights to `PerDraw` (one push instead of two, but the mesh
  would have to learn about morphs and its scratch would carry actor state); a per-instance weight
  *storage* buffer (a third binding and a per-frame upload for 48 bytes); reading weights from the delta
  buffer (mixing static offsets with per-frame state in one immutable buffer).

## Decision 4 — `MorphsMax` lives in `Gpu.fs`, beside `BonesMax`

- **Decision**: `Gpu.MorphsMax = 8`, mirrored as `MORPHS_MAX` in `mesh.vert`; `ActorRender.create` sizes
  its morph slot map with it, and the Inspector reports the "at maximum" case with it.
- **Rationale**: it is a **pipeline bound** — the length of the uniform array and the loop's ceiling —
  exactly like `BonesMax`, which is why `BonesMax` is in `Gpu.fs` and not in `Actor.fs`. It also has to
  be visible to `render/Mesh.fs` and `render/Gpu.fs`, which are compiled *before* `world/Actor.fs`, so it
  cannot live where `MaxAnimationSlots` does. That asymmetry is deliberate: the animation cap is a CPU
  blend bound, the morph cap is a shader bound.
- **Alternatives considered**: `MaxMorphs` in `Actor.fs` beside `MaxAnimationSlots` (symmetrical, and it
  would force the cap down into modules compiled before it, or duplicate the number); a literal `8` in
  each place (the shader already duplicates `BONES_MAX`, but two or three copies of the morph cap is one
  more than the comment can defend).

## Decision 5 — `MorphSlot` and its algebra live in `Actor.fs`, indexed by int

- **Decision**: `Actor.MorphSlot = { MorphIndex : int; mutable Weight : float32 }` — no `Enabled` flag,
  because a weight of 0 is already "contributes nothing" and a morph has no clock to gate. The pure
  algebra sits beside the animation one: find-or-add a slot for a morph index, clamp its weight, and
  remove a slot by index, taking the asset's morph **count** as an argument for validation.
- **Rationale**: `Actor.fs` is the module whose functions are pure and headless-testable, and the weight
  algebra is exactly that; taking the count (an `int`) rather than the asset keeps `Actor.fs` free of a
  dependency on `ActorAsset.fs` (which is compiled after it) and on the GPU. An `Enabled` flag is
  omitted because it would be a second way to say what weight 0 says — `036` kept it for clips only
  because it gated a playback clock, which morphs do not have.
- **Alternatives considered**: a `MorphSlot` holding the morph *name* (a string per slot, and a lookup
  by name where an index is already the identity); an `Enabled` flag for parity (a redundant state to
  keep consistent); putting the algebra in `ActorRender` (untestable, as `036` established).

## Decision 6 — The sidecar is discovered from the actor manifest, under its own extension

- **Decision**: the actor manifest (`Id`/`Name`/`Model`) gains an optional `Morphs` field naming a
  manifest path; that morph manifest names its own data file. The morph manifest does **not** use the
  `.manifest` extension.
- **Rationale**: `Assets.scanDir` enumerates `*.manifest` recursively under `assets/actors` and reads
  each as an actor manifest, so a morph manifest named that way would be read as an actor with no model
  path and fail the scan. Giving it its own extension and referencing it from the actor's manifest keeps
  the scan untouched and the ownership clear. It also means an actor with no `Morphs` field is simply an
  actor with no morphs, which is every asset today.
- **Alternatives considered**: teaching `scanDir` to skip morph manifests (a special case in the one
  place that should stay uniform); a fixed filename beside the actor (name collision between the two
  sidecars and an implicit convention); putting the morph manifest outside `assets/actors` (no reason to
  scatter an actor's data).

## Decision 7 — The manifest addresses the combined vertex buffer directly

- **Decision**: the morph manifest is `version`, `data` (the data file's name), and `morphs`, each with a
  `name` and `targets`, each target being `first_vertex` (an **absolute** index into the actor's vertex
  array), `vertex_count`, `has_normal_deltas`, `position_offset` and `normal_offset` (offsets in floats
  into the data file). Parsed with `Prime.scvalue` under `[<SymbolicExpansion>]`, as every other manifest
  is.
- **Rationale**: the actor's VBO is the IQM vertex array, which is already the concatenation of its
  meshes' vertex ranges, so an absolute vertex index is both the shader's `gl_VertexIndex` and the index
  `Mesh.create` interleaves at. odin's `mesh_index` exists because raylib keeps a separate array per
  mesh; there is no such array here, so carrying a mesh index would force the loader to reconstruct an
  offset the manifest could have stated. The float offsets keep the one data file's layout the manifest's
  business, exactly as odin's do.
- **Alternatives considered**: `mesh_index` + `source_mesh_name` (odin's fields; here they are only a
  roundabout way to an offset, and the source mesh names are not otherwise in the port); one data file
  per morph (many small files, and the offsets fields become unnecessary).

## Decision 8 — Parse and packing are pure; the loader is a thin IO shell

- **Decision**: `ActorAsset.parseMorphManifest : string -> MorphManifest` and
  `ActorAsset.buildMorphDeltas : MorphManifest -> float32 array -> vertexCount:int -> morphCount:int ->
  float32 array` are pure. `Assets.loadActors` reads the files, calls them, and uploads the result once
  with `Gpu.uploadBufferOnce`.
- **Rationale**: this is the `036` split — the logic lives where it can be driven without a GPU, and the
  IO and the upload live where every other asset's IO lives. It also gives the tests a fixture that is a
  **string and a byte array**, with no filesystem and no device, which is what makes SC-001 and SC-004
  checkable headlessly.
- **Alternatives considered**: parsing in `Assets` (untestable without an asset tree); a separate
  `world/ActorMorph.fs` module (a new file and a `.fsproj` entry for three functions that belong to the
  asset record they build).

## Decision 9 — Malformed morph data fails at load

- **Decision**: an unparsable manifest, a version the loader does not know, a data file whose length is
  not a multiple of four bytes or is too short for the offsets the manifest names, a target whose range
  runs past the vertex count, and an asset declaring more than `MorphsMax` morphs all **raise** at load.
- **Rationale**: a morph sidecar is authored data describing a specific mesh; one that does not match it
  cannot be rendered around, and a silently skipped target would show as a subtly wrong face. `odin`
  logs and continues, which is right for a runtime that cannot stop; this port's loader fails loudly for
  every asset kind already (`no model path`, `no positions`, `exceeding BonesMax`), and morph data joins
  them.
- **Alternatives considered**: skipping bad targets (a shape that is wrong in a way nobody can see);
  clamping offsets (rendering whatever the bytes happen to mean).

## Decision 10 — The Inspector section mirrors the Animations section

- **Decision**: the actor block gains a `Morphs` collapsing header: a row per held morph (name, a
  `dragFloat` weight 0..1 that calls `ActorRender.setMorphWeight`, a `smallButton` that calls
  `ActorRender.removeMorph`), a `smallButton` per morph the asset carries and the actor does not hold
  (joining at weight 0), and the three degenerate states as text. Row widgets are kept distinct with
  `ImGui.pushId`/`popId`. It stores nothing in `EditorState` and records no command.
- **Rationale**: `036`'s section is the shape an author already knows, on the surface that already
  exists, with the same direct-write stance (`docs/EDITOR_DESIGN.md` D-011) and the same consequence
  (runtime state, not persisted, not undone). Reusing it costs no new widget and no new editor state.
- **Alternatives considered**: a second panel (a new docked surface for two controls); the reference
  editor's combo-plus-Add shape (a stored selection index and its re-seed rules, for no behaviour the
  buttons do not already give).

## Decision 11 — Tests extend `ActorTests`; the shader and the section are quickstart

- **Decision**: extend `test/ActorTests.fs` with the manifest parse, the delta packing and the weight
  algebra. Do not add a test file, and do not unit-test the shader or the Inspector section.
- **Rationale**: the pure half is the whole of the logic this feature adds outside the shader, and
  `ActorTests` is already where the actor's pure functions are driven headlessly. The shader needs a GPU
  and the section needs a frame; both are the manual half, as `036` recorded for its section and `035`
  for its panel lines.
- **Alternatives considered**: a `test/ActorMorphTests.fs` (a new file plus a `.fsproj` entry for tests
  that fit beside the actor's existing ones); asserting the GPU buffer's bytes (the packing function's
  output is what the buffer holds, asserted directly).

## Decision 12 — The feature ships a fixture, because no exporter exists

- **Decision**: land one actor asset carrying a morph sidecar (manifest + data) under `assets/actors/`,
  small enough to check in, so the feature has something to render and the tests have a fixture.
- **Rationale**: the port's actor meshes are IQM, which has no shape keys, and its glTF loader serves
  environments and props, so nothing in the repository can currently produce a morph sidecar. The
  reference project's sidecars were produced by an external exporter whose manifest names its glTF
  source; without that tool here, the format and a hand-authored instance of it are what this feature
  can land, and a fixture is also the honest way to prove the loader against real bytes.
- **Alternatives considered**: building an exporter (a separate tool and a separate feature); generating
  the fixture only inside a test (the feature would have nothing to render, and the interactive
  walkthrough would have nothing to select).

## No unresolved clarifications

Every choice is settled by the spec, by the code, or by the inspection recorded above — including the
two that could only be settled by reading the pipeline (`createMeshPipeline`'s declared descriptor
counts, Decision 3) and the asset scan (`scanDir`'s recursive `*.manifest` glob, Decision 6). No
`NEEDS CLARIFICATION` remains for Phase 1.
