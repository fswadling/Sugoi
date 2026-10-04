---

description: "Task list for Actor Cue Ops"
---

# Tasks: Actor Cue Ops

**Input**: Design documents from `/specs/064-actor-cue-ops/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/actor-cue-ops.md, quickstart.md

**Tests**: Included — the project's convention (and SC-006) require the suite green, and each op is a behaviour
the suite can assert headlessly with no device (a fabricated `ActorAsset`, the `specs/063` fixture).

**Organization**: Tasks are grouped by user story (US1 P1, US2 P2, US3 P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish carry none)

## Path Conventions

- Single F# project at the repository root; sources in `world/`, `state/`, `editor/`, tests in `test/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The shared new-entity constructor the cue's spawn needs, without a third copy of the literal.

- [X] T001 `world/Entity.fs`: add `baseEntity (name: string) (transform: Transform) : Entity` — `Name`, the
  transform, and the four editor flags (`Enabled`/`Visible`/`Selectable`/`Draggable` all true). This is the
  literal `editor/Spawning.fs`'s `baseEntity` and `editor/Outliner.fs`'s `newEntity` already each spell.
- [X] T002 `editor/Spawning.fs` + `editor/Outliner.fs`: delegate to `Entity.baseEntity` —
  `Spawning.baseEntity name = Entity.baseEntity name transformIdentity`, and `Outliner.newEntity` calls it with
  its transform — so the literal exists once. (depends on T001)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The two ops and every exhaustive match that must account for them; the project builds warning-free
only once these land together, because adding union cases forces every `match` on `Cue` to handle them.

**⚠️ CRITICAL**: No user story can be implemented until this phase is complete.

- [X] T003 `world/Cue.fs`: add the two leaves beside the zone ops —
  `SetActor of place: Place * character: Roster.Character * look: Casting.Look * waypoint: string` and
  `RemoveActor of character: Roster.Character` — and add them to the module's opening note that
  lists the no-progress ops (`Exposit`, `SetZone`, `AddZone`, …).
- [X] T004 `world/CueEngine.fs`: add the `SetActor`/`RemoveActor` arms to `advance` (research D2–D5, D7, D8).
  `SetActor`: resolve the place (`state.Zones`, absent ⇒ nothing) and the waypoint
  (`Zone.tryWaypointByName`, unresolved ⇒ nothing); read `GameplayState.tryCastActor`. No member ⇒ resolve the
  asset with `Casting.tryAsset { Character; Look = look } shared.Cast` (`None` ⇒ nothing), build via
  `Entity.baseEntity (Roster.characterName character) pose`, `Zone.spawnActor zone physics assets entity asset
  (Some character)`, then `GameplayState.addCast`. Member in the named place ⇒ `Zone.setTransform` to the
  waypoint's pose. Member elsewhere ⇒ capture her `ActorState` from the old zone, `Zone.removeActor` there,
  spawn into the named place with the **captured `AssetId`**, then `removeCast`/`addCast`. When the target place
  is not the active one, detach the body just built with `Physics.removeBody` guarded by `Physics.isAdded`.
  `RemoveActor character`: resolve the index and `Zone.removeActor`/`GameplayState.removeCast` where she is —
  the op names no place; a character not in the scene does nothing. Both answer `ValueSome Fin`. (depends on
  T001, T003)
- [X] T005 [P] `editor/CueTree.fs`: add `"Set actor"`/`"Remove actor"` to `kinds`, `kindName` and `label`
  (e.g. `"Set actor: Aiko @ wp"`, `"Remove actor: Aiko"`); add both to the not-executing leaves in `executing`
  and to the no-children arm of `children`; add `defaultOfKind` values
  (`SetActor(Place.start, List.head Roster.characters, List.head Casting.allLooks, "")`,
  `RemoveActor(List.head Roster.characters)`); extend `withPlace` to `SetActor` only; add `withCharacter` (both
  ops) and `withLook` (`SetActor` only); extend `textOf`/`withText` to `SetActor`'s waypoint; and add the
  `drawEditor` arms (place + character + look choosers for the set op, character alone for the remove).
  (depends on T003)
- [X] T006 [P] `editor/CueEditor.fs`: `drawNodeText` offers `SetActor`'s waypoint from the **named place's**
  live waypoints (resolved from the gameplay state, not the active zone) via `drawWaypointCombo`, falling back
  to the typed field when that place is not present. (depends on T003, T005)
- [X] T007 [P] `test/CueTests.fs` and `test/CuePanelTests.fs`: extend the local `describe` helpers and the
  label/`textOf` expectations so the test project compiles with the two new cases. (depends on T003)
- [X] T008 Build checkpoint: `dotnet build fsharp-game.fsproj` and `dotnet build test/fsharp-game.Tests.fsproj`
  compile warning-free with the new cases and matches.

**Checkpoint**: the ops exist, compile and are authorable; the user stories can proceed.

---

## Phase 3: User Story 1 - A cue sets an actor by (place, character) (Priority: P1) 🎯 MVP

**Goal**: A cue brings a member into a named place and places her at a waypoint — one op for a spawn and a
move — keeping her art and the one-character-uniqueness invariant.

**Independent Test**: run a cue carrying a set op for a character with no actor and assert one actor now plays
her, in the named place, at the waypoint's pose, indexed there; run it again and assert still exactly one,
moved.

