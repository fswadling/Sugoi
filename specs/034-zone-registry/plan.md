# Implementation Plan: The Zone Registry

**Branch**: `034-zone-registry` | **Date**: 2026-09-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/034-zone-registry/spec.md`

## Summary

The game gains a closed vocabulary of **places** — a zone and one of the variants that zone declares — and
a **registry** mapping a place to the zone registered there: the environment asset, the environment's
own settings, and its parts as the author left them. `world/ZoneRegistry.fs` holds the vocabulary and
the sparse map; `world/ZoneTemplate.fs` holds the record of one registered zone; `editor/ZoneRegistryPanel.fs`
is the panel, docked beside the Outliner and Cast, where each place offers **Register** (file the
active zone's loaded environment against that place) and **Instantiate** (put it back).

Four things about the shape are decisions rather than consequences, and each is argued in
`research.md`:

- **A zone's variants are its own union, and a place is a value from it.** The vocabulary is a union
  whose cases carry a further union each — `ArtRoom of Period`, and `Island` carrying nothing — so the
  association between a zone and its variants is in the type, two zones declare unrelated sets, and a
  place is an ordinary key (`Place.ArtRoom Period.Morning`). Reflection walks the cases and then
  each case's field type to build the panel's tree, so the panel knows nothing about which zones
  exist.
- **A registration is a copy built from `PropState`**, the authored value the command system already
  captures for a part (`Zone.capturePart`) and already carries in `PartCreate`/`PartDelete`. Reusing
  it means a registered part is not a fourth spelling of the same four fields, and it is what lets an
  entry represent a part the author **deleted** as well as one she moved or renamed.
- **Registering writes the registry directly; instantiating is an ordinary scene edit.** The registry
  outlives every zone and the undo stacks are per zone, so a registration parked in one zone's stack
  would be unreachable from every other and discarded when that zone is deleted — the same reasoning
  that made the cast binding a direct write (D-022). Instantiating, by contrast, replaces the active
  zone's environment through the command that already exists for exactly that
  (`SetEnvironment of Previous * Next`, carrying whole environments), so it is undoable for free.
- **The instantiate path duplicates the Asset Browser's swap rather than extracting it.**
  `editor/Spawning.fs` is the natural home for "pair a mutation with the command that reverses it",
  but it is compiled *before* `editor/Selection.fs`, and the swap must drop a stale part selection.
  This is the second call site; the rule of three says the third extracts. Recorded in `research.md`
  Decision 7 so that the third does.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: none new. The panel needs `text`, `sameLine`, `smallButton`,
`beginDisabled`/`endDisabled`, `collapsingHeader` and `pushId`/`popId`, all already bound in
`render/ImGui.fs` — verified by reading the binding list rather than assumed. The one native change is
the dock layout: `DockBuilderDockWindow("Zone Registry", ImGuiDir_Left)` in
`native/cimgui_sdl3.cpp`, plus a `libcimgui.so` rebuild, exactly as the Cast panel's docking was
added. (`native/libcimgui.so` is a gitignored build artifact; only the `.cpp` is tracked.)

**Storage**: N/A, deliberately. The registry lives in memory for the session; persisting it with the
rest of the project is the persistence phase's work (`docs/ROADMAP.md` phase 15), and this feature
decides no file format.

**Testing**: two new headless test files, and unlike `specs/033-cast-spawn` there is no argument for
skipping them, because the interesting half of this feature is data rather than ImGui glue:

- `test/ZoneRegistryTests.fs` — the vocabulary as reflection derives it, and the sparse map's
  contract (an absent place answers `None`, replacing replaces, two places are independent).
- the **round trip** in `test/ZoneTests.fs`, which is the feature's central claim and is reachable
  headlessly for the same reason `test/SpawningTests.fs` is: a null `Gpu.Device` is never dereferenced
  by `EnvironmentRender.create`, so a room asset can be built by hand, loaded with
  `Zone.loadEnvironment`, its parts moved, renamed and deleted, captured with
  `ZoneTemplate.ofEnvironment`, and loaded into a fresh zone with `Zone.loadEnvironmentFrom` — then
  the parts compared. That single test is what makes FR-012 a checked claim rather than a promise,
  and it extends `test/ZoneTests.fs` rather than the registry's own file because that file's fixture
  already drives the real load path: a third copy of it is what the rule of three refuses.

What no test reaches is the six lines of ImGui glue in the panel plus the native dock line; those are
`quickstart.md`'s interactive scenarios.

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS. The panel draws in edit mode only, and `ZoneRegistry.byZone` is
computed once at type initialisation, so a frame costs one dictionary lookup per row plus two buttons
— four rows today. No play-mode path is touched, and neither `Zone.loadEnvironment*` nor the panel
runs per frame.

**Constraints**: registration must not be undoable and must not be zone-scoped (FR-009, FR-010);
instantiating must be one step that restores what was there (FR-013); the registry must not record
props, waypoints or triggers (FR-015); every existing behaviour must be unchanged (FR-016), which
includes the Asset Browser's environment load, whose behaviour is identical but whose five-line body
is duplicated by the new panel's swap rather than shared (`research.md` Decision 7).

**Scale/Scope**: two new `world/` files, one new `editor/` panel, one new test file, one new
`SharedState` field (four construction sites: production and three fixtures), a parameterised private
builder in `world/Zone.fs` plus `Zone.loadEnvironmentFrom`, one line in `editor/Editor.fs`'s panel
list and one in its `draw`, one native line, and the docs the feature falsifies. No new command, no
new entity kind, no new `.fsproj` compile cluster, no new dependency.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — no new command, entity kind or dependency; the registry is a `Dictionary` behind one lookup, the record is built out of types that already exist (`Entity`, `PropState`), and the panel's controls are the same two widgets the Cast panel already uses. Two new `world/` files are the minimum the compile order allows (see Structure Decision). |
| II. Fail Loudly | Pass — an absent place is an *expected* absence (`option`, not a sentinel); a registration whose asset is not loaded, a place whose asset is missing from the manager, and a vocabulary case that is neither fieldless nor carries a single union all `failwith` rather than defaulting. A re-exported asset whose submesh range has moved is caught by `EnvironmentBatch.gather`'s existing range check. |
| III. Spec-First | Pass — `specs/034-zone-registry/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — a union of values as the key rather than a string, a `[<Struct>]`-shaped `PropState array` for the parts (contiguous, no per-part object), and one `Dictionary` for the map. `Environment` is untouched, so the hot paths over `Parts` are unchanged. |
| V. Minimise GC Pressure | Pass — nothing is added to a per-frame path. The panel's reflection runs once at type initialisation; the per-frame cost is dictionary lookups and labels, in edit mode only. |
| N-V. Testing is the Authoring Tool | Pass, and served — the round trip is driven headlessly through the real `Zone` load path, which is the check that would have been missing had this feature followed 033's "ImGui glue only" argument. |

