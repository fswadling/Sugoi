# Quickstart: Codebase Review Remediation

## Prerequisites

- .NET 10 SDK and the repo's native libraries (run `native/build.sh` if `libcimgui.so` is missing).
- From the repository root: `dotnet build`.
- The baseline suite is 705 passing tests.

## Automated coverage

Run the affected suites first after each phase:

```sh
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~InputTests"
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~ProjectTests"
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~AssetsTests"
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~EnvironmentBatchTests"
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~CueTests"
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~NarrativeDerivationTests"
```

Then the full suite:

```sh
dotnet build
dotnet test test/Sugoi.Tests.fsproj
```

What to expect:

1. **UI text safety** — the dynamic-string call sites compile against `ImGui.textUnformatted`; literal labels are unchanged. (Native rendering is checked manually below.)
2. **Persistence** — a failed write leaves `lastWritten` unchanged and retries; an unchanged library after a failure performs at most one attempt per 0.5 s; a later success writes the latest library.
3. **Digit guard** — `Key.Digit 0`, `4` and `10` raise; `1..3` produce distinct masks.
4. **Texture directory** — the pure helper returns the declared directory when present and the model directory when absent; Akane's manifest resolves to `actors/Akane/akane`.
5. **Static batch** — a clean batch's `gather` returns false and `upload` is not called; an edit, add, remove or environment switch marks it dirty.
6. **Asset names** — repeated `actorNames`/`musicNames`/`soundAssets` calls return the same list until invalidation.
7. **Undo** — every selection edit is one history step; the production module has no `UndoStack.undo`/`redo`.
8. **Narrative** — a rule whose second premise is derived in a later pass produces its conclusion; a no-projection commit performs no derivation; reading reasons does not re-sort.
9. **Allocation** — camera follow, dialogue/prompt layout, diagnostics readout, actor palette/model and the idle cue path report 0 bytes in steady state.
10. **Isolation** — each test passes when run alone, and the suite passes in its normal order.

## Manual walkthrough

Run `dotnet run` and use the editor.

### 1. Authored text cannot crash a panel

1. Create an actor or scenario whose name contains `%s %d %n`.
2. Open the Outliner, Inspector, Asset Browser, Advent, Scenario, Dialogue, Cue, Songs and Sounds panels.
3. Expected: the literal text appears in every tree row, label and header; no crash, garbage or warning.
4. Put a `%s` in a diagnostics message (for example, a deliberately broken asset manifest) and confirm the Debug overlay renders it literally.

### 2. A failed write is retried, not lost

1. Make `project.manifest` unwritable (for example `chmod a-w project.manifest`).
2. Rename a scenario or add one.
3. Confirm the Debug overlay reports a write failure once, not every frame.
4. Restore write permission and make no further edit.
5. Expected: within about half a second the file is rewritten with the latest library; the failure report stops.

### 3. Dragging an actor does not rebuild its body

1. Select an actor and drag it for at least 120 consecutive frames.
2. Expected: the body follows smoothly; the process does not allocate/rebuild a capsule per frame. A scale change still rebuilds the shape.
3. Switch to play mode and confirm collision still follows the actor.

### 4. Environment switch releases the old registration

1. Load environment A, note the Debug overlay's draw-call/instance counts.
2. Switch to B, then back to A.
3. Expected: counts return to A's values; no stale geometry is drawn; memory does not grow across repeated A→B→A switches.

### 5. The static scene is gathered only when it changes

1. Open a zone with many props/parts and leave the camera still.
2. Instrument `Zone.gatherStatic`/`EnvironmentBatch.upload` (or watch the Debug overlay if it reports gathers).
3. Expected: over at least 1,000 idle frames there are zero gathers and zero uploads. Moving the camera still updates the view-projection; moving a prop triggers exactly one gather/upload.

### 6. Hidden panels and idle editor work do no work

1. Dock several panels as tabs. Select Outliner, then hide/dock another tab.
2. Expected: hidden tabs' list building, sorting and row formatting stop; switching back repaints the current state. A collapsed window does not run its body.
3. With an unchanged cue, asset set, active environment and camera, confirm the cue rows, sorted name lists, grid and pin set are not rebuilt, re-sorted, re-locked or re-allocated on consecutive idle frames.

### 7. A second actor loads its own textures

1. Add a second actor asset with its textures in a directory named by `[Textures …]` in its manifest.
2. Expected: the actor loads and renders; no character name is hardcoded; an actor that omits `Textures` resolves beside its model.

### 8. Dialogue and prompt draw without per-frame allocation

1. Show a line and leave it idle; open the prompt.
2. Expected: no GC growth over a sustained idle run. The existing allocation tests assert zero bytes for the measured paths.

## Verify/exclude spikes

Record the outcome in the implementation notes before changing any of these:

- **Transfer-buffer cycling**: confirm the API contract and apply `cycle = true` to recurring maps. This is not an opt-out; record the evidence.
- **Sway subtree recompose (F-27)**: apply only if the exact affected subtree is available; otherwise keep the full pass and record the exclusion. FR-020 is conditional.
- **`SlotMap.Occupy` link write**: apply only if the span field assignment compiles; otherwise exclude.
- **Load-path list churn (F-32)**: leave it if a settle/upload frame shows it is bounded and unmeasurable.
- **`EnvironmentRender.release` no-op (F-43)**: remove only if the entity path no longer needs it.
- **`factsOf` index retention (F-88)**: keep the scan if retaining the index complicates `Engine`.
- **V-03 (GPU release policy)** and **V-08 (sound voice rewind)**: verify-only. Record the outcome; do not change either in this feature without a new or amended spec.

## Expected outcome

- `dotnet build` succeeds with 0 warnings.
- The full suite passes, including the new regression tests, and runs within 10% of the 705-test baseline duration.
- The manual checks above pass.
- Every `Verify` item has a recorded outcome; every `Exclude` item is unchanged.
