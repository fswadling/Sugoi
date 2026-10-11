# Implementation Plan: Cue Actors Addressed by Name — a Scene-Driven Chooser

**Branch**: `073-cue-actor-by-name` | **Date**: 2026-10-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/073-cue-actor-by-name/spec.md`

## Summary

Make the cue vocabulary and its editor **scene-driven**. Every actor op carries a plain **string** actor name
instead of a `Roster.Character`, so `world/Cue.fs` no longer depends on `Roster` or `Casting`. The actor
chooser moves out of `CueTree.drawEditor` (closed-set combos) into `CueEditor.drawNodeText` (scene reads),
where it offers only the **viewed place's** live actors — mirroring the waypoint chooser. The clip/morph
choosers read the **selected actor's asset** (resolved in the viewed zone), not the cast binding. `AddActor`,
which creates an actor, names a discovered **asset** (chosen from the asset manager) and a typed **name**
(defaulting to the asset's), plus its place and waypoint.

The roster, the look vocabulary, the cast binding, the cast panel and the dialogue portrait path are
**unchanged** — removed from cues, not from the game.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `Place`, `Zone.tryActorByName` (present), `GameplayState.tryActorByName` (takes a
string now), `Assets.AssetManager` (discovered actor assets: `Actors`), `world/Cue.fs`,
`world/CueEngine.fs` (`addActor`/`removeActor`/`tryResolveActor`/blends/motion/player), `editor/CueTree.fs`
(combos + write helpers), `editor/CueEditor.fs` (`drawNodeText`, `drawWaypointCombo` as the pattern),
`ActorRender` (the actor's `Asset` field). No new dependency.

**Storage**: N/A (nothing persisted)

**Testing**: xUnit, headless (`dotnet test`); the fabricated-`ActorAsset` fixture (`test/ActorNameTests.fs`,
`test/CueTests.fs`); no device.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: none new — the actor combo is a per-frame scan of the viewed zone's actor slot map,
cold (the editor is open and the play chain is stopped), the waypoint combo's cost already.

**Constraints**: the build is warning-free; the suite is green; no device opened; the roster/binding stay for
portraits.

**Scale/Scope**: 4 source files (`world/Cue.fs`, `world/CueEngine.fs`, `state/GameplayState.fs`,
`editor/CueEditor.fs`), 1 editor file (`editor/CueTree.fs`), ~4 test files; no `.fsproj` change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. It removes a dependency (`Roster`/`Casting` from cues), a write helper
  (`withLook`), and the roster-combo plumbing; it adds one combo (`drawActorCombo`) and one write (`withAsset`),
  both mirroring the waypoint chooser.
- **II. Fail Loudly** — PASS. The editor's combo offers only names that resolve, so the common mistake is
  unrepresentable; the cue layer keeps its no-op rule for state it cannot act on (`specs/064` D7, `specs/072`).
- **III. Spec-First** — PASS. This feature is `specs/073-cue-actor-by-name`.
- **IV. Data-Oriented Architecture** — PASS. The op carries a string; resolution is the existing slot-map scan.
  No new heap structure; the editor scans are cold.
- **V. Minimise GC Pressure** — PASS. No hot-path change; the combo builds a small `ResizeArray<string>` per
  draw, as `drawWaypointCombo` already does.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/073-cue-actor-by-name/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── cue-actor-by-name.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Cue.fs              # actor ops take `actor: string`; AddActor takes `asset: string` (no look)
world/CueEngine.fs        # addActor names the asset; removeActor/tryResolveActor/noOp take a string;
                          #   the motion/blend State cases carry a string
state/GameplayState.fs    # tryActorByName takes a string
editor/CueTree.fs         # op patterns become strings; withCharacter→withActor; drop withLook; drop the
                          #   actor/look combos from drawEditor (keep the place combo for AddActor)
editor/CueEditor.fs       # add drawActorCombo + drawAssetCombo; drawNodeText draws the actor combo (viewed
                          #   place's actors) and, for AddActor, the asset combo + typed name; clip/morph
                          #   names from the placed actor's asset (not the binding)
test/ActorNameTests.fs    # resolution by string
test/CueTests.fs          # actor ops by string; AddActor names an asset
test/CuePanelTests.fs     # labels/kinds/writes under the new shapes
test/EntityTests.fs       # (only if it names an actor op — it does not; leave unless it breaks)
docs/ARCHITECTURE.md      # the cue-actor-op passage
docs/ROADMAP.md           # the phase-10 cast passage
```

**Structure Decision**: No module moves and no compile-order change. `world/Cue.fs` loses its last reference
to `Roster`/`Casting`; the editor split is unchanged in structure — closed-set combos stay in
`CueTree.drawEditor`, scene-dependent controls stay in `CueEditor.drawNodeText` — only the actor chooser moves
between them. `Assets` (the asset manager) is already read by `CueEditor` (through `shared.Assets`), so the
asset combo needs no new plumbing.

## Complexity Tracking

> No Constitution Check violations.
