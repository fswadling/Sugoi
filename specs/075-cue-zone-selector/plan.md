# Implementation Plan: An Explicit Zone on Every Entity Cue Op

**Branch**: `075-cue-zone-selector` | **Date**: 2026-10-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/075-cue-zone-selector/spec.md`

## Summary

Give every cue op that names an actor or a waypoint by a string an explicit **`place: Place`** argument:
`RemoveActor`, `Animate`, `CrossFade`, `Morph`, `CrossMorph`, `EnablePlayer`, `DisablePlayer`, `SetCamera`,
`FlyCamera`. Such an op resolves its entity in the **named place's zone** rather than by a global scan of
the present places (the actor ops) or in the active zone (the camera ops). `MoveActor`/`RotateActor` are
the deliberate exceptions: physics is attached per active zone, so they keep no zone and act on the active
zone.

The editor's per-op **zone selector is the existing place combo** (`CueTree.drawEditor`), extended from the
zone/trigger ops to these ops; the entity choosers (`CueEditor.drawNodeText`) read the op's place; and a
newly-created op defaults its place to the **active zone**. This makes the actor/camera choosers show the
zone the op actually targets — the missing selector in the original report.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `Place` (the closed place union, already on `AddActor`), `Zone.tryActorByName` /
`Zone.tryWaypointByName`, `Zone.tryTriggerByName`, `state/GameplayState.fs` (the zones map; loses the global
`tryActorByName`), `world/Cue.fs`, `world/CueEngine.fs`, `editor/CueTree.fs` (place combo + write helpers +
`defaultOfKind`), `editor/CueEditor.fs` (`drawNodeText`). No new dependency.

**Storage**: N/A (nothing persisted)

**Testing**: xUnit, headless (`dotnet test`) — `test/CueTests.fs` (through `CueEngine.tick` with a fabricated
`ActorAsset`) and `test/CuePanelTests.fs` (the pure control: labels, kinds, write helpers). No device.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: none new — resolution becomes one `Dictionary.TryGetValue` plus the existing slot-map
scan (was a scan of every present place), cold relative to the per-frame actor scan it replaces.

**Constraints**: the build is warning-free; the suite is green; no device opened; the cue op set is otherwise
unchanged.

**Scale/Scope**: 5 source files (`world/Cue.fs`, `world/CueEngine.fs`, `state/GameplayState.fs`,
`editor/CueTree.fs`, `editor/CueEditor.fs`), ~3 test files; no `.fsproj` change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. Net-simplifying: it deletes the global resolver (`GameplayState.tryActorByName`)
  and the `place <> ActiveZone` guards that became implied; it adds one `Place` field to nine op cases and
  extends one existing write helper (`withPlace`) and one combo.
- **II. Fail Loudly** — PASS. Actor ops keep their no-op rule for state they cannot act on; the camera ops
  keep their raise-on-unresolved convention (`specs/054` FR-009). An explicit zone is a value, not a guard.
- **III. Spec-First** — PASS. This feature is `specs/075-cue-zone-selector`.
- **IV. Data-Oriented Architecture** — PASS. The zone is the closed `Place` union value carried in the op;
  resolution reads the existing dictionary and slot map; no new heap structure.
- **V. Minimise GC Pressure** — PASS. No hot-path change; the extra lookup is O(1).

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/075-cue-zone-selector/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── cue-entity-zone.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Cue.fs              # nine ops gain `place: Place` (first arg): RemoveActor, Animate, CrossFade, Morph,
                          #   CrossMorph, EnablePlayer, DisablePlayer, SetCamera, FlyCamera; MoveActor/
                          #   RotateActor unchanged; the …State cases unchanged (Blend/Move/Rotate already
                          #   carry place; FlyCameraState captures its path)
world/CueEngine.fs        # tryResolveActor → resolve in a given place; removeActor/beginBlend/EnablePlayer/
                          #   DisablePlayer take the op's place; beginMove/beginRotate resolve in the active
                          #   zone and drop the redundant `place <> ActiveZone` guard; SetCamera/FlyCamera read
                          #   the named place's zone
state/GameplayState.fs    # remove the now-unused global `tryActorByName`
editor/CueTree.fs         # withPlace covers the nine ops; withActor/withText/withText2/withWaypoint keep the
                          #   place; label/kindName show the place; defaultOfKind takes a default place;
                          #   drawEditor takes that default place and extends the place combo to the nine ops
editor/CueEditor.fs       # drawNodeText resolves each node's own place for the actor/camera choosers (not
                          #   drawn when the place is absent); assetNamesOf reads the op's place; draw and
                          #   drawTrigger pass the active place as the default
test/CueTests.fs          # actor/camera ops by named place: resolution in the named zone, an absent place and
                          #   a name not in it no-op; the global-scan tests retire
test/CuePanelTests.fs     # describe/labels/kinds/writes under the new shapes; withPlace on the nine ops;
                          #   defaultOfKind's default place
test/PlayerTests.fs       # player toggles name a place
docs/ARCHITECTURE.md      # the cue-actor-op / cue-module passage
docs/ROADMAP.md           # the phase-9/10 cue passage
```

**Structure Decision**: No module moves and no compile-order change. The new work is confined to the cue
trio — `world/Cue.fs` (the vocabulary), `world/CueEngine.fs` (the effects), and the editor
(`CueTree.fs`/`CueEditor.fs`) — plus the removal of a now-unused resolver in `state/GameplayState.fs`.
The place combo already lives in `CueTree.drawEditor` (where the zone/trigger ops draw theirs), so the
per-op selector is a generalization of an existing control rather than a new one; `CueTree` may name
`Place` and needs no zone registry (`specs/047` FR-006 holds).

## Complexity Tracking

> No Constitution Check violations.
