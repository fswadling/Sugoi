# Data Model: Codebase Review Remediation

This feature adds three small pieces of runtime state, one optional asset-manifest field, and a set of internal result-shape changes. It adds no persisted project state and no new public type.

## 1. `world/render/EnvironmentBatch.fs` — dirty static batch

### Fields

```fsharp
type EnvironmentBatch =
    { mutable Instances : InstanceBuffer.InstanceBuffer
      Registry : ResizeArray<LoadedSubmesh>
      Lookup : Dictionary<nativeint, int>
      mutable Counts : int array
      mutable Starts : int array
      mutable Cursor : int array
      ViewProjBytes : byte array
      mutable DrawCalls : int
      mutable InstanceCount : int
      mutable Dirty : bool }          // NEW: the static scene changed since the last gather
```

`create` sets `Dirty = true`, so the first frame gathers.

### Operations

```fsharp
val markDirty : EnvironmentBatch -> unit
val reset : EnvironmentBatch -> unit
val gather : Gpu.Device -> EnvironmentBatch -> SlotMap<Prop> -> SlotMap<Prop> voption -> bool
val upload : Gpu.Device -> EnvironmentBatch -> Gpu.CommandBuffer -> unit
```

- `markDirty` sets `Dirty <- true`. It does not allocate.
- `reset` clears `Registry`, `Lookup`, `Counts`, `Starts`, `Cursor`, `DrawCalls` and `InstanceCount`, and sets `Dirty <- true`. It does not free the instance buffer; `release` does that.
- `gather` returns `false` and does nothing when `Dirty = false`. Otherwise it clears the per-slot counts, walks props/parts twice, ensures the instance buffer, fills the staging array, and sets `Dirty <- false`; it returns `true`.
- `upload` is called by the caller only when `gather` returned `true`.

### Invalidation transitions

| Event | Dirty before | Action | Dirty after |
|---|---|---|---|
| Batch created | — | first gather | `true` → `false` |
| Prop/part moved, rotated or scaled | `false` | mark | `true` |
| Prop/part added | `false` | mark | `true` |
| Prop/part removed | `false` | mark | `true` |
| Environment replaced | `false` | reset | `true` |
| Awaited environment becomes active | `false` | mark | `true` |
| Undo restores a zone's props/parts | `false` | mark | `true` |
| Zone released | any | reset then release buffers | `true`, then unreachable |
| Actor transformed | `false` | unchanged | `false` |
| Camera moved | `false` | unchanged (viewProj is pushed per frame) | `false` |

### Invariants

- `gather` is the only operation that clears `Dirty`.
- `Registry`, `Lookup` and the count arrays describe the current environment only; a reset is mandatory before an environment switch.
- `upload` never runs after a clean gather.
- `render` reads the previous counts when the batch is clean; the instance buffer content is unchanged, so the draw is identical.

## 2. `world/assets/Assets.fs` — name caches

### New fields on `AssetManager`

```fsharp
mutable ActorNamesCache : string list option
mutable MusicNamesCache : string list option
mutable SoundAssetsCache : AudioAsset.AudioAsset list option
```

`create`/`scan` initialises all three to `None`. `release` sets them to `None`.

### Operations

```fsharp
val actorNames : AssetManager -> string list
val musicNames : AssetManager -> string list
val soundAssets : AssetManager -> AudioAsset.AudioAsset list
val invalidateNameCaches : AssetManager -> unit
```

- Each accessor matches its cache: `Some xs` returns `xs`; `None` builds the current sorted list, stores it and returns it.
- `invalidateNameCaches` sets all three to `None`.
- Built lists are immutable, so returning the same list reference is safe for every caller.

### Invariants

- A cache is never built from an empty manager unless the dictionaries are genuinely empty.
- The caches are derived state; no authored or persisted value depends on them.
- The three dictionaries remain the source of truth.

## 3. `world/Project.fs` — failed-write retry

### New module state

```fsharp
let mutable private writeFailed : bool = false
let mutable private retryAccumulator : float = 0.0
let private retrySeconds : float = 0.5
```

### Operations

```fsharp
val persist : Registry -> unit
val persistFrame : float -> Registry -> unit
```

- `persist` is one immediate attempt:
  - no-op when `ReferenceEquals(registry, lastWritten)` and `not writeFailed`;
  - otherwise encode and `writeAtomic`;
  - on success: `lastText <- text`, `lastWritten <- registry`, `writeFailed <- false`, `retryAccumulator <- 0.0`;
  - on failure: `writeFailed <- true` (the failure is already reported by `writeAtomic`); `lastWritten` and `lastText` are unchanged.
- `persistFrame dt registry`:
  - if the registry differs from `lastWritten`, call `persist` immediately (a new edit always tries now);
  - else if `writeFailed`, add `dt` to `retryAccumulator` and call `persist` when it reaches `retrySeconds`, resetting the accumulator; otherwise do nothing.

### Invariants

- `lastWritten` advances only on a successful write.
- `lastText` advances only on a successful write or a successful read.
- A persistent failure performs at most one write attempt and one diagnostic per `retrySeconds`.
- The project file format and version are unchanged.

