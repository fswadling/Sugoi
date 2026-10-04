---

description: "Task list for Actor Motion & Animation Cue Ops"
---

# Tasks: Actor Motion & Animation Cue Ops

**Input**: Design documents from `/specs/066-actor-motion-cue-ops/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/actor-motion-cue-ops.md, quickstart.md

**Tests**: Included — the project's convention (and SC-005) require the suite green, and each op is a behaviour
the suite can assert headlessly with no device (the fabricated `ActorAsset` fixture, extended with clips and a
morph).

**Organization**: Tasks are grouped by user story (US1 P1 blends, US2 P2 movement, US3 P3 the player toggle). Adding a
`Cue` case forces every exhaustive `match` on `Cue` to account for it, so each story bundles its union cases
with the match updates that keep the build warning-free.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (foundational and polish carry none)

## Path Conventions

- Single F# project at the repository root; sources in `world/`, `editor/`, tests in `test/`.

---

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: The readers the blends need, and the test asset that can play clips/morphs.

**⚠️ CRITICAL**: No user story can be implemented until this phase is complete.

- [X] T001 [P] `world/ActorRender.fs`: add `tryClipWeight (r: byref<ActorRender>) (name: string) : float32 option`
  and `tryMorphWeight` — the named slot's current weight, or `None` when the asset does not define it (a named
  slot the actor does not hold reads 0; scanning `AnimationSlots.Slots` / `MorphSlots.Slots`), so a blend can
  capture its start and detect a name the asset lacks.
- [X] T002 [P] `test/CueTests.fs`: extend the fabricated `ActorAsset` fixture (the `actorShared` / `emptyModel`
  shape) with a model that defines at least two animation clips (`Idle`, `Jog`) and one morph, so blends and
  `MoveActor` can resolve names headlessly.
- [X] T003 Build checkpoint: `dotnet build fsharp-game.fsproj` and `dotnet build test/fsharp-game.Tests.fsproj`
  compile with the readers and the richer fixture.

**Checkpoint**: the readers exist; the stories can proceed.

---

## Phase 2: User Story 1 - A cue blends an actor's animation or morph (Priority: P1) 🎯 MVP

**Goal**: a cue fades a slot's weight over a duration (`Animate`, `CrossFade`, `Morph`, `CrossMorph`), reaching
its target exactly at the deadline.

**Independent Test**: run an `Animate` and assert the clip's weight moves from where it started to the target
and lands exactly; run a `CrossFade` and assert one weight falls as the other rises; both no-op on a missing
actor or a name the asset lacks.

- [X] T004 `world/Cue.fs`: add the four blend ops (`Animate`, `CrossFade`, `Morph`, `CrossMorph`) and their
  shared in-flight `BlendState` (research D2, data-model.md); add them to the module's no-progress/`…State` note.
- [X] T005 `world/CueEngine.fs`: add the arms and a `beginBlend`/`stepBlend` pair — capture `initialFrom`/
  `initialTo` via the T001 readers, guard a name the asset lacks (no-op) and a missing actor (no-op), hold a
  `BlendState`, lerp each tick, and land the exact endpoints at the deadline. (depends on T001, T004)
- [X] T006 [P] `editor/CueTree.fs`: add the blend ops to `kinds` (a new **Animation** group), `kindName`,
  `label`, `executing` (the `BlendState` case), `children` (no children), `defaultOfKind`, `withCharacter`,
  `textOf`/`withText` (the first name) and `textOf2`/`withText2` (the second, for the cross-fades), and the
  `drawEditor` arms (a character chooser + a `dragNumber` for the weight and duration); `CueEditor.drawNodeText`
  offers the clip/morph names as combos over the character's asset. (depends on T004)
- [X] T007 [P] `test/CueTests.fs` and `test/CuePanelTests.fs`: extend the local `describe` helpers for the new
  cases so the test project compiles. (depends on T004)
- [X] T008 [US1] `test/CueTests.fs`: through `CueEngine.tick`, an `Animate` moves a clip's weight from where it
  started to the target, reaching it exactly at the deadline; a `CrossFade` fades `from` out as `to` comes in;
  a `Morph`/`CrossMorph` blends a morph slot; an unknown clip/morph name and a missing actor perform nothing;
  and the op records **no** step on `state.Editor.Undo` (FR-006).
- [X] T009 [P] [US1] `test/CuePanelTests.fs`: the blend kinds and labels render, and the writes
  (`withCharacter`, `withText`) replace the character and the name.

**Checkpoint**: US1 works and is testable independently.

---

## Phase 3: User Story 2 - A cue moves an actor along a path, or turns her in place (Priority: P2)

**Goal**: `MoveActor` physics-drives her body along a waypoint chain, turning from her own facing to the last
waypoint's arrow and blending moving→idle; `RotateActor` turns her in place to face a waypoint. Both are
active-zone only and end at their deadline.

**Independent Test**: place a waypoint chain in the actor's (active) zone, run a `MoveActor` with the physics
stepped, and assert the body follows the spline and arrives at the last waypoint; run a `RotateActor` and assert
the yaw reaches the waypoint's; both no-op on an unresolved path, a missing actor, or an actor not in the active
zone.

- [X] T010 `world/Cue.fs`: add `MoveActor` / `MoveActorState` and `RotateActor` / `RotateActorState`
  (data-model.md).
- [X] T011 `world/CueEngine.fs`: add the arms and a begin/step for each — resolve the actor's zone through the
  index and require it to be the **active** zone (motion is active-zone only, else no-op); resolve the path via
  `Zone.waypointChain` (a single waypoint is a straight walk to it; positive speed, else no-op); build
  `points`, capture her `initialRotation` and the path's `finalRotation`, keep `speed`, and
  compute `duration` (`Maths.pathLength`); each tick **physics-drive** the body (horizontal velocity toward the
  spline point at `speed`, vertical preserved, plus facing; research D3) and blend the clips; at the deadline
  arrive at the endpoint when unobstructed, else leave her where the collision stopped her. (depends on T005,
  T010)
- [X] T012 [P] `editor/CueTree.fs` and `editor/CueEditor.fs`: add the movement ops (the **Actors** group) to
  `kinds`, `kindName`, `label`, `executing` (the `…State` cases), `children`, `defaultOfKind`, `withCharacter`,
  `withWaypoint` (the path / waypoint), `textOf`/`withText` (a move's moving clip) and `textOf2`/`withText2`
  (its idle clip), and the CueTree `drawEditor` arms (character chooser + number drags for `speed`/`duration`);
  in `CueEditor.drawNodeText`, offer `MoveActor`'s path and `RotateActor`'s waypoint from the **active zone's**
  waypoints (`drawWaypointCombo`, as the camera ops) and `MoveActor`'s moving/idle clips from the character's
  asset's names (combos, typed when the asset does not resolve). (depends on T010)
- [X] T013 [P] `test/CueTests.fs` and `test/CuePanelTests.fs`: extend the `describe` helpers for the new cases.
  (depends on T010)
- [X] T014 [US2] `test/CueTests.fs`: through `CueEngine.tick` with the physics world stepped, a `MoveActor`
  drives the actor's body along the spline from her current position and arrives at the last waypoint facing its
  orientation with the idle clip at full weight when unobstructed; a `RotateActor` reaches the waypoint's yaw; an
  unresolved path, a path with no length, a missing actor, and an actor whose zone is **not** the active one
  perform nothing; and each op records **no** step on `state.Editor.Undo` (FR-006).
- [X] T015 [P] [US2] `test/CuePanelTests.fs`: the movement kinds and labels render, and the writes replace the
  character and the path/waypoint.

**Checkpoint**: US1 and US2 both work and are independently testable.

---

## Phase 4: User Story 3 - A cue binds or unbinds an actor as the player (Priority: P3)

**Goal**: `EnablePlayer` makes the named actor the zone's player (input drives her, the camera follows);
`DisablePlayer` clears it.

**Independent Test**: run `EnablePlayer` on a placed actor and assert the zone's player resolves to her;
run `DisablePlayer` and assert she no longer is; both no-op on a missing actor.

- [X] T016 `world/Cue.fs`: add `EnablePlayer` / `DisablePlayer`.
- [X] T017 `world/CueEngine.fs`: add the arms — resolve the actor, set `zone.Player <- ValueSome
  (Systems.playerHandleOf ref)`, or clear it when she is the player; no-op otherwise (research D4). (depends
  on T005, T016)
- [X] T018 [P] `editor/CueTree.fs`: add the player ops (the **Actors** group) to `kinds`, `kindName`,
  `label`, `executing`, `children`, `defaultOfKind`, `withCharacter`, and the `drawEditor` arm (a character
  chooser only). (depends on T016)
- [X] T019 [P] `test/CueTests.fs` and `test/CuePanelTests.fs`: extend the `describe` helpers for the new cases.
  (depends on T016)
- [X] T020 [US3] `test/CueTests.fs`: through `CueEngine.tick`, `EnablePlayer` makes the actor the
  zone's player (`Systems.isPlayerLive`), `DisablePlayer` clears it, both a disable when she is not
  the player and an op on a missing actor perform nothing, and the op records **no** step on
  `state.Editor.Undo` (FR-006).
- [X] T021 [P] [US3] `test/CuePanelTests.fs`: the player kinds and labels render, and `withCharacter`
  replaces the character.

**Checkpoint**: all three stories function.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T022 [P] Docs: note the actor motion/animation cue ops under `docs/ROADMAP.md` and the cue section of
  `docs/ARCHITECTURE.md`; record that only the non-actor toggles and `Print` remain.
- [X] T023 `dotnet build` warning-free and `dotnet test` green (SC-005).
- [X] T024 Run the automated section of `quickstart.md` and confirm the headless outcomes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: no dependencies; **blocks** the user stories.
- **US1–US3 (Phases 2–4)**: depend on Foundational; run in priority order.
- **Polish (Phase 5)**: depends on the stories.

### Within and between stories

- T001 → T005; T004 → T005/T006/T007; T010 → T011/T012/T013; T016 → T017/T018/T019.
- T011 depends on T005 (the blend helpers `MoveActor` reuses).
- Test tasks follow the code they cover; the `describe` updates and the story tests share files, so they are not
  all parallelisable.

### Parallel Opportunities

- T001, T002 (distinct files) then T003.
- Within a story: the `CueTree` arm task, the test-`describe` task and the story's own test task touch distinct
  files once the `Cue` cases land.
- T022 is independent of the code.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (the readers + the fixture).
2. US1 (the blends).
3. **STOP and VALIDATE**: a cue can fade a clip/morph and lands on its target.
4. US2 (movement), then US3 (the player toggle), then polish.

### Incremental notes

- Each story lands its `Cue` cases together with the matches that must account for them, so the build stays
  warning-free at every checkpoint.
- The bulk of the work is US2 (the physics-driven walk); US1 and US3 are thin.

## Notes

- `[P]` = different files, no dependencies.
- Actor ops are runtime effects, never steps on the undo history (FR-006).
- A cue op is forgiving: a missing actor, an unresolved path, or a name the asset lacks performs nothing
  (FR-005); the editor's `ActorRender` setters still raise.
- Motion is **physics-driven and active-zone only** (research D3); a blend acts in the actor's own zone whether or
  not it is active.
- No `.fsproj` change: no module is added or moved.

---

## Phase 6: Convergence

- [X] T025 Log a no-op reason to stderr for `EnablePlayer`/`DisablePlayer` (a missing actor, or a disable when she is not the player) through `CueEngine.noOp`, as FR-005 requires of every no-op, per FR-004/FR-005 (`partial`).
