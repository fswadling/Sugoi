# Research: Entity Reference Integrity

## 1. Slot storage: immutable struct vs mutable class

- **Decision**: `SlotMap<'T>` is a `[<Sealed>]` class mutated in place. `Slot` and
  `Handle` remain `[<Struct>]`; the public `Slots` is a `ReadOnlySpan<Slot<'T>>`
  and `Get` returns a `byref<'T>` into the payload.
- **Rationale**: the store is shared — the zone holds it, undo commands reference
  it, and an environment owns one. An immutable struct returning a new map from
  `Add`/`Free` requires every caller to write the result back; a missed write-back
  desyncs the free list from the storage, which is the class of bug this feature
  removes. A class makes the reference the identity of the store, so no copy can
  diverge.
- **Alternatives considered**: immutable struct with `map <- SlotMap.add map …` at
  every call site (rejected: error-prone; zone/environment/undo must share one
  store); a class wrapping an immutable struct (rejected: extra layer, no benefit).
- **Cost**: `Occupy`'s free-list rebuild is O(capacity) but runs only on the undo
  path; reads/writes stay O(1).

## 2. Identity for a reference: generation + epoch

- **Decision**: `EntityRef { Kind; Index; Generation; Epoch }`. Generation is the
  slot's incarnation counter (bumped on free); epoch is the owning environment's
  id (`0` for non-part kinds). Constructors `actorRef`/`propRef`/`partRef` keep
  call sites from forgetting the epoch.
- **Rationale**: an index alone aliases a reused slot (the pre-existing hole). The
  generation closes it. But each environment's parts map starts its generations at
  `0`, so two environments can have a same-index/same-generation part; the epoch
  disambiguates them.
- **Alternatives considered**: one global monotonic id per entity (rejected:
  larger refs, loses slot locality); clearing part refs on swap (rejected: undo
  could then not revive them).
- **Note**: absence is `EntityRef option`; no sentinel value.

## 3. Environment parts: own store, remembered environment value

- **Decision**: `Environment` gains `Epoch` and `Parts : SlotMap<Prop>`; the zone's
  `Props` holds placed props only. Loading builds a fresh environment (new parts
  map, fresh epoch) and installs it via `setEnvironment`; undo/redo re-install the
  whole `Environment` value, deactivating/activating in place so slots and
  generations survive.
- **Rationale**: parts and placed props have opposite lifetimes — a swap must
  discard all parts but preserve placed content. Separate stores make that
  structural rather than a filtering rule. Retaining the parts map across a swap
  keeps recorded part refs resolvable when the environment returns (undo).
- **Alternatives considered**: one store with parts tagged/filtered by environment
  (rejected: a swap must not free placed props, and a stale part ref could still
  alias a placed prop); free and respawn every part on each swap (rejected:
  recorded slots/generations would change, breaking undo).
- **Consequence**: `partsFor zone ref` returns the parts map only when `ref.Epoch`
  matches the loaded environment; otherwise the ref resolves to nothing and a
  restore is reported as an error.

## 4. Undo restoring a deleted entity

- **Decision**: `SlotMap.Occupy(index, generation, value)` re-occupies a free slot
  and restores the recorded generation; it raises if the slot is already alive.
  `Zone.ensureSlotFree` checks before building GPU/physics resources, so a
  conflict fails cheaply. Entity creation is centralised in `editor/Spawning.fs`,
  pairing each `Zone.spawn*` with its reversing command.
- **Rationale**: index-based `setByIndex` silently overwrote whatever occupied the
  slot. With generation-carrying refs and a central creation surface, a recorded
  slot is always free when its undo runs, so `Occupy` can be strict and surface an
  invariant break instead of corrupting the scene.
- **Alternatives considered**: relocate the restored entity to any free slot
  (rejected: the recorded ref would no longer resolve, and it hides the break).
- **Risk**: the "only `Spawning` creates entities" invariant is enforced by
  convention (documented in `docs/EDITOR_DESIGN.md` and `editor/Spawning.fs`); the
  loud raise is the safety net.

## 5. Selection reconciliation on environment swap

- **Decision**: `Selection.dropStalePartSelection` drops a Part selection whose
  epoch no longer resolves. It is called after an environment load and after every
  undo/redo step (`Editor.undoStep`/`redoStep`). Actor/Prop selections are left
  alone.
- **Rationale**: a swap dangles only a Part selection. Actor/Prop refs can
  legitimately resolve again when undo restores their slot, so dropping them would
  be wrong. Routing undo/redo through shared steps means the reconcile cannot be
  missed.
- **Alternatives considered**: clear the selection on any swap (rejected: discards
  a still-valid actor/prop selection); leave the dangling selection for the
  Inspector to ignore (rejected: an invalid selection is a bug, not a display
  state — Fail Loudly prefers clearing it).

## 6. Test isolation for Jolt

- **Decision**: add `test/xunit.runner.json` with `parallelizeTestCollections:
  false`.
- **Rationale**: `Physics.create` calls Jolt's process-global `Foundation.Init`, so
  parallel test collections that create physics worlds are unsafe. The new
  `ZoneTests`/`SpawningTests` drive real bodies through a headless fixture.
- **Alternatives considered**: a shared collection fixture (rejected: more
  machinery than the single setting requires).

## 7. Headless validation

- **Decision**: tests build `Assets.AssetManager` values with a zero `Device` and
  only the fields the collider/render paths read.
- **Rationale**: `EnvironmentRender.create` and the collider path never dereference
  the GPU device, so the real `loadEnvironment`/`setEnvironment`/`removeEntity`
  paths (including Jolt bodies) run in tests with no graphics context.
- **Alternatives considered**: mocking the asset manager/renderer (rejected: would
  not exercise the real slot/body lifecycle, which is where the bugs live).
