# Phase 0 Research: Codebase Review Remediation

This feature resolves the review's verified unknowns before design. Each decision records what was checked, why the chosen approach is the smallest correct one, and what was rejected. Findings are referenced by their register ID from `spec.md`.

## Decision 1 — Use `igTextUnformatted` for every dynamic UI string (F-01)

**Checked**: `nm -D native/libcimgui.so` lists `igTextUnformatted` (and `igTextV`) beside the variadic `igText`. The project's binding already documents that `igText` is variadic (`render/ImGui.fs:181-183`).

**Decision**: add

```fsharp
[<DllImport("cimgui", EntryPoint = "igTextUnformatted", CallingConvention = CallingConvention.Cdecl)>]
extern void textUnformatted(string text, nativeint textEnd)
```

and call it with `IntPtr.Zero` for `text_end` wherever the string is dynamic (asset names, entity names, scenario names, cue text, diagnostics, formatted values). Keep `igText` only for compile-time literals. `setItemTooltip` has one call site with a literal, so it needs no change; if a dynamic tooltip is added later, bind `igSetItemTooltipV` or use a literal format with an argument.

**Rationale**: `igText` is `void igText(const char* fmt, ...)`. `sprintf` substitutes once and returns a string, which `igText` then interprets again; an authored `%s` is therefore undefined behaviour. `igTextUnformatted` has no format interpretation. The symbol is present, so no native rebuild is needed.

**Alternatives considered**: escaping `%` before every call (easy to miss and still format-sensitive); binding `igTextV` and constructing a varargs list (more machinery, same result); treating every call as a literal (wrong for dynamic text).

## Decision 2 — Cycle recurring transfer-buffer maps, not one-shot maps (F-02)

**Checked**: the SDL3-CS 3.4.14.1 XML documentation declares
`SDL_MapGPUTransferBuffer(device, transfer_buffer, bool cycle)` and the project already retires destination instance buffers for three frames (`render/InstanceBuffer.fs:21-23`), matching SDL_gpu's frames-in-flight model. The recurring maps are `InstanceBuffer.upload`, `DebugDraw.upload`, both `Ui.upload` maps and `ActorRender.upload`; the one-shot maps are `Gpu.recordBuffer`, `Gpu.uploadBufferOnce`, `Texture.createFromDecoded`.

**Decision**: pass `cycle = true` for every buffer rewritten on consecutive frames; keep `false` for one-shot uploads. Add a one-line comment at each recurring call stating why.

**Rationale**: with multiple frames in flight, the same staging buffer is still bound by an earlier frame's copy pass when the next frame maps it. `cycle` is SDL_gpu's mechanism for exactly this reuse. The destination-buffer retirement already acknowledges the same lifetime; the source buffer must match it.

**Alternatives considered**: allocate a staging buffer per frame (more memory and allocation); wait for the device each frame (stalls the CPU/GPU pipeline); keep `false` and rely on driver behaviour (not a contract).

**Verification**: confirm the binding's `cycle` contract before applying it. The change is unconditional for recurring maps: pass `true` for every per-frame staging buffer. If the contract shows `cycle = false` is also safe, that is recorded as context, not as an opt-out.

## Decision 3 — A batch-owned dirty flag with explicit invalidation (F-05, F-11, F-30)

**Decision**: `EnvironmentBatch` gains `mutable Dirty : bool` (initially `true`) and a `reset` operation that clears `Registry`, `Lookup`, `Counts`, `Starts`, `Cursor`, `DrawCalls` and `InstanceCount`. `gather` returns `bool` and no-ops when clean; `upload` is called only when `gather` reports a change. Invalidation sites:

- `Zone.setMeshTransform` (prop/part move, rotate or scale);
- `Zone.removeProp` / `Zone.removePart` (through `removeMeshEntity`);
- `Zone.spawnPropBinding` / `Zone.spawnPartBinding`;
- `Zone.setEnvironment` (outgoing parts removed, incoming activated or awaited);
- `Zone.activateEnvironment` / `Zone.reconcileEnvironment` (an awaited environment becomes active);
- `Zone.occupyZone` (undo restores props/parts);
- `Zone.release` (reset the batch before freeing its buffers);
- any future asset settle that adds or removes an environment part.

The actor path does not invalidate the static batch: actors are drawn separately and do not contribute instances.

