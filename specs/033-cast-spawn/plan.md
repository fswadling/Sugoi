# Implementation Plan: Spawning a Cast Member from the Cast Panel

**Branch**: `033-cast-spawn` | **Date**: 2026-09-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/033-cast-spawn/spec.md`

## Summary

Each slot row in the Cast panel gains a **Spawn** control beside its combo. Activating it creates
one actor in the active zone through the editor's single entity-creation surface, drawn with the
asset that slot is bound to and named with the member's case name. A slot with nothing assigned
draws the same control **disabled**, so the affordance is visible where it will become usable.

Two consequences fall out of that, and both are corrections rather than additions:

- **The panel gains the gameplay state.** `Spawning.spawnActor` takes `shared` *and* `state` — it
  pairs the create with the command that reverses it and selects the new entity, which is why no
  panel may call `Zone.spawnActor` directly. So `CastPanel.draw` becomes the same shape as its
  siblings, and `specs/032-cast-binding`'s contract and data model stop being true on that one line.
- **`baseEntity` moves to `Spawning`.** Both editor panels build the same entity for a spawned
  asset, and this feature makes that the third call site (`AssetBrowser`'s actor and prop, and
  now the cast panel's actor), so the literal moves to the module that already owns "the editor
  creates entities". That is also how FR-007 is enforced rather than reviewed: one literal means
  the two spawn paths cannot drift, because there is only one.

No new ImGui binding is needed: `igSameLine`, `igSmallButton` and `igBeginDisabled`/`igEndDisabled`
are all already bound, and the row's widgets are unique already because the member's name is pushed
onto the ID stack around them.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: none new. `render/ImGui.fs` already binds every widget the control needs —
verified by reading the file rather than assumed.

**Storage**: N/A. No persisted state changes: the binding is still session state, and a spawned
actor is zone contents like any other.

**Testing**: **no new headless test**, deliberately. What this feature decides — which asset a slot
spawns, and what the actor is called — is `Casting.tryAsset` and `Roster.characterName`, both
already covered, and the spawn itself is `Zone.spawnActor` + `ActorCreate`, whose undo is already
covered by `test/SpawningTests.fs`. What is left is ImGui glue that no headless test can reach, so
the validation is the interactive walk in `quickstart.md`, plus the existing suite passing
unmodified.

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS. The panel draws in edit mode only, and the control adds one
button and one dictionary lookup per slot row per frame — eight rows today. No play-mode path is
touched.

**Constraints**: creation must go through `editor/Spawning.fs` (undo and selection come from there,
and a direct `Zone.spawn*` would break recorded refs); the binding must not be written (SC-004); the
created actor must carry no cast identity (FR-009), so `ActorState` and `Command` are untouched;
every existing behaviour must be unchanged (FR-008).

**Scale/Scope**: one control in `editor/CastPanel.fs`; `baseEntity` moves from `editor/AssetBrowser.fs`
to `editor/Spawning.fs`; one call site in `editor/Editor.fs`; two one-line corrections in
`specs/032-cast-binding`; one sentence in `docs/EDITOR_DESIGN.md` D-022. No new module, no new
dependency, no new command, no `.fsproj` change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one control, and one literal *moved* to its third call site rather than copied to a second. No new module, binding, command, or test file. |
| II. Fail Loudly | Pass — an unassigned slot is an expected absence (`tryAsset` answers `None`), so it renders a disabled control rather than inventing a default model; the spawn itself raises through the existing path if the asset id were somehow wrong. |
| III. Spec-First | Pass — `specs/033-cast-spawn/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — nothing is added to any data type. A spawned actor is an ordinary `Actor`, and `ActorState` gains no field. |
| V. Minimise GC Pressure | Pass — no play-mode path changes; the control is editor UI, which already allocates its labels. |
| N-V. Testing is the Authoring Tool | Pass, and served — the spawn path this reuses is already covered headlessly, including its undo, so the new code is UI glue over tested parts rather than untested behaviour. |

## Complexity Tracking

None. The only structural change is moving a seven-line entity literal to the module that owns
entity creation, at its third call site — which the rule of three already calls for, and which
FR-007 asks for on its own grounds.

## Project Structure

### Documentation (this feature)

```text
specs/033-cast-spawn/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── cast-spawn.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
editor/
├── CastPanel.fs         # + the Spawn control per slot row, disabled while the slot is
│                        # unassigned; `draw` gains the gameplay state so the spawn can route
│                        # through `Spawning`; the member's name is already pushed onto the ID
│                        # stack, which is what keeps several `Spawn` buttons distinct.
├── Spawning.fs          # + `baseEntity`, public: the entity a spawned asset becomes. Module
│                        # comment already says this is the single place entities are created.
├── AssetBrowser.fs      # its private `baseEntity` is deleted in favour of `Spawning`'s; its two
│                        # call sites are otherwise unchanged.
└── Editor.fs            # `CastPanel.draw shared &state`.

docs/
└── EDITOR_DESIGN.md     # D-022 gains its second job: a bound slot also places its actor, and why
                         # that is undoable while assigning is not.

specs/032-cast-binding/
├── contracts/cast-binding.md   # the one line that says the panel takes `shared` alone.
└── data-model.md               # the same claim, and the paragraph that reasons from it.
```

**Structure Decision**: no new file. The control lives in the panel that owns the slots, and the
shared entity construction lives in `Spawning` — the module whose own comment already claims "the
single place the editor creates entities", so a second literal elsewhere would have contradicted it.
`ActorState`, `Command`, `Zone` and `Casting` are untouched: this feature adds no data and no
command, it adds a caller of the ones that exist.

## Phase 0: Research

See [research.md](./research.md). It settles where the control sits and what it does when the slot
is empty, why the panel takes the gameplay state, where `baseEntity` lives, which widgets the
control needs (none new), what is deliberately not tested, and that the actor carries no cast
identity.

## Phase 1: Design

- [data-model.md](./data-model.md) — the row's spawn decision, what the created actor is, and what
  is deliberately unchanged.
- [contracts/cast-spawn.md](./contracts/cast-spawn.md) — the surfaces that change (`CastPanel`,
  `Spawning`, `AssetBrowser`, `Editor`) and the two `specs/032-cast-binding` corrections.
- [quickstart.md](./quickstart.md) — runnable validation scenarios.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: one control, one literal moved to its third call site, no new module, dependency, command, or test file. |
| II. Fail Loudly | Pass — an empty slot is a disabled control, not a default model. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — no data type changes; a spawned actor is an ordinary `Actor`. |
| V. Minimise GC | Pass — no play-mode path changes. |
