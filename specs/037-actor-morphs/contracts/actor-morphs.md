# Contract: Actor Morph Targets

Public surface added or changed by this feature. Signatures are illustrative F#.

## `Gpu` — `render/Gpu.fs` (changed)

```fsharp
// NEW, beside BonesMax. The vertex-stage morph bound: the length of the pushed weights array and the
// ceiling of the shader's loop. Mirrored as MORPHS_MAX in shaders/mesh.vert.
val MorphsMax : int = 8

// CHANGED descriptor counts: the vertex shader now declares the bone palette AND the morph deltas as
// storage buffers, and PerDraw AND MorphWeights as uniform buffers.
let vertexShader = loadShader device SDL.GPUShaderStage.Vertex "shaders/mesh.vert.spv" 2u 2u 0u
```

- `loadShader`'s arguments are `uniformBuffers, storageBuffers, samplers`; the vertex shader goes from
  `1u 1u 0u` to `2u 2u 0u`. The fragment shader is unchanged.

## The morph types and the loader — `world/ActorAsset.fs` (changed)

```fsharp
[<SymbolicExpansion>]
type MorphManifest =
    { Version : int; Data : string; Morphs : MorphDefinition array }

[<SymbolicExpansion>]
type MorphDefinition =
    { Name : string; Targets : MorphTarget array }

[<SymbolicExpansion>]
type MorphTarget =
    { FirstVertex : int; VertexCount : int; HasNormalDeltas : bool
      PositionOffset : int; NormalOffset : int }

type ActorMorphs =
    { Names : string array
      Count : int
      Buffer : Gpu.Buffer }

// NEW — pure. Parse a manifest's text; raises on an unknown version or a malformed document.
val parseMorphManifest : text:string -> MorphManifest

// NEW — pure. Resolve the manifest's targets against the data file into the dense per-vertex layout
// (`v * morphCount + m`, a vec4 position and a vec4 normal each); raises on a short data file or a
// target outside the vertex count.
val buildMorphDeltas :
    manifest:MorphManifest -> data:float32 array -> vertexCount:int -> morphCount:int -> float32 array

// CHANGED: gains `Morphs : ActorMorphs option`.
type ActorAsset = { … ; Morphs : ActorMorphs option }
```

## The load — `world/Assets.fs` (changed)

```fsharp
// `loadActors` reads an actor's `Morphs` sidecar when the actor manifest names one: reads the morph
// manifest and its data file, calls the two pure functions above, uploads the result once with
// `Gpu.uploadBufferOnce`, and stores `Some { Names; Count; Buffer }`. No sidecar → `None`. Every
// malformed case raises here (research Decision 9).
```

- The actor manifest type gains the field

  ```fsharp
  [<SymbolicExpansion>]
  type private Manifest =
      { Id : string; Name : string; Model : string
        Morphs : string }   // NEW: optional morph manifest path, relative to the assets root
  ```

  and `readManifest` passes it through. The morph manifest uses a distinct extension — **`.morphs`** — so `scanDir`'s `*.manifest` glob does not read it as an actor; the manifest names its own data file in `Data` (conventionally `.bin`). Note also that the spec calls the values **offsets** while this contract, the plan and odin call them **deltas** — the same per-vertex values, named once here so the two words cannot read as two things.

## `Actor` — `world/Actor.fs` (changed)

```fsharp
[<Struct>]
type MorphSlot = { MorphIndex : int; mutable Weight : float32 }

// NEW — find-or-add the slot for a morph, clamped to 0..1. Raises on an out-of-range morph index, and
// through `SlotMap.Add` when the set is full (FR-005, FR-006, FR-007).
val setMorphWeight : slots:SlotMap<MorphSlot> -> morphCount:int -> morphIndex:int -> weight:float32 -> unit

// NEW — free the morph's slot; a no-op when absent.
val removeMorph : slots:SlotMap<MorphSlot> -> morphIndex:int -> unit
```

## `ActorRender` — `world/ActorRender.fs` (changed)

