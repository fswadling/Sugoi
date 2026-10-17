# Implementation Plan: Codebase Review Remediation

**Branch**: `098-codebase-review-fixes` | **Date**: 2026-10-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/098-codebase-review-fixes/spec.md`

## Summary

Fix the confirmed findings from the nine-reviewer pass in five independently verifiable tracks, ordered by risk:

1. **Safety and data integrity** — render dynamic UI text without variadic formatting, cycle per-frame staging buffers, retry failed project writes, resolve actor textures from the manifest, reset the static-scene registration on environment change, move actor bodies in place, guard the digit key mask, roll back partial asset loads, and repair the stale panel list.
2. **Frame cost follows change** — a dirty-flag static batch, panel-visibility guards, cached asset-name lists, cached cue rows, an unchanged-pin fast path, a reused grid, per-row no-allocation paths, targeted music ramps, one-pass palette/model uploads, per-slot animation windows, geometric instance growth, conditional subtree sway recomposition, and a persisted fork shape.
3. **One implementation per concept** — remove the second undo stepper and the test-only forwarders, route selection through `Undo.commit`, collapse the three command classifiers, and share the ImGui buffer helpers, pipeline builder, cue editor, dialogue controls, name combos, glTF float parser, capsule offset, body traversal, name scan and load-report helper.
4. **Test-suite consolidation** — one headless fixture layer, no module-level mutable state, one cue description, deterministic async waits, production accessors, one allocation helper, and removal of implementation-detail assertions.
5. **Narrative hardening** — complete `Mentions` for nested premises, skip the fixpoint when a commit changes no projected fact, and remove the redundant traversals/allocations in derivation, fact insertion, participant indexing, reason reads and report construction.

No new module, cluster, dependency, storage format or public behaviour is introduced beyond the explicitly listed fixes. The changed modules stay in their existing compile blocks, so no `.fsproj` order change is expected. Every track ends with a green build and the full suite; the test count may grow but the existing 705 assertions must continue to hold.

## Technical Context

**Language/Version**: F# 10 on .NET 10

**Primary Dependencies**: unchanged — SDL3-CS 3.4.14.1, the project's cimgui/SDL3 ImGui bindings, JoltPhysicsSharp 2.22.0, Prime 11.5.2

**Storage**: `project.manifest` at the launch working directory (Prime s-syntax); asset manifests under `assets/actors/` and the environment roots

**Testing**: xUnit (`dotnet test test/Sugoi.Tests.fsproj`), headless where the seam permits; allocation assertions use `GC.GetAllocatedBytesForCurrentThread`

**Target Platform**: Linux desktop (SDL3); editor and game share one window

**Project Type**: Single F# desktop game/editor project

**Performance Goals**: 60 FPS; 0 static-scene gathers and 0 static-scene uploads on an unchanged scene; 0 hidden-panel body work; 0 managed bytes on the named steady-state play/draw paths

**Constraints**: F# compile order; `world/` and `state/` dependency direction; the 705-test baseline; `igTextUnformatted` is the safe text binding; a transfer buffer reused across frames must be mapped with `cycle = true`; write failures retry on a bounded schedule; no new module or dependency

**Scale/Scope**: 91 confirmed findings (35 `Fix` performance, 10 correctness, 18 abstraction/dead, 17 repetition, 17 test, 10 narrative, 1 documentation, plus 12 verify/exclude); ~45 production files and ~30 test files touched, with no architectural move

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the plan mostly deletes: a second undo stepper, dead exports, duplicated helpers, a redundant classifier and a throwaway zone. The only new state is a dirty flag, three name caches and a retry flag, each with one consumer. No generic framework is introduced. |
| II. Fail Loudly | Pass — `Key.Digit` outside 1..3 raises; a failed write is reported and retried rather than silently accepted; a non-reproducing finding is excluded with evidence rather than given a fallback; dead seams are kept only when explicitly documented. |
| III. Spec-First | Pass — `specs/098-codebase-review-fixes/spec.md` is written and drives this plan; every finding in the register has a disposition. |
| IV. Data-Oriented Architecture | Pass — the static batch keeps contiguous arrays and adds one mutable dirty flag; caches hold immutable lists; slot scans use byref access; the narrative facts remain an ordered map. No class or inheritance is introduced. |
| V. Minimise GC Pressure | Pass — the plan removes per-frame reference tuples, list copies, closures, per-joint repacks and full mix rebuilds, and adds allocation assertions over each named path. |
| Narrative principles | Pass — N-I: history is untouched, only interpretation and the derivation bookkeeping change. N-II: the nested `Mentions` fix removes a completeness gap without introducing iteration-order dependence; output ordering remains canonical. N-IV: every conclusion still records its rule and premises; the fixes reduce redundant work without dropping a reason. N-V: the additions are headless tests. |

Re-check after Phase 1: the design still passes all five gates and the three engaged narrative principles. No violation needs tracking.

## Project Structure

### Documentation (this feature)

```text
specs/098-codebase-review-fixes/
├── plan.md                      # this file
├── research.md                  # Phase 0 decisions and verified unknowns
├── data-model.md                # Phase 1 state additions and transitions
├── quickstart.md                # Phase 1 validation guide
├── contracts/
│   └── remediation-invariants.md# internal seams and their guarantees
├── checklists/
│   └── requirements.md          # spec quality checklist
└── tasks.md                     # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
core/
├── Diagnostics.fs               # CHANGED: recent() returns live storage; clear() decision recorded
├── Gltf.fs                      # CHANGED: one float-array helper; unused component constants removed
├── Input.fs                     # CHANGED: Digit range guard; KeyMask.add removed
├── Iqm.fs                       # CHANGED: unread geometry/header fields dropped; frame channels narrowed
├── Lru.fs                       # CHANGED: unused tail removed
├── Maths.fs                     # CHANGED: catmullRom private
└── SlotMap.fs                   # CHANGED: dead-slot payload not copied; Occupied link write; Free reuses CheckedSlot

