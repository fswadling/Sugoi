# Contract: Remediation Invariants

This feature has no external API. Its contracts are the internal seams the implementation and tests rely on: UI text safety, transfer-buffer lifetime, static-batch freshness, persistence retry, asset-name caching, undo ownership, slot scanning, entity offsets, and narrative attribution. Each section states the guarantee and the evidence that proves it.

## 1. UI text (`render/ImGui.fs`)

```fsharp
[<DllImport("cimgui", EntryPoint = "igTextUnformatted", CallingConvention = CallingConvention.Cdecl)>]
extern void textUnformatted(string text, nativeint textEnd)

val text : string -> unit   // retains the literal-format binding, used only for compile-time literals
```

Guarantees:

- Every dynamic string (asset name, entity name, scenario name, cue text, diagnostic, formatted number) is passed to `textUnformatted` with `textEnd = IntPtr.Zero`.
- No `%` sequence in authored data is interpreted.
- A literal UI label may use `text`; a formatted value uses `textUnformatted (sprintf …)`.
- `setItemTooltip` remains for literal tooltips; a dynamic tooltip must use an unformatted binding.

Evidence: `igTextUnformatted` is exported by `native/libcimgui.so`.

## 2. Per-frame transfer buffers (`render/InstanceBuffer.fs`, `render/DebugDraw.fs`, `render/Ui.fs`, `world/render/ActorRender.fs`)

Guarantees:

- Every transfer buffer rewritten on consecutive frames is mapped with `cycle = true`; this is unconditional for recurring maps.
- One-shot buffers (`Gpu.recordBuffer`, `Gpu.uploadBufferOnce`, `Texture.createFromDecoded`) may keep `cycle = false`.
- A frame never reads a staging region another in-flight frame is overwriting.
- Destination instance buffers keep their existing three-frame retirement.

Evidence: SDL3-CS `MapGPUTransferBuffer(device, transferBuffer, bool cycle)`; SDL_gpu's multi-frame-in-flight model.

## 3. Static batch (`world/render/EnvironmentBatch.fs`, `world/Zone.fs`, `screen/GameplayScreen.fs`)

```fsharp
val markDirty : EnvironmentBatch -> unit
val reset : EnvironmentBatch -> unit
val gather : Gpu.Device -> EnvironmentBatch -> SlotMap<Prop> -> SlotMap<Prop> voption -> bool
```

Guarantees:

- `create` is dirty; the first render gathers and uploads.
- `gather` returns `false` and does no work when clean.
- `gather` clears the dirty flag only on a successful full gather; it returns `true`.
- `upload` runs only when `gather` returned `true`.
- `render` pushes the current view-projection every frame and draws from the existing counts when clean.
- An environment switch calls `reset` before the new parts are attached; no `LoadedSubmesh` from the previous environment remains reachable through `Registry`.
- Every prop/part add, remove, transform, environment activation and zone restore marks the batch dirty.
- An actor transform does not mark the batch.

Evidence: `Zone.gatherStatic` is the only zone-to-batch binding; `GameplayScreen.renderScene` is the only per-frame caller.

## 4. Project persistence (`world/Project.fs`)

```fsharp
val persist : Registry -> unit
val persistFrame : float -> Registry -> unit
```

Guarantees:

- `persist` is one immediate attempt.
- On failure, `lastWritten` and `lastText` do not advance; the failure is reported by `writeAtomic`.
- `persist` retries an unchanged registry only when `writeFailed` is true.
- `persistFrame dt` attempts immediately on a new registry and otherwise retries at most once per 0.5 seconds.
- A later successful write leaves the file equal to the latest library.
- The project format and version are unchanged.

Evidence: `GameplayScreen.fs:95` is the per-frame call site; `writeAtomic` already reports and preserves the last good file.

## 5. Asset names (`world/assets/Assets.fs`)

```fsharp
val actorNames : AssetManager -> string list
val musicNames : AssetManager -> string list
val soundAssets : AssetManager -> AudioAsset.AudioAsset list
val invalidateNameCaches : AssetManager -> unit
```

Guarantees:

- The first call after invalidation enumerates and sorts the current dictionary once.
- Subsequent calls return the same immutable list.
- `scan` and `release` invalidate the caches.
- A newly loaded or removed asset is visible after the next invalidation.
- The dictionaries remain the source of truth.

