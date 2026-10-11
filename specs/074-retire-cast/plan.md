# Implementation Plan: Retire the Cast — Actors and Speakers Addressed by Name

**Branch**: `074-retire-cast` | **Date**: 2026-10-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/074-retire-cast/spec.md`

## Summary

Remove the cast from the game. `world/Casting.fs` (the `Look` vocabulary, the `Slot { Character; Look }` key and
the `Bindings` map), `SharedState.Cast` and `editor/CastPanel.fs` are deleted; the closed `Character` union moves
out of the game into the narrative subsystem (`cast/Roster.fs` → `narrative/Roster.fs`), which keeps compiling
untouched. A portrait line's speaker becomes a plain **string** (the actor's name): `Dialogue.Kind.PortraitDialogue
of speaker: string * Emotion`. `world/DialogueRender.fs` resolves the speaker to her actor asset **by name** in the
asset manager (the asset whose declared `Name` equals the string, lowest `Id` winning a tie) and draws that asset's
declared portrait. The lookup goes through a **name index built once** when the assets are scanned, so the per-frame
draw path stays allocation-free. Both speaker choosers — the cue `Exposit` editor and the Dialogue panel — offer
the discovered actor asset names instead of the roster.

It also repairs the one hot-path cost the by-name work left behind: the cue's in-flight actor ops
(`MoveActorState`/`RotateActorState`/`BlendState`) re-resolved their actor by scanning for her `Name` **every
tick**. They now capture the resolved slot **`Handle`** and `Place` once, at `begin*`, and carry them in the
`…State` (`world/Cue.fs` names the `Handle` from `core/SlotMap.fs`, which is compiled below it); each tick is an
O(1) slot-map lookup that reuses the actor the op began on, and allocates nothing. The one-shot ops keep the name
scan.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `world/Assets.fs` (`AssetManager`; gains a name index), `world/DialogueRender.fs`
(resolution + draw), `world/Dialogue.fs` (`Kind`), `state/SharedState.fs` (drops `Cast`), `editor/CueTree.fs`
(Exposit speaker combo), `editor/CueEditor.fs`, `editor/DialoguePanel.fs`, `editor/Editor.fs` (panel list),
`screen/GameplayScreen.fs` (draw call), the narrative subsystem (receives `Roster.fs`), and `world/Cue.fs` +
`world/CueEngine.fs` (`core/SlotMap`'s `Handle`; the actor `…State` cases carry it). No new package.

**Storage**: N/A (nothing persisted)

**Testing**: xUnit, headless (`dotnet test`). `test/DialogueTests.fs` migrated to string speakers; `test/CastingTests.fs`
deleted; `test/RosterTests.fs` unchanged (module name preserved); a new pure test for the asset name index; every
`SharedState` literal in tests loses its `Cast = …` line; the existing `tryPortrait allocates nothing per call` guard
is retained. No device.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor, with a narrative engine in-process)

**Performance Goals**: the portrait lookup is on the per-frame draw path and MUST stay allocation-free
(`specs/038` FR-010) — met by the scan-time index. The in-flight actor ops MUST also resolve in O(1) with no
per-tick allocation (FR-014–FR-016) — met by carrying the slot `Handle` in the `…State` instead of scanning.

**Constraints**: warning-free build; green suite; no device opened; the narrative engine's suite must pass
unmodified.

**Scale/Scope**: game source — 2 files deleted (`world/Casting.fs`, `editor/CastPanel.fs`), 1 moved
(`cast/Roster.fs` → `narrative/Roster.fs`), ~13 edited (`world/Dialogue.fs`, `world/DialogueRender.fs`,
`world/Assets.fs`, `world/Cue.fs`, `world/CueEngine.fs`, `world/Actor.fs`, `world/ActorRender.fs`,
`world/Systems.fs`, `physics/Physics.fs`, `state/SharedState.fs`, `screen/GameplayScreen.fs`,
`editor/DialoguePanel.fs`, `editor/CueTree.fs`, `editor/CueEditor.fs`, `editor/Editor.fs`), `fsharp-game.fsproj`;
tests — 1 deleted, ~12 edited, 1 added; docs — `ARCHITECTURE.md`, `PROJECT_STRUCTURE.md`, `ROADMAP.md`,
`EDITOR_DESIGN.md`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. It removes two modules, a shared-state field, a panel and a whole lookup layer
  (`Look`/`Slot`/`Cast`); the only addition is one small index on the asset manager (justified by FR-011) and one
  read helper. Net deletion.
- **II. Fail Loudly** — PASS. A speaker with no matching asset is an *expected* authoring state, so it stays a
  value (`MissReason`), not an exception (`specs/038` FR-006); a partial portrait set still fails loudly at load.
  The retired `MissReason` cases that can no longer occur (`AssetNotLoaded`) are removed rather than left dead.
- **III. Spec-First** — PASS. This feature is `specs/074-retire-cast`.
- **IV. Data-Oriented Architecture** — PASS. The speaker is a string; the asset name index is a
  `Dictionary<string, ActorAsset>` built once; no object graph is introduced.
- **V. Minimise GC Pressure** — PASS. The index keeps the per-frame portrait resolve allocation-free (contains +
  indexer, the pattern the current code already uses to avoid tuples), and the in-flight actor ops stop allocating
  per tick (the `Handle` carry, FR-016); the chooser helpers are cold editor paths.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/074-retire-cast/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── portraits-by-name.md
│   └── actor-op-state.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
fsharp-game.fsproj         # drop `cast/Roster.fs` (moved) and `world/Casting.fs`, `editor/CastPanel.fs`;
                           #   add `narrative/Roster.fs` first among the narrative files
narrative/Roster.fs        # MOVED from cast/Roster.fs — the closed `Character` union, engine-owned; module
                           #   name preserved so every narrative consumer (`open Roster`) and `RosterTests`
                           #   compile unchanged
world/Dialogue.fs          # `Kind.PortraitDialogue of speaker: string * Emotion`; no `Roster` dependency
world/DialogueRender.fs    # resolve speaker string -> asset by name; drop the `cast` parameter; reduce MissReason
world/Cue.fs               # the actor `…State` cases (MoveActorState/RotateActorState/BlendState) carry the slot
                           #   `Handle` + `Place` (from `core/SlotMap`), captured at begin; `open SlotMap`
world/CueEngine.fs         # `begin*` capture the handle; the `step*` funcs look the actor up in O(1) and end the
                           #   op on an absent place / dead slot / changed name; one-shot ops keep the name scan
physics/Physics.fs         # `getBodyTransform` returns a `struct (Vector3 * Quaternion)`, not a reference tuple
                           #   (it runs every play frame from `Systems.syncActors` and every move tick)
world/Actor.fs             # `tryFindAnimIndex` becomes a `voption` loop (no per-call closure, no `Some`)
world/ActorRender.fs       # the per-tick clip/morph setters use those loops, so a step allocates nothing
world/Assets.fs            # `AssetManager` gains a name index (`ActorsByName`); `scan`/`loadActors` build it;
                           #   an `actorNames` read helper for the choosers
state/SharedState.fs       # `Cast : Casting.Cast` removed; `create` no longer builds one
screen/GameplayScreen.fs   # `DialogueRender.draw` call drops `shared.Cast`
editor/DialoguePanel.fs    # speaker chooser offers discovered actor names with a typed fallback; advisory wording
editor/CueTree.fs          # `drawEditor` takes the discovered actor names; `filteredNameCombo` adds a typed
                           #   fallback for the Exposit speaker, used by both choosers
editor/CueEditor.fs        # pass the actor names into `CueTree.drawEditor`
editor/Editor.fs           # drop the "Cast" entry from `panelNames` and the `CastPanel.draw` call
editor/CastPanel.fs        # DELETED
world/Casting.fs           # DELETED
cast/Roster.fs             # DELETED (moved to narrative/Roster.fs)

test/DialogueTests.fs      # speaker strings; `castBinding`/`emptyCast` deleted; `tryPortrait` arity/args
test/CastingTests.fs       # DELETED
test/RosterTests.fs        # unchanged (module name preserved)
test/AssetNameIndexTests.fs# NEW — the asset name index: by name, exact, lowest id wins, missing -> none
test/CueTests.fs           # the in-flight actor ops: reuse the handle, end on removal/recycled slot, no per-tick
                           #   allocation (a guard beside the existing cue allocation properties)
test/*.fs                  # every `SharedState` literal drops `Cast = …`
docs/ARCHITECTURE.md       # the cast / portrait passages
docs/PROJECT_STRUCTURE.md  # the `cast/` cluster and compile order
docs/ROADMAP.md            # the phase-10 cast passage
docs/EDITOR_DESIGN.md      # the Cast panel entry (D-022)
assets/README.md           # NEW — the actor manifest fields + the name→portraits contract (author-facing)
```

**Structure Decision**: One module leaves the game and one cluster (`cast/`) disappears. `narrative/Roster.fs` keeps
the `Roster` module name and is listed first among the narrative files, so the engine and its tests compile with no
edits — the relocation is a move plus a compile-order entry. In the game, the dialogue speaker becomes a string
(the by-name pattern `specs/072`/`specs/073` established for actors), and the portrait lookup moves from the binding
to the asset manager's name index. `world/Dialogue.fs` sheds its last dependency (it no longer names `Roster`) and
sits before `Assets` as before. Separately, `world/Cue.fs` gains `core/SlotMap.fs`'s `Handle` in its actor `…State`
cases — legal because `core/` is compiled below `world/`, and it keeps the module free of zone/entity/asset types
(FR-019).

## Complexity Tracking

> No Constitution Check violations.