```fsharp
[<Struct>]
type ActorRender =
    { Asset : ActorAsset.ActorAsset
      AnimationSlots : SlotMap<Actor.AnimationSlot>
      MorphSlots : SlotMap<Actor.MorphSlot>    // NEW, capacity Gpu.MorphsMax
      Pose : Actor.PoseFrame
      MorphWeightBytes : byte array            // NEW, 48 bytes, reused
      … }

// NEW — resolve a morph name (raising when the asset carries no such morph, FR-007) and set its weight
// through `Actor.setMorphWeight`. One path, as `setClipWeight` is for the animation slots.
val setMorphWeight : r:byref<ActorRender> -> name:string -> weight:float32 -> unit

// NEW — resolve a morph name and remove its slot.
val removeMorph : r:byref<ActorRender> -> name:string -> unit

// CHANGED body, same signature. `draw` now, before delegating to `Mesh.draw`:
//   1. bind the asset's delta buffer at vertex storage slot 1 (`r.Asset.Morphs`), or the bone buffer
//      when the asset carries none — binding 1 is always satisfied and never read at count 0;
//   2. write the ≤ MorphsMax weights into `MorphWeightBytes` by morph index (0 for unheld morphs) plus
//      the morph count in `info.x`, and push it at vertex uniform slot 1.
val draw : r:ActorRender -> renderPass:… -> … -> unit
```

- `create` also builds `SlotMap<Actor.MorphSlot>(uint Gpu.MorphsMax)` and the 48-byte array;
  `upload`/`advanceInPlace`/`release` are unchanged (morphs have no clock and no GPU resource to free
  per actor).

## The shader — `shaders/mesh.vert` (changed, recompiled to `.spv`)

```glsl
const int MORPHS_MAX = 8;

layout(set = 0, binding = 1) readonly buffer Morphs {
    // struct MorphSample { vec4 pos; vec4 nrm; }
    vec4 morphData[];            // 2 vec4 per (vertex, morph); index = (gl_VertexIndex * morphCount + m) * 2
} morph;

layout(set = 1, binding = 1) uniform MorphWeights {
    vec4 weights[2];
    vec4 info;                   // info.x = morphCount
} morphWeights;

// in main(), before the bone blend:
vec3 position = aPosition;
vec3 normal = aNormal;
int morphCount = int(morphWeights.info.x);
for (int m = 0; m < morphCount; ++m) {
    int base = (gl_VertexIndex * morphCount + m) * 2;
    float w = morphWeights.weights[m / 4][m % 4];
    position += morph.morphData[base].xyz * w;
    normal += morph.morphData[base + 1].xyz * w;
}
// then skin `position`/`normal` exactly as `aPosition`/`aNormal` were.
```

- The vertex input layout (40-byte stride, locations 0..4) is unchanged; the fragment shader is
  unchanged. Recompile with `glslc`; the `.fsproj` `CopyShaders` target already copies `mesh.vert.spv`.

## The Inspector section — `editor/Inspector.fs` (changed)

A `Morphs` collapsing header inside the existing `if ref.Kind = EntityKind.Actor` block, beside the
Animations section, with the same shape:

```fsharp
// a row per held morph: name, `dragFloat` weight 0..1 -> ActorRender.setMorphWeight, `smallButton`
// Remove -> ActorRender.removeMorph, row ids pushed by morph name;
// a `smallButton` per morph the asset carries and the actor does not hold -> `setMorphWeight … 0.0f`;
// the three degenerate states as text: "(asset has no morphs)", "(all morphs added)",
// "(maximum 8 morphs)" — and "(holds no morphs)" when the actor holds none.
```

- Reaches the renderer as the Animations section does, and stores nothing in `EditorState`. No new
  widget and no new binding, so `native/` and `render/ImGui.fs` are untouched.

## Not changed

- `render/Mesh.fs` — the vertex layout, the scratch, and `draw`'s signature are untouched; the morph
  binding and the weight push are the renderer's, before it delegates.
- `world/Entity.fs` — no morph field on `ActorState` or `Actor`; the slots are renderer runtime state.
- `state/Command.fs` and `state/EditorState.fs` — no command and no field (FR-011).
- `world/Systems.fs` — no morph logic; a caller reaches `ActorRender.setMorphWeight` directly.
- The fragment shader, the other shaders, the vertex format and the pose frame.
- `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` — no file is added or moved.