## 4. `world/assets/Assets.fs` and `ActorAsset.fs` — explicit texture directory

### Manifest shape

```fsharp
[<SymbolicExpansion>]
type private Manifest =
    { Id : string
      Name : string
      Model : string
      Morphs : string
      Portraits : ActorAsset.PortraitPaths
      Textures : string          // NEW: root-relative texture directory; empty means the model directory
      Sway : Sway.SwayChain array }
```

`readManifest`/`scanDir` return `Manifest` (with `Id` defaulted to the file stem and `Name` to `Id`) instead of a six-tuple.

### Pure helper

```fsharp
/// The directory actor textures resolve from: the declared root-relative directory when present,
/// otherwise the model's own directory.
val textureDirectory : modelDirectory: string -> declared: string -> string
```

`loadActorAsset` resolves each material as `Path.Combine(textureDirectory actorDir manifest.Textures, mesh.Material)`.

### Asset data change

`assets/actors/Akane/akane.manifest` gains `[Textures "actors/Akane/akane"]`. No other manifest changes.

### Invariants

- No character name appears in texture-resolution code.
- An absent `Textures` field keeps the model-directory default; existing assets that do not declare one continue to load.
- The manifest remains named-field s-syntax; an absent field parses to `null`/empty.

## 5. `core/SlotMap.fs` — scan and restore semantics

No type change. `IteriAlive` gains a byref read:

```fsharp
let slot = &slots.[i]
if slot.Alive then f (uint32 i) slot.Generation slot.Value
```

No dead-slot payload is copied. `Occupy` writes the free-list link through the span where the compiler permits it; otherwise it writes back a slot whose `Value` was not read.

`Free` delegates to the existing `CheckedSlot` rather than repeating its range/staleness checks.

### Invariants

- The callback still receives the same live slots in the same order.
- `Alive`, `Generation` and `Value` are unchanged by a scan.
- `Get`/`Slots` remain the only in-place access paths.

## 6. `world/Entity.fs` — capsule offset and model matrix

```fsharp
/// The offset from an entity's feet position to its capsule body's centre.
val capsuleOffset : CapsuleCollider -> Vector3

/// Scale * rotation with the translation written directly into the matrix's translation row.
val modelMatrix : Transform -> Matrix4x4
```

`Zone` (spawn and sync), `Systems` (actor sync) and `CueEngine` (move) all use `Entity.capsuleOffset`. `modelMatrix` matches `Actor.localMatrix`'s translation fast path.

### Invariants

- `position + capsuleOffset collider` is the body centre for every capsule body.
- `modelMatrix` is the same matrix as `Scale * Rotation * Translation`, up to floating-point rounding.

## 7. `narrative/` — mention, commit and fact state

### `Rules`

Each derivation must carry the full set of fact kinds it reads, including nested binds:

```fsharp
// Declared alongside the rule (compiler-checked), rather than probed from an empty fact set.
Mentions : FactKind list
```

The three current units declare their full sets. `Evaluate` no longer needs to derive `Mentions`.

### `Facts`

```fsharp
/// Add a fact and its reason; return whether the fact key was absent and the new set.
val add : Fact -> Reason -> Facts.Set -> bool * Facts.Set
```

`contains` remains for callers that need a read-only probe. `dropInferred` rebuilds the map in one traversal.

### `Engine`

`reasonsFor` returns the stored canonical list. If the engine retains a `ParticipantIndex`, `factsOf` reads it; otherwise `factsOf` matches the fact shape directly without allocating a participant list.

### `Derive`

`infer` receives an optional "projected fact set changed" signal. A commit that matches no projection returns after appending history and does not call `dropInferred`/`infer`. Reason-only changes are applied without a derivation pass.

### Invariants (from the narrative constitution)

- Committed history is append-only and unchanged by these edits.
- A derived fact's reasons remain canonical, deduplicated and ordered.
- The same history and rules produce identical output.
- A rule whose nested premise appears later is re-evaluated and attributes its conclusion.

## 8. `state/Command.fs` and `state/Undo.fs` — one history

No type change. The following are removed:

```fsharp
UndoStack.undo
UndoStack.redo
UndoStack.undoCommand     // private
UndoStack.applyCommand    // private
```

`Undo.commit`, `Undo.undoHistory` and `Undo.redoHistory` remain the only production stepping/commit path. `Command.placeOf`/an `isStateLevel` classifier is the single classification of state-level commands.

### Invariants

- One accepted editor edit is one push on `state.Editor.Undo`.
- A mixed-place history steps only through `Undo`.
- No test-only stepper exists in production code.

## 9. Validation rules

- A `Key.Digit n` outside 1..3 raises `invalidArg`; no mask collision is reachable.
- A project write failure is observable and retried, but never recorded as a successful write.
- A hidden panel performs no user-side body work; a visible panel is unchanged.
- A static scene with no relevant change performs no gather or upload.
- An environment switch releases every previous registration exactly once.
- A missing `Textures` field defaults to the model directory; an explicit one is used verbatim.
- A nested `Mentions` entry causes the rule to run in the pass where the premise appears.
- A no-projection commit appends history and performs no derivation.
- A reason read returns the stored order without re-sorting.