render/
├── Gpu.fs                       # CHANGED: one pipeline builder; shared shader load helper
├── Texture.fs                   # CHANGED: one sampler helper with an address mode
├── Mesh.fs                      # CHANGED: normal matrix computed once per actor draw path
├── InstanceBuffer.fs            # CHANGED: cycle=true for recurring maps; geometric growth
├── DebugDraw.fs                 # CHANGED: cycle=true; direct float Marshal.Copy; shared shader/write helper
├── Ui.fs                        # CHANGED: cycle=true; direct float/byte copy; setLine returns a struct tuple
├── ImGui.fs                     # CHANGED: textUnformatted binding and dynamic-string callers
├── DebugOverlay.fs              # CHANGED: unformatted dynamic text; fps line and report rows cached
└── Camera.fs                    # CHANGED: comment corrected only

audio/
├── Music.fs                     # CHANGED: targeted row apply; unused isPaused removed
└── Sound.fs                     # CHANGED: capacity made private

world/
├── Entity.fs                    # CHANGED: modelMatrix fast path; capsuleOffset; baseEntity use
├── Actor.fs                     # CHANGED: per-slot frame window precomputed; in-place slot writes
├── Zone.fs                      # CHANGED: in-place actor body move; dirty marks and batch reset; one body traversal;
│                                #   one name scan; baseEntity/bindings; capsuleOffset
├── Sway.fs                      # CHANGED only if the F-27 verify confirms an exact affected subtree
├── Systems.fs                   # CHANGED: struct camera pose; capsuleOffset; unchanged-clip fast path
├── Project.fs                   # CHANGED: failed write not recorded; bounded 0.5 s retry entry point
├── assets/
│   ├── Assets.fs                # CHANGED: manifest record with Textures; rollback; name caches; one load-report helper
│   ├── ActorAsset.fs            # CHANGED: pure texture-directory helper
│   ├── AssetLoad.fs             # CHANGED: forwarders/dead accessors removed; list churn reduced
│   └── EnvironmentAsset.fs      # CHANGED: dead accessors removed
└── render/
    ├── ActorRender.fs           # CHANGED: direct palette copy; posed model cached; cycle=true; clip fast path
    ├── DialogueRender.fs        # CHANGED: struct tuple returns; no per-frame allocation
    ├── EnvironmentBatch.fs      # CHANGED: Dirty/reset; no per-frame closures; cached submesh slots
    ├── EnvironmentRender.fs     # CHANGED: no-op release removed or routed
    └── PromptRender.fs          # CHANGED: struct tuple returns

state/
├── Command.fs                   # CHANGED: second stepper and forwarders removed; one classifier
├── CueEngine.fs                 # CHANGED: targeted music ramp; one noOp; persisted fork shape; one chain pass;
│                                #   capsuleOffset; actor detach via Zone
├── ScenarioFork.fs              # CHANGED: capacity constant instead of a throwaway zone; shared detach
├── SharedState.fs               # CHANGED: dead mutable removed
└── Undo.fs                      # UNCHANGED: already the one stepper and commit path

editor/
├── AdventPanel.fs               # CHANGED: visibility guard; shared bufferText
├── AssetBrowser.fs              # CHANGED: visibility guard; Undo.commit
├── CueEditor.fs                 # CHANGED: visibility guard; one editor pipeline; one name combo
├── CueTree.fs                   # CHANGED: visibility guard; shared helpers; cached rows; shared dialogue kind control
├── DialoguePanel.fs             # CHANGED: visibility guard; shared kind control and bufferText
├── Editor.fs                    # CHANGED: panel list corrected; pick takes the ray
├── Inspector.fs                 # CHANGED: visibility guard; shared colour/text scaffolds; numeric formatting
├── MiscPanel.fs                 # CHANGED: visibility guard
├── Outliner.fs                  # CHANGED: visibility guard; one slot-map row helper; stable IDs
├── Raycast.fs                   # CHANGED: dead intersectAabb removed or documented
├── ScenarioPanel.fs             # CHANGED: visibility guard; shared bufferText
├── Selection.fs                 # CHANGED: every edit through Undo.commit
├── SongPanel.fs                 # CHANGED: visibility guard; setup moved inside
├── SoundPanel.fs                # CHANGED: visibility guard; setup moved inside
└── Viewport.fs                  # CHANGED: cached grid; byref accumulator; pick takes a ray