## Complexity Tracking

None. The feature adds no abstraction: one union of authored values, one record, one dictionary, one
lookup, and a panel that draws two controls per row. The only structural change to existing code is
parameterising `Zone`'s private environment builder so that an asset's part list and a registered
record's part list take the same path to the same activation.

## Project Structure

### Documentation (this feature)

```text
specs/034-zone-registry/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── zone-registry.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── ZoneTemplate.fs      # NEW: the record of one registered zone — the asset, the environment's
│                        # settings, and its parts as authored values — plus `ofEnvironment`, the
│                        # copy taken from a loaded environment. Compiled before `Zone.fs`, because
│                        # the loader takes one as its argument.
├── Zone.fs              # CHANGED: `buildEnvironment` takes the base, the orientation and the part
│                        # states rather than reading them all from the asset, so an asset's own
│                        # parts and a registered record's parts share one activation path; `+`
│                        # `loadEnvironmentFrom`, which mirrors `loadEnvironment` exactly. The parts
│                        # map's capacity becomes `max 512 parts.Length`, which also removes a
│                        # latent failure for an asset with more than 512 parts.
└── ZoneRegistry.fs      # NEW: the places — the union, each case carrying its own variant union —
│                        # plus the reflection that derives the panel's tree, and the sparse map
│                        # from a place to its record.
editor/
├── ZoneRegistryPanel.fs # NEW: the panel. A node per zone, a row per place, and two controls: the
│                        # Register control (disabled while the active zone has no environment) and
│                        # the Instantiate control (disabled while the place is unregistered).
└── Editor.fs            # CHANGED: the panel joins `panelNames` (so Ctrl+Tab reaches it) and is
                         # drawn alongside its siblings.
state/
└── SharedState.fs       # CHANGED: `+ ZoneRegistry : ZoneRegistry.Registry`. Project state, like
                         # the cast binding, and for the same reason: it must outlive every zone.
test/
├── ZoneRegistryTests.fs # NEW: the vocabulary as reflection derives it, and the map's contract.
└── ZoneTests.fs         # EXTENDED: the round trip — a registered zone loaded back part for part.
native/
└── cimgui_sdl3.cpp      # CHANGED: one line, docking the panel in the left node.
docs/
├── EDITOR_DESIGN.md     # D-023 for the panel; D-011's line that names the zone registry among the
│                        # surfaces wanting a project-scoped stack is corrected to record why it has
│                        # none.
├── PROJECT_STRUCTURE.md # the compile list and the `world/` layout gain the two new files.
├── ROADMAP.md           # phase 12's deliverables and "done when".
└── ARCHITECTURE.md      # the `Zone_Registry` entry: what an entry holds now, and what is deferred.
fsharp-game.fsproj       # two `<Compile Include>` lines for the world files, one for the panel.
test/fsharp-game.Tests.fsproj  # one line for the new test file.
```

