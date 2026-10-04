# Implementation Plan: Zones as Places

**Branch**: `061-zones-as-places` | **Date**: 2026-10-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/061-zones-as-places/spec.md`

## Summary

Make a runtime zone's identity the authored **`Place`** union instead of a free string. Extract the
`Place`/`Period` vocabulary from `ZoneRegistry` into its own early-compiled module (mirroring `Roster`);
key `GameplayState.Zones` by `Place` and hold `ActiveZone : Place`; retype the zone steps, the cue's
`SetZone`, and the Outliner/registry-panel/cue-panel controls to name a place; populate the state at
creation with the designated **start place**; and manage a place's **presence** (add makes a place present,
remove makes it absent and unloads its environment). A place is a compile-time constant and is never created
or deleted; the active place can never be made absent (switch first). The registry stays an orthogonal,
sparse config store.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `Prime` (unchanged), `System.Collections.Generic` (the zone map),
`Microsoft.FSharp.Reflection` (the registry's place tree)

**Storage**: N/A (nothing persisted yet; ROADMAP phase 15)

**Testing**: xUnit, headless (`dotnet test`); no device

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: none new — zone operations are editor-time or once-per-switch, not per-frame

**Constraints**: no free-string zone identity remains; the build is warning-free; the suite is green

**Scale/Scope**: ~9 source files + 1 new module; ~7 test files touched (zones are named in many tests)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. A closed vocabulary + retyped identity; no new subsystem. The only behaviour not
  present today is the explicit environment unload on removal (FR-010), which is required by the spec.
- **II. Fail Loudly** — PASS. Adding a place already present is refused; the cue's zone op keeps today's
  behaviour when a place is not present (a later feature makes it a no-op, per the spec's Assumptions).
- **III. Spec-First** — PASS. This feature is `specs/061-zones-as-places`.
- **IV. Data-Oriented Architecture** — PASS. `Place` is a closed union keyed in a dictionary; zones are not
  touched per frame.
- **V. Minimise GC Pressure** — PASS. Zone lookups happen once per frame at most (the active zone) and on
  switches; no hot-path change.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/061-zones-as-places/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── place-identity.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Place.fs            # NEW: the Place/Period vocabulary, placeName, all, start (early-compiled)
world/ZoneRegistry.fs     # opens Place; keeps byZone + the sparse Registry (config)
world/Cue.fs              # SetZone now names a Place
world/Zone.fs             # drop the vestigial Name field
world/CueEngine.fs        # diagnostics name the active place
state/GameplayState.fs    # Zones keyed by Place; ActiveZone : Place; create/switch/add/select/delete
state/Command.fs          # ZoneMove/ZoneAdd/ZoneDelete carry Place
state/History.fs          # the zone steps carry out by Place
state/EditorState.fs      # drop NewZoneName (+ zoneNameBufferSize)
editor/Outliner.fs        # Zones list of present places; add = choose an absent place; no free text
editor/CueTree.fs         # SetZone: a place chooser + placeName label
editor/CueEditor.fs       # route SetZone through the chooser
editor/ZoneRegistryPanel.fs  # Register captures the active place; Instantiate = present+active+load (one step)
fsharp-game.fsproj        # add world/Place.fs before world/Cue.fs
test/*.fs                 # retype zone names to places (GameplayStateTests, CueTests, CuePanelTests,
                          # AdventTests, SelectionTests, SpawningTests, ZoneRegistryTests)
```

**Structure Decision**: The vocabulary lands in `world/` (`Place.fs`) because both the runtime (state) and
the registry name it; extracting it above `world/Cue.fs` is what lets the cue's zone op name a place. The
registry keeps only config (`Registry`) and the panel tree (`byZone`), preserving the `Roster`/`Casting`
split of vocabulary vs mapping.

## Complexity Tracking

> No Constitution Check violations.