screen/
├── GameplayScreen.fs            # CHANGED: gated static upload; pin fast path; persistFrame; one activeZone local
└── MainMenuScreen.fs            # CHANGED: visibility guard (consistency)

narrative/
├── Rules.fs                     # CHANGED: complete Mentions; reduced per-conclusion allocation; dead builder check
├── Derive.fs                    # CHANGED: no-op commit skip; bounded counting fold; one-pass add
├── Facts.fs                     # CHANGED: add returns novelty; one-pass dropInferred; canonical reason reads
├── ParticipantIndex.fs          # CHANGED: Map.change; no per-fact list
├── Engine.fs                    # CHANGED: no re-canonicalisation; index retention decision
├── Harness.fs                   # CHANGED: decorate-sort; reuse computed history/facts
├── Canonical.fs                 # CHANGED: unreachable branch removed
└── History.fs                   # CHANGED: lastTime private

docs/
├── EDITOR_DESIGN.md             # CHANGED: scenario undo statement corrected
├── RENDER_PERFORMANCE.md        # CHANGED if the implementation changes a measurement statement
└── ARCHITECTURE.md              # CHANGED if the implementation changes a decision statement

assets/actors/Akane/akane.manifest # CHANGED: explicit [Textures "actors/Akane/akane"]

test/
├── TestSupport.fs               # CHANGED: headless fixtures, allocation/temp-dir/assert helpers
├── NarrativeTestSupport.fs      # CHANGED: shared test-only projections
├── EnvironmentBatchTests.fs     # CHANGED: dirty/gather/upload contract
├── InputTests.fs                # CHANGED: digit range
├── ProjectTests.fs              # CHANGED: retry and content-based no-write assertion
├── AssetsTests.fs               # CHANGED: manifest record and texture directory
├── ActorTests.fs                # CHANGED: production clip accessor; shared fixture
├── CueTests.fs                  # CHANGED: owned state, shared describe, production accessors
├── CuePanelTests.fs             # CHANGED: shared describe, behavioural assertions
├── ZoneTests/SpawningTests/...   # CHANGED: shared fixtures and rigs
├── Narrative*Tests.fs           # CHANGED: nested premise, shared projections, reduced runs
└── (remaining touched tests)     # CHANGED: fixture consolidation and assertions
```

**Structure Decision**: Single-project layout, matching `docs/PROJECT_STRUCTURE.md`. No file moves and no `.fsproj` compile-order change are planned; new pure helpers live in the module that already owns the concept (`Entity.capsuleOffset`, `ActorAsset.textureDirectory`, `EnvironmentBatch.reset`, `Project.persistFrame`).

## Implementation Phases

Each phase is independently shippable and ends with `dotnet build` plus the affected tests.

### Phase A — Safety and data integrity (P1)

Fix F-01 to F-10. Add regression tests first where a headless seam exists (digit guard, persistence retry, asset rollback, texture-directory helper, manifest parsing, `Mentions`). Apply the native-binding and GPU-lifetime changes last in the phase, because they need the manual checks in `quickstart.md`.

### Phase B — Frame cost follows change (P1)

Introduce the dirty static batch and the panel guards first; they multiply the value of every later allocation fix. Then apply the allocation and caching changes module by module, adding an allocation assertion for each named path. Keep the view-projection push per frame.

### Phase C — One implementation per concept (P2)

Delete before abstracting: remove the dead exports, the second stepper, the forwarders and the duplicate helpers. Then consolidate the live patterns, compiler- and grep-verified. Do not introduce a new module for a helper that belongs to an existing owner.

### Phase D — Test-suite consolidation (P2)

Introduce the shared fixtures and helpers, migrate files in small groups, and keep the suite green between groups. Fix the shared mutable state and the sleep-poll test before reducing the narrative harness's repeated runs.

### Phase E — Narrative hardening (P3)

Land the `Mentions` fix and its regression test first; then the no-op commit skip, then the allocation/traversal changes. Re-run the narrative suites and the performance test after each change.

### Phase F — Verification and documentation

Correct the drifted comments/docs, run the full suite, check the manual validation matrix, and record the outcome of every `Verify` item in the register.

## Complexity Tracking

No constitution violations. The three state additions are each a single field or flag with one consumer:

| Addition | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| `EnvironmentBatch.Dirty` | The batch must know whether the static scene changed; without it every frame re-gathers and re-uploads. | Recomputing a hash/version from all props every frame costs more than the flag and still allocates. |
| Three name-cache fields on `AssetManager` | Name lists are sorted per frame by several panels; assets change only at scan/settle. | A global memo in the editor cannot be reached by all callers and would still rebuild per panel. |
| `Project` failed-write retry state | A failed write must not be recorded as written, and a retry must not run per frame. | Retrying every frame issues unbounded I/O and error reports; never retrying loses the edit. |