## 6. Undo ownership (`state/Command.fs`, `state/Undo.fs`, `editor/Selection.fs`)

Guarantees:

- `Undo.commit` is the only apply-and-record path for editor edits.
- `Undo.undoHistory`/`redoHistory` are the only history steppers.
- `UndoStack.undo`/`redo` and their forwarders are removed from production code.
- `Command.placeOf`/`isStateLevel` is the single classifier used by placement, application and inversion.
- Adding a state-level command requires one edit, not three.
- Every accepted selection edit is one push; a no-op edit records nothing.

## 7. Slot scans (`core/SlotMap.fs`)

Guarantees:

- `IteriAlive` visits the same live slots, in index order, with the same generation and payload.
- A dead slot's payload is not copied.
- A scan does not mutate `Alive`, `Generation` or `Value`.
- `Get` remains the in-place access path; `Slots` remains the span access path.
- `Free` uses `CheckedSlot` and raises the same range/staleness errors.

## 8. Entity offset (`world/Entity.fs`)

```fsharp
val capsuleOffset : CapsuleCollider -> Vector3
val modelMatrix : Transform -> Matrix4x4
```

Guarantees:

- `entityPosition + capsuleOffset collider` is the body centre used by spawn, sync and scripted movement.
- `modelMatrix` equals scale × rotation × translation, with the translation written directly.
- No call site re-derives `Vector3(0, HalfHeight + Radius, 0)`.

## 9. Narrative (`narrative/`)

Guarantees:

- A derivation's declared `Mentions` contains every construct bound by its sources, including nested binds.
- A rule whose nested premise appears in a later pass is re-evaluated and attributes its conclusion.
- A commit whose event matches no projection appends history and performs no `dropInferred`/`infer`.
- A reason-only change is applied without a derivation pass.
- `Facts.add` reports novelty and inserts in one logical traversal.
- `Engine.reasonsFor` returns the stored canonical order without re-deduplication or re-sorting.
- The derivation bound is enforced while conclusions are produced, not after materialising all of them.
- `Harness.report` sorts on precomputed text and reuses the history/facts already computed.
- History remains immutable; the output remains deterministic and attributed.

## 10. Test support (`test/TestSupport.fs`, `test/NarrativeTestSupport.fs`)

Guarantees:

- One headless `SharedState` and `GameplayState` fixture.
- One `entity`/`actor`/`prop`/`part` constructor.
- One allocation-measurement helper with a recorded warm-up rule.
- One temp-directory helper that deletes recursively in `finally`.
- No module-level mutable value is shared across test facts.
- Tests assert production accessors and optional results, not parallel implementations or sentinels.
- The cue description used by tests exists once.
- An asynchronous wait uses a bounded deterministic signal, not sleep-polling.

## 11. Asset load rollback (`world/assets/Assets.fs`)

Guarantees:

- An eager actor load that fails after acquiring textures or creating a mesh/storage buffer releases every acquired texture and created GPU resource before reporting and skipping the asset.
- A prop build that fails partway through `buildSync`/`advanceCursor` releases every created static mesh and texture acquired for that cursor before reporting and skipping the asset.
- The rollback reuses the existing cursor `Created`/`Keys` accounting; it does not add a parallel resource ledger.
- A successful load leaves exactly the resources the asset needs; a skipped load leaves none.

## 12. Editor idle work

Guarantees:

- A hidden/collapsed panel executes no body or per-frame setup.
- An unchanged cue value reuses its row model and labels.
- An unchanged asset set returns cached name lists.
- An unchanged active environment performs no pin-set allocation, clear, refill or lock.
- An unchanged camera and selection reuse the built grid.
- A row draw uses `pushId` and static labels; it does not allocate a per-row identifier.

## 13. Excluded behaviour

The following are deliberately not contracted by this feature and must not change without new evidence:

- JIT elision of large-struct copies at call boundaries.
- `bool` P/Invoke marshalling.
- GPU resource release policy on resize/shutdown (verify-only; no change in this feature).
- Sound voice rewind behaviour (verify-only; a required seek is a follow-up feature, not a change here).
- The one-shot actor-mesh upload path.
- `Texture`'s colour-target usage flag.
- `Entity option` allocation in editor-only paths.
- `GameplayState.activeZone` by-value calls.
- Struct narrative premises/reasons.
- `Scenario.Name` being unread by the harness.
- The rule builder's `Return`/`YieldFrom`/`Combine` members.
