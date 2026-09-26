# Data Model: Actor Morph Targets

The feature adds two records to the actor's asset, one slot type and one buffer to the renderer, and
keeps the vertex format and the pose frame untouched.

## `MorphSlot` — `world/Actor.fs` (new)

```fsharp
[<Struct>]
type MorphSlot =
    { MorphIndex : int
      mutable Weight : float32 }
```

- `MorphIndex` names a morph in the actor asset's morph set, and is the slot's identity for find-or-add
  and remove (one slot per morph — the spec's assumption, as with one slot per clip).
- `Weight` is mutable, written in place, and clamped to 0..1 when set (FR-005). **There is no `Enabled`
  flag**: a weight of 0 already means "contributes nothing", and unlike an animation clip a morph has no
  clock for a flag to gate (research Decision 5).

## The weight algebra — `world/Actor.fs` (new)

```fsharp
// Find-or-add the slot for a morph and clamp its weight. A morph index outside the asset's morphs
// raises; adding past `Gpu.MorphsMax` raises through `SlotMap.Add`.
val setMorphWeight : slots:SlotMap<MorphSlot> -> morphCount:int -> morphIndex:int -> weight:float32 -> unit

// Free the morph's slot; a no-op when the actor holds none for it.
val removeMorph : slots:SlotMap<MorphSlot> -> morphIndex:int -> unit
```

Pure, int-indexed, and GPU-free: `morphCount` is the asset's morph count, passed as an argument so
`world/Actor.fs` (compiled before `world/ActorAsset.fs`) need not know the asset's types.

## Authored data — `world/ActorAsset.fs` (new)

```fsharp
// The on-disk manifest, parsed with Prime like every other manifest.
[<SymbolicExpansion>]
type MorphManifest =
    { Version : int
      Data : string                       // the data file, relative to the manifest
      Morphs : MorphDefinition array }

[<SymbolicExpansion>]
type MorphDefinition =
    { Name : string
      Targets : MorphTarget array }

[<SymbolicExpansion>]
type MorphTarget =
    { FirstVertex : int                   // absolute index into the actor's combined vertex buffer
      VertexCount : int
      HasNormalDeltas : bool
      PositionOffset : int                // offset in float32s into the data file
      NormalOffset : int }
```

- `FirstVertex` addresses the actor's **combined** vertex buffer (the IQM vertex array), which is both
  the shader's `gl_VertexIndex` and the index `Mesh.create` interleaves at — not odin's per-source-mesh
  index (research Decision 7).
- `Data` is the manifest's own reference to its data file, as odin's `binary` field is.

## Runtime asset data — `world/ActorAsset.fs` (new)

```fsharp
type ActorMorphs =
    { Names : string array        // morph names, in manifest order — the morph index is the array index
      Count : int
      Buffer : Gpu.Buffer }       // the delta storage buffer, uploaded once
```

```fsharp
type ActorAsset =
    { Id : string
      Name : string
      ModelPath : string
      Bounds : Gltf.Bounds
      Mesh : Mesh.Mesh
      Model : Actor.ActorModel
      Morphs : ActorMorphs option }   // NEW: None when the actor manifest names no morphs
```

## Renderer state — `world/ActorRender.fs` (changed)

```fsharp
[<Struct>]
type ActorRender =
    { Asset : ActorAsset.ActorAsset
      AnimationSlots : SlotMap<Actor.AnimationSlot>
      MorphSlots : SlotMap<Actor.MorphSlot>      // NEW: capacity Gpu.MorphsMax
      Pose : Actor.PoseFrame
      MorphWeightBytes : byte array              // NEW: 48 bytes, reused — the pushed weights
      BoneBuffer : Gpu.Buffer
      BoneStorageBuffers : nativeint array
      BoneTransfer : Gpu.TransferBuffer
      BoneBytes : byte array }
```

- `MorphSlots` is created empty — an actor holds no morphs until a caller or the author adds one
  (research Decision 5), which is what makes FR-011's "rebuilding an actor's renderer yields the default
  state" true.
- `MorphWeightBytes` is the reused uniform payload; `draw` fills it and pushes it, so no per-frame
  allocation (FR-012). It is written per actor, so it is the actor's, not the mesh's scratch.

## The delta buffer — layout

> One storage buffer per asset. Sample *m* for vertex *v* sits at index `v * morphCount + m`:

```text
struct MorphSample { vec4 pos; vec4 nrm; };   // 32 bytes: position delta, normal delta

index = gl_VertexIndex * morphCount + m
```

- Size = `vertexCount * morphCount * 32` bytes, built once at load; shared by every actor from the asset
  (an actor's weights are its own, the offsets are not — the spec's edge case).
- A target that supplies no normal offsets contributes a zero `nrm`, so the shader reads one aligned
  `vec4` pair per morph whatever the morph's shape.
- Bound at vertex **storage slot 1** (set 0, binding 1); the bone palette is slot 0. An asset with no
  morphs binds the bone buffer there and pushes a count of 0, so the binding is always valid and the
  loop never runs (research Decision 3).

## The weights — layout of the pushed vertex uniform

```text
set = 1, binding = 1:  uniform MorphWeights {
    vec4 weights[2];   // offset  0 — up to 8 weights, indexed weights[m / 4][m % 4]
    vec4 info;         // offset 32 — info.x = morphCount
}
```

- 48 bytes pushed per actor per frame from `MorphWeightBytes`. Four `vec4`s of 16-byte-aligned storage
  keep the shader's index arithmetic simple.
- Indexing by **morph index**, not by slot order, is what makes the sum order-independent: the shader
  reads `weights[m]` for morph *m* whatever order the actor's slots happen to be in.

## The displacement, as a state function

> For each vertex, the rendered position is the base position plus `Σ (weightᵢ · positionDeltaᵢ)` over
> the held morphs, and the normal likewise from the normal deltas; the result is then posed by the bones.

- **Base is the vertex buffer.** There is no reset step — unlike odin, which mutates an array it must
  restore, the GPU path is stateless, so "base + Σ" is expressed once in the shader rather than as a
  reset and an accumulate.
- **Additive and order-independent.** Offsets add, so weights summing above 1 are meaningful and the
  result does not depend on the order the morphs are held in (spec edge case).
- **Zero everywhere is the base.** With no morphs held, or every weight at 0, the sum is 0 and the
  displacement is nil — the actor renders exactly as it did before the feature (FR-008).
- **Before posing.** The displacement is applied to the vertex the bone blend then transforms (FR-004).

## Validation rules (load time)

- The manifest parses; `Version` is one the loader knows; `Data` names a file that exists (FR-002).
- The data file's length is a multiple of 4 and holds every offset the manifest names (FR-003).
- Every target's `[FirstVertex, FirstVertex + VertexCount)` lies inside the vertex count (FR-003).
- The asset declares at most `Gpu.MorphsMax` morphs (FR-006 — an over-full asset is a broken asset).

## Validation rules (runtime)

- A requested weight clamps to 0..1 (FR-005); a morph already held is adjusted, not duplicated (FR-007);
  a morph index outside the asset's morphs raises; an add past the cap raises (FR-006).
- A morph name the asset does not define raises (FR-007).
- Morph edits are not persisted, not recorded by a command, and not carried in `ActorState` (FR-011);
  the slots hold no GPU resource, so releasing an actor frees nothing for them.