**Rationale**: the two-pass gather (count, then fill) is required for offsets, and the view-projection uniform is pushed every frame in `render`, so camera movement remains correct while the instance buffer is reused. Resetting the registry on environment change both drops the evicted `LoadedSubmesh` references and removes the pointer-reuse alias risk. Keeping the dirty flag on the batch avoids a second source of truth on `Zone`.

**Alternatives considered**: a generation counter compared against a global frame counter (more state, no benefit); hashing prop transforms every frame (more work than the gather); rebuilding only the changed submesh (complex and not justified by the measured cost).

## Decision 4 — Guard panel bodies on `beginWindow` (F-12)

**Decision**: every `do ImGui.beginWindow(...) |> ignore` becomes

```fsharp
let visible = ImGui.beginWindow("Panel", IntPtr.Zero, 0)
if visible then
    // body and per-frame setup
do ImGui.endWindow()
```

Move the pre-window list building in `SongPanel` and `SoundPanel` inside the guard. Apply the same shape to `DebugOverlay` and `MainMenuScreen` for consistency, even though those windows are normally visible.

**Rationale**: ImGui returns false for a hidden dock tab or a collapsed window. The current code still runs the panel's user-side work (sorts, list building, string formatting), which is the cost the review measured. The window must still be ended.

**Alternatives considered**: a shared `withWindow` helper (the bodies differ and the helper would need a closure, adding an allocation); relying on `ImGui`'s internal `SkipItems` (it skips widget submission, not user-side work).

## Decision 5 — Cache asset name lists on the manager (F-13)

**Decision**: add three lazy caches to `AssetManager`:

```fsharp
mutable ActorNamesCache : string list option
mutable MusicNamesCache : string list option
mutable SoundAssetsCache : AudioAsset.AudioAsset list option
```

`actorNames`, `musicNames` and `soundAssets` return the cached list, building and sorting it once on first use. Invalidate by setting the three to `None` at the end of `scan` (after all dictionaries are populated) and in `release`. If a future load changes the sets, it calls the same invalidation.

**Rationale**: the dictionaries change only at scan/settle/release, while the panels ask every frame. Returning an immutable F# list keeps every call site unchanged and avoids the `Seq.sort`/`List.ofSeq` churn. Caching as a list (not an array) means no conversion at the call sites.

**Alternatives considered**: sorting in the panels (per-frame and spread across callers); caching in `EditorState` (not reachable from all callers, and duplicated per surface); returning arrays (would force `List.ofArray` at several sites).

## Decision 6 — Bounded persistence retry without recording a failure (F-03)

**Decision**: keep `Project.persist (registry)` as one immediate attempt, and add

```fsharp
val persistFrame : dt: float -> registry: Registry -> unit
```

`persist` updates `lastWritten` and `lastText` only when `writeAtomic` succeeds, and records a module-level `writeFailed` flag. `persistFrame` calls `persist` immediately when the registry changed, or when a retry is due after a failure; retries are bounded to one per 0.5 s. `GameplayScreen.update` calls `persistFrame frame.Dt state.Scenarios`. `writeAtomic` already reports the failure, so the bounded rate also bounds the report rate.

**Rationale**: `persist` currently runs every frame from `GameplayScreen.fs:95`. Retrying every frame after a failure would issue one file write and one diagnostic per frame; never retrying loses the edit. The 0.5 s gate is the same order as the existing 0.25 s external-poll interval and is inaudible to an author.

**Alternatives considered**: change `persist` to take `dt` (breaks the existing tests and any explicit-save caller); retry only on the next library change (loses the edit if the author stops typing); retry on a timer thread (adds concurrency for no benefit).

## Decision 7 — Explicit texture directory in the actor manifest (F-04, F-59)

**Checked**: `assets/actors/Akane/akane.manifest` declares `Model "actors/Akane/Akane.iqm"`; its textures live in `assets/actors/Akane/akane/`. The loader currently hardcodes the final `"akane"` segment (`world/assets/Assets.fs:245`).

**Decision**: add an optional `Textures : string` field to the actor `Manifest` (an absent field parses to `null`/empty). Resolve the texture directory as:

```fsharp
let textureDirectory (modelDirectory: string) (declared: string) : string =
    if String.IsNullOrEmpty declared then modelDirectory else declared
```