- [X] T009 [P] [US1] `test/CueTests.fs`: through `CueEngine.tick`, a `SetActor` for an unplaced character
  spawns one actor in the named place at the waypoint's position and facing, records `Cast = Some character`,
  and `GameplayState.tryCastActor` resolves her to that place and ref. Add the fabricated-`ActorAsset` fixture
  (the `test/CastIndexTests.fs` shape) and a resident cast binding `{ Character; Look } -> assetId`.
- [X] T010 [P] [US1] `test/CueTests.fs`: a second `SetActor` naming the same place sets the same actor's
  transform (still one actor, index unchanged); a `SetActor` naming a different **present** place moves her
  across zones — one actor for her across all zones, her `AssetId` unchanged (the op's `look` not consulted),
  and the index pointing at the new place and ref.
- [X] T011 [P] [US1] `test/CueTests.fs`: a `SetActor` and a `RemoveActor` record **no** step on
  `state.Editor.Undo` — actor ops are runtime effects (FR-007) — while the scene still reflects the change.
- [X] T012 [P] [US1] `test/CuePanelTests.fs`: the `"Set actor"` kind renders and `withPlace` / `withCast` /
  `withText` write the place, the character-and-look, and the waypoint on it.

**Checkpoint**: US1 works and is testable independently.

---

## Phase 4: User Story 2 - A cue removes an actor from a place (Priority: P2)

**Goal**: A cue takes a member out of a named place, releasing her and dropping her from the cast index.

**Independent Test**: place a member, run a cue carrying a remove op naming her place, and assert no actor plays
her and the index no longer resolves her; run it again and assert it performs nothing.

- [X] T013 [P] [US2] `test/CueTests.fs`: a `RemoveActor` removes the member's actor wherever she is (the
  zone's actor count drops) and the index no longer resolves her; a second remove, and a remove of a character
  not in the scene, perform nothing.
- [X] T014 [P] [US2] `test/CueTests.fs`: removing the zone's player leaves a designation that no longer
  resolves — `Systems.isPlayerLive zone` is false and nothing raises (FR-011); a same-place `SetActor` on a
  player actor leaves the designation intact.
- [X] T015 [P] [US2] `test/CuePanelTests.fs`: the `"Remove actor"` kind/label renders and `withCharacter`
  writes the character on it (a `withPlace`/`withLook` write on it is a bug).

**Checkpoint**: US1 and US2 both work and are independently testable.

---

## Phase 5: User Story 3 - The actor ops compose with the zone ops and are safe to re-run (Priority: P3)

**Goal**: A redundant or unactionable actor op performs nothing, so a cue is safe to run twice, and the ops
compose with the zone ops into a transition.

**Independent Test**: run a cue that sets a member in an absent place, sets a character the game has no art
for, and names an unknown waypoint — each completes without raising; then assert a composition leaves the
expected state.

- [X] T016 [US3] `test/CueTests.fs`: every unactionable actor op performs nothing and the cue still reaches
  `Fin` — a set in an absent place, a set with no art for the look, a set naming a waypoint the place does not
  hold, and a remove of a member not present (FR-006's re-run safety).
- [X] T017 [US3] `test/CueTests.fs`: the composition `AddZone B → SetActor B Aiko → SetZone B` leaves `B`
  active with Aiko present in it; and `SetActor` followed by `RemoveActor` returns the index to exactly empty
  (SC-003).

**Checkpoint**: all three stories function.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T018 [P] Docs: note the actor lifecycle cue ops under `docs/ROADMAP.md` (phase 10's cast / the scenario
  phases) and the cue section of `docs/ARCHITECTURE.md`; record that movement/locomotion/animation ops remain
  deferred.
- [X] T019 `dotnet build` warning-free and `dotnet test` green (SC-006).
- [X] T020 Run the automated section of `quickstart.md` and confirm the headless outcomes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks** every user story.
- **US1–US3 (Phases 3–5)**: all depend on Foundational; run in priority order (or in parallel if staffed).
- **Polish (Phase 6)**: depends on the stories.

### Within and between stories

- T001 → T002 is the constructor and its two delegations; T003 → T004/T005/T006/T007 is the union change and
  the matches that must account for it.
- T004 is the whole engine behaviour; the stories' tests pin one facet each.
- Test tasks follow the code they cover; US1–US3's tests share `test/CueTests.fs`, so they are not all
  parallelisable.

### Parallel Opportunities

- T005 (CueTree), T006 (CueEditor) and T007 (tests) touch distinct files once T003 lands.
- T009–T015 touch a few shared files; run them in priority order, parallelising only where the files differ
  (`CueTests` vs `CuePanelTests`).
- T018 is independent of the code.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (the ops exist, compile and are authorable).
2. US1 (set: spawn and move, cross-zone included).
3. **STOP and VALIDATE**: a cue can place a member and re-place her, with one actor and an exact index.
4. US2 (remove), then US3 (no-op safety and composition), then polish.

### Incremental notes

- The Foundational phase is deliberately one landing: the union gains two cases and every exhaustive match
  must account for them, so it does not half-compile.
- The ops' behaviour is small; the bulk of the work is the tests that pin the three set cases (spawn /
  same-place move / cross-zone move), the remove, and the no-op rule.

## Notes

- `[P]` = different files, no dependencies.
- Actor cue changes are runtime effects, never steps on the undo history (FR-007).
- The op acts on the **named** place, which may be inactive; a body built there is detached per-body
  (`Physics.removeBody`, research D5). The active place is never changed by an actor op.
- A cue op is forgiving: a redundant or unactionable actor op performs nothing, while the editor's own
  functions keep raising (research D7).
- No `.fsproj` change: no module is added or moved.