**Structure Decision**: the registry is split into two files by the compile order rather than by
taste. `Zone.fs` must be able to name the record its loader takes, so `ZoneTemplate` sits above it;
the vocabulary and the map need neither `Zone` nor the editor, so `ZoneRegistry` sits below them both
and cannot grow a dependency on the live zone by accident. The panel is its own file for the reason
`editor/CastPanel.fs` is: a dockable sub-editor is a surface, and it is compiled after
`editor/Selection.fs` because the instantiate path reconciles a stale part selection.

The vocabulary is in the same file as the map, rather than beside it as `cast/Roster.fs` is beside
`world/Casting.fs`, because nothing shares it: the cast's roster was hoisted into a file of its own
so that the narrative engine and the game could name one declaration, and no other cluster needs to
name a place. `research.md` Decision 8 records this and the trigger for revisiting it.

## Phase 0: Research

See [research.md](./research.md). It settles how a zone declares its variants and why the variants are
the case's payload rather than a shared union, why names are derived rather than matched (departing
from `Casting.lookName`), what a record's parts are made of, where the registry lives and what its
write looks like, how the load path is shared, why the swap is duplicated rather than extracted, why
the vocabulary and the map share a file, the ID stack the panel must push, the native docking line,
and what is deliberately deferred.

## Phase 1: Design

- [data-model.md](./data-model.md) — the vocabulary, the record, the map, and what is deliberately
  unchanged.
- [contracts/zone-registry.md](./contracts/zone-registry.md) — the surfaces that change or appear:
  `ZoneRegistry`, `ZoneTemplate`, `Zone`, `SharedState`, `ZoneRegistryPanel`, `Editor`, the native
  dock layout.
- [quickstart.md](./quickstart.md) — runnable validation scenarios, headless and interactive.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: no new command, entity kind, dependency, or cluster; the panel's controls are widgets the Cast panel already uses, and the one native change is a line the Cast panel's docking already established. |
| II. Fail Loudly | Pass — every absent case is an `option`, every broken invariant raises, and the one failure mode the design cannot prevent (a re-exported asset) is already a loud range check in the batch. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — `Environment`, `Prop`, `Zone` and the slot maps are untouched; the registry is a dictionary read once per row per frame, in edit mode. |
| V. Minimise GC | Pass — no per-frame path gains an allocation; `byZone` is computed once. |
| N-V. Testing is the Authoring Tool | Pass — the round trip is asserted headlessly through the real load path, and the vocabulary's derivation is asserted rather than eyeballed. |