The manifest value is root-relative, like `Model` and `Portraits`. Set Akane's manifest to `[Textures "actors/Akane/akane"]`. At the same time, change `readManifest`/`scanDir` to return the `Manifest` record (with `Id`/`Name` defaulted) instead of a six-tuple, which removes the repeated destructuring in `Assets.scan`.

**Rationale**: the folder is asset data, not a code constant. An explicit manifest field scales to a second actor, is checkable at the scan boundary, and keeps the fallback (model directory) as the natural default rather than a character-name heuristic. Returning the record is the minimal fix for the tuple churn the review found.

**Alternatives considered**: search sibling folders for the first one containing the material (a heuristic that can pick the wrong folder); always use the model directory (breaks Akane's existing layout); infer from the model stem (another implicit convention).

## Decision 8 — One undo stepper and one commit path (F-36, F-37, F-38)

**Decision**: delete `UndoStack.undo`, `UndoStack.redo` and their private `undoCommand`/`applyCommand` forwarders from `state/Command.fs`. Move their tests to `Undo.undoHistory`/`redoHistory` (or to `Command.apply`/`Command.undo` directly where no gameplay state is needed). Replace the six apply-and-record blocks in `editor/Selection.fs` and the one in `editor/AssetBrowser.fs` with `Undo.commit shared &state cmd`. Replace the three state-level command matches with one `isStateLevel`/`placeOf` classifier used by placement, application and inversion.

**Rationale**: the production stepper already handles mixed place-naming commands; the zone-only stepper cannot, and its own comment says so. The six selection blocks are the exact body of `Undo.commit`. The classifier is one list that must not drift.

**Alternatives considered**: keep the zone-only stepper for tests (the tests can drive the real history); rename the zone-level functions and keep forwarders (the forwarders exist only because the module and type share a name).

## Decision 9 — Byref slot scans and byref slot writes (F-26, F-23, F-29)

**Decision**: in `SlotMap.IteriAlive`, bind `let slot = &slots.[i]` before testing `Alive`, so a dead slot's payload is never copied. In `Actor.setSlotWeight`/`setMorphWeight` and the animation-slot updates, write through `let slot = &span.[index]` rather than reconstructing the record. Precompute each animation slot's `(animIndex, f0, f1, blend, weight)` once per `blendPose` into the existing small slot array (maximum eight slots), then sample per joint.

**Rationale**: the payloads are large (`ActorRender` embeds a pose frame and several arrays), and the scans run per frame over capacity 512 for parts. The animation frame window depends only on the slot, not the joint.

**Alternatives considered**: change `IteriAlive` to pass `inref<'T>` (a wider API change; the byref local removes the dead-slot copy immediately); cache a per-slot frame window on the slot (would need invalidation when the clip changes).

## Decision 10 — Remove per-frame allocations on named paths (F-19 to F-25)

**Decisions**:

- `Diagnostics.recent` returns the live `Report list` storage (the list is immutable in practice: reports are added at the head; returning the current list is safe) rather than a comprehension copy. `DebugOverlay.draw` caches the fps line and each formatted report row, rebuilding them only when the report changes. The existing allocation test is extended to cover both.
- `Systems.cameraPose` returns `struct (Vector3 * Vector3)`; `Ui.setLine` returns `struct (int * int)`; `DialogueRender.content` returns a struct triple; callers deconstruct with `let struct (...)`.
- `ActorRender.upload` copies `Pose.Skinning` bytes directly (`MemoryMarshal.AsBytes`) into the mapped transfer buffer; `simulate` caches the posed model and normal matrix on the render record, and `draw` reuses them.
- `Mesh.draw` and `Mesh.drawOutline` take the normal matrix as an argument, so one inversion serves both.
- `CueEngine`'s music ramps call a targeted `Music.applyRow` for the changed row and call `reconcile` only on structural changes.
- `InstanceBuffer.ensure` grows to `max 4 (capacity * 2)`; `reclaim` uses an array/struct queue rather than a cons list where it is on the frame path.
- `Sway.apply` recomposes the changed chain and its descendants when the exact affected subtree is available; the F-27 verify spike records whether it is, and a missing subtree is recorded as an exclusion rather than forcing a change. FR-020 is conditional for this reason.
- `CueEngine`'s blocked `Fork` returns the rewritten `Parallel` shape so the next tick advances the stored value.

**Rationale**: each is a direct removal of a measured or structurally guaranteed per-frame allocation, with no behaviour change. Struct tuples and direct byte copies are the project's established hot-path patterns.

**Alternatives considered**: relying on the JIT to elide reference tuples (it does not for `System.Tuple`); pooling the temporary arrays (unnecessary once the copies are removed); changing the music model to mutable weights (the targeted apply is smaller and keeps authored snapshots).

## Decision 11 — Complete `Mentions` and skip no-op commits (F-08, F-81 to F-90)

**Decision**:

1. Make `Mentions` complete for nested binds. The smallest correct fix is to declare the constructs each derivation reads alongside the derivation (a compiler-checked list), or to run the nested source against a synthetic fact set that contains one fact of each construct. The first is preferred: no runtime probe, and the rule author states the dependency. Add a regression test where the second premise is derived in a later pass.
2. In `Engine.commit`, if the event matches no projection's `Establishes`/`Withdraws`, return `{ engine with History = ... }` after appending history; skip `dropInferred` and `infer`. If projections only change reasons, apply the reason edits and skip the derivation pass.
3. `Facts.add` returns `bool * Facts.Set` (novelty + set) and inserts with `Map.change`; `Derive` drops the separate `contains` call.
4. `dropInferred` uses one `Map.fold`/`Map.choose` traversal.
5. `ParticipantIndex.add` uses `Map.change`; `factParticipants` returns a struct pair or the index iterates per participant, avoiding the per-fact list.
6. `Engine.reasonsFor` returns the stored canonical list; `Facts.canonical` is deleted or kept only for externally built maps.
7. `Derive` counts conclusions as it folds and raises when the bound is exceeded, rather than materialising the whole list first.
8. `Harness.report` decorates facts with their text before sorting and reuses the already-computed history/facts.
9. `Facts.factsOf` uses the retained participant index if `Engine` keeps one; otherwise match the fact directly rather than allocating a participant list.

**Rationale**: the narrative engine is deterministic and test-driven; the `Mentions` gap is a correctness bug, while the rest are per-commit costs that scale with the fact set. The fixes preserve history immutability and canonical ordering.

**Alternatives considered**: drop the pass restriction entirely (correct but re-runs every rule every pass); rebuild the index per reader (more allocation); make facts mutable (violates the data-oriented immutable-fact model).

## Decision 12 — Test-suite consolidation order (F-64 to F-80)

**Decision**: introduce the shared layer first:

- `TestSupport.headlessState`, `headlessShared`, `managerWith`/`emptyManager`, `entity`, `actor`, `prop`, `part`, `triangleSubmesh`, `roomAsset`/`crateAsset`, `populatedClassroom`, `assertAllocatesNothing`, `withTempDir`, `assertNear`.
- `NarrativeTestSupport` owns the shared test-only projections.
- A single `CueTestSupport`-style `describe`/`describeEndings`/`seqOf` lives beside the existing support module.

Then migrate test files in small groups. Fix `CueTests`' module-level mutable state before any parallelization discussion. Replace `AssetLoadTests`' sleep-poll with a `ManualResetEventSlim`/`CountdownEvent`. Replace reflection/reference-identity assertions with behavioural ones. Reduce `NarrativeHarnessTests`'s 200 runs to the two distinct scenarios and delete the subsumed 2-run determinism test. Use `Harness.report` in `NarrativeRuleSetTests`.

**Rationale**: fixtures are mechanical and compiler-checked; the mutable state and sleep-poll are correctness/flakiness issues. Migrating in groups keeps the suite green and makes review tractable.

**Alternatives considered**: one big fixture migration (hard to review, easy to hide a behavioural change); enabling parallel test collections (blocked by process-global `Diagnostics`, `Project` state and CWD mutation).

## Decision 13 — Verify/exclude spikes

The following are **not** implementation commitments until their evidence is reproduced:

- **F-32** (load-path list churn): profile a settle/upload frame; if the lists are bounded by the queue cap and not measurable, leave them.
- **F-33** (`SlotMap.Occupy`): undo is cold; apply the link-only write if the span field assignment compiles, otherwise exclude.
- **F-43** (`EnvironmentRender.release` no-op): remove only if `Entity` no longer needs the call; otherwise keep and document.
- **F-88** (`factsOf`): the engine has no game caller yet; keep the scan if retaining the index complicates `Engine`.
- **V-03** (GPU release policy): verify-only; record the outcome. No change in this feature; any change requires a new or amended spec.
- **V-08** (`Sound.play` rewind): verify-only; record the outcome. If a seek is needed, record it as a follow-up feature rather than changing playback in 098.
- **Sway full recompose (F-27)**: verify-only. Apply the subtree recompose only if the exact affected subtree is already available or cheap to derive; otherwise record the exclusion. FR-020 is conditional.

**Rationale**: the review's severity is not a substitute for reproduction. Each spike ends with a recorded outcome, and the plan/tasks reference the register ID.

## Verified facts carried into the plan

- `igTextUnformatted` is exported by `native/libcimgui.so`.
- `MapGPUTransferBuffer` takes a `bool cycle` in SDL3-CS 3.4.14.1.
- `Project.persist` is called every frame from `screen/GameplayScreen.fs:95`.
- `Project.writeAtomic` already reports a failure and leaves the previous file.
- The actor manifest is a `[<SymbolicExpansion>]` record and Akane's textures live in `assets/actors/Akane/akane/`.
- `Zone.setEnvironment` and `reconcileEnvironment` are the two places an environment becomes active; `activateEnvironment` rebuilds part attachments.
- `Undo.commit` is the existing one-step path; `UndoStack.undo/redo` have no production callers.
- `EnvironmentBatch` currently has no reset and no dirty state; `Zone.gatherStatic` is the one zone-to-batch binding.

## Implementation outcomes (2026-10-17)

**Baseline**: 705 tests, 0 warnings.

**Final validation**: `dotnet build Sugoi.fsproj` succeeds with 0 warnings and 0 errors; `dotnet test test/Sugoi.Tests.fsproj` passes **729/729** in ~2 s.

### Spike outcomes

- **T002 / F-02**: SDL3-CS 3.4.14.1 confirms `MapGPUTransferBuffer(..., bool cycle)`; `cycle = true` is applied to every recurring per-frame map (`InstanceBuffer`, `DebugDraw`, both `Ui` maps, `ActorRender`). One-shot uploads keep `false`.
- **T003**:
  - **F-32**: excluded. Remaining list/pin work is bounded by `LoadQueueCapacity` (8) and runs only on settle/drop, never in steady-state idle.
  - **F-33**: applied. `SlotMap.Occupy` relinks dead slots through a mutable `NextFree` without copying payloads.
  - **F-43**: applied. The `EnvironmentRender.release` no-op was removed; no caller needed it.
  - **F-88**: excluded. `factsOf` keeps the scan, now through `foldParticipants`; retaining the index in `Engine` would complicate commit/merge state for an unused reader.
  - **V-03**: verify-only. No change.
  - **V-08**: verify-only. No change; a required seek is recorded as a follow-up feature.
- **T004 / F-27**: excluded. `Iqm.Model` carries no child/subtree index, so the exact affected subtree is not available; `Sway.apply` keeps the full `composeAll`. FR-020 remains conditional.

### Task exclusions

- **T015** (partial-load rollback test): integration-only. Headless tests cannot create the GPU transfer buffer/texture decode needed to force the mid-build failure; the rollback implementation is in T027.
- **T016** (panel list test): integration-only. `Editor.panelNames` is private and cycling needs an ImGui frame; the list itself is corrected and reviewed.
- **T055 / T061 / T113**: as recorded above (Sway exclusion, bounded load-path work, `factsOf` scan).
- **T118** (manual GUI walkthrough): not run in this environment (no display). The automated build, full suite and headless checks are green.

### Implementation notes

- Test count rose from 705 to 729 with the new regression tests.
- Test isolation: the 19 most-affected classes were run individually, and the full 729 ran in normal order.
- No new modules, dependencies, storage format or `.fsproj` reorder were introduced.
- The reviewed documentation drift was corrected in `docs/EDITOR_DESIGN.md`, `docs/RENDER_PERFORMANCE.md`, `render/Camera.fs`, `world/Cue.fs`, `editor/CueTree.fs` and `world/render/EnvironmentBatch.fs`.
