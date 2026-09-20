# Implementation Plan: The Cast — a Roster Hoisted from the Engine and Bound to Actor Assets

**Branch**: `032-cast-binding` | **Date**: 2026-09-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/032-cast-binding/spec.md`

## Summary

Move the roster out of the narrative engine into a **shared layer beneath it** — a new
`cast/` cluster holding `Roster.fs` (`Character`, `characterName`, `characters`) — and give
the game a **binding from a slot to an actor asset**. The engine consumes the hoisted roster
exactly as it consumed its own, and keeps everything else it owns: its constructs, its traits,
its rules.

Identity and appearance are two types, not one. A roster case stays **fieldless** — it is
*who*, and an engine fact about a character must not carry her clothes. A **look** is the
game's vocabulary of variants, shared and closed: `SchoolUniform | Casual`. A **slot** pairs
the two, and the binding is a sparse map from slot to actor asset id, where an entry's
existence *is* the check that a character has art for that look. Not every member has every
look; nothing declares otherwise, and the panel shows the whole cross product with the
unassigned leaves reading as "no model here".

The editor gains a fourth docked panel, `CastPanel`, presenting the cast as a tree: a member
per collapsing header, a look per row beneath her, and a combo of discovered actor assets per
row. The binding lives on `SharedState` beside `Assets`, because it is session-scoped
authoring state like the loaded assets, and outside every zone's `UndoStack`, because it
outlives any one zone — the same call the player designation got.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: none new. `System.Collections.Generic` for the sparse map,
`Microsoft.FSharp.Reflection` for enumerating the look vocabulary — the idiom
`Vocabulary.characters` already establishes for a closed union. The panel reuses the existing
ImGui binding and `native/libcimgui.so`; five widget exports are added to `render/ImGui.fs`
(all present in the shipped library, verified with `nm -D`).

**Storage**: N/A — the binding is in-memory session state, as loaded assets are. Persistence is
`docs/ROADMAP.md` phase 15 and is out of scope; a slot is expressible as a member and a look,
so a later feature can store one without re-modelling it.

**Testing**: xUnit via `dotnet test test/fsharp-game.Tests.fsproj`. Two new headless modules —
`RosterTests` (the enumeration and the render match agree over the cases) and `CastingTests`
(assign, resolve, clear, absent, shared asset, and that one slot's binding does not leak into
another). The narrative suite continues to run headless and unmodified in behaviour; only its
references to the moved roster change.

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS. The panel is editor UI and never runs in play mode. Both
enumerations it walks — `Roster.characters` and `Casting.allLooks` — are module-initialised
values, so drawing the panel adds no per-frame reflection and no per-frame roster allocation;
the only per-frame allocation is the panel's own labels, which is what the existing Inspector
already does.

**Constraints**: the engine must not become able to reference the game (FR-004) — this is why
the roster does not go into `world/`; no new package reference; only discovered actor assets
are assignable, so a slot cannot name a missing asset (FR-008); the binding is outside every
undo stack (FR-011), because the stacks are per zone (`GameplayState.UndoByZone`) and the
binding outlives a zone; the engine's existing facts, rules and history are byte-for-byte
unaffected (FR-012). The one part of this feature that touches native code — the dock line —
requires rebuilding `native/libcimgui.so`, and nothing else depends on it.

**Scale/Scope**: four roster members × two looks = eight slots, all assignable. Three source
files land (`cast/Roster.fs`, `world/Casting.fs`, `editor/CastPanel.fs`), two test files land,
`narrative/Vocabulary.fs` loses three members, `state/SharedState.fs` gains one field,
`editor/Editor.fs` gains a panel entry, `render/ImGui.fs` gains five P/Invokes, the `.fsproj`
gains three compile entries and the test `.fsproj` two, one native shim line is added, and
four documents are corrected — `ARCHITECTURE.md`, `PROJECT_STRUCTURE.md`, `ROADMAP.md` and
`EDITOR_DESIGN.md` — and `specs/025-narrative-core` gains an amendment.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the roster is *moved*, not copied, and the binding is a map plus one lookup. There is no availability table (an entry's existence is the check), no per-character variant type, and no `assign`/`clear` helpers: the panel has one call site each, so it mutates the map. The one addition that needs justifying is a source cluster holding one file, recorded in Complexity Tracking. |
| II. Fail Loudly | Pass — a slot with no entry resolves to `None` rather than a default model (an expected absence), and a stored id that no longer names a loaded asset raises rather than being skipped. The panel offers only discovered assets, so a binding cannot be created for a missing one. |
| III. Spec-First | Pass — `specs/032-cast-binding/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — `Slot` is a struct record and a union of two closed unions, so the map's key is a value with structural equality rather than a string. The binding is a lookup, not an object graph. |
| V. Minimise GC Pressure | Pass — no per-frame path gains work. The roster and look enumerations are module-initialised values, so the panel reflects once per run rather than once per frame; the game loop does not touch the binding at all. |
| N-III. Modular Authorship on a Shared Substrate | Pass, and served — the vocabulary stays closed; the roster moves to a layer both the engine and the game see, so the engine derives the cast from one declaration rather than owning a copy, and adding a member still reports every place that must account for it. Traits stay the engine's, because nothing binds them to an asset. |
| N-V. Testing is the Authoring Tool | Pass — the new tests are headless, and the engine's suite still runs with no window, device, or assets (FR-013). |

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| A new source cluster, `cast/`, holding one file | The roster must be visible to the engine *and* to the game, while the engine must not become able to reference the game (FR-001, FR-004). A layer beneath `narrative/` is the only position satisfying both. | Declaring the roster in `world/` and compiling the engine after it is fewer moving parts, but it lets the engine reach the assets, GPU and entity modules — the compile error that `docs/ARCHITECTURE.md` records as the reason `narrative/` is listed first would become a convention again. |
| One native shim line plus a `libcimgui.so` rebuild | The dock layout is built in `native/cimgui_sdl3.cpp`, and ImGui's `DockBuilder` API is internal, so it is not reachable from F# through the vendored cimgui exports. Without the line the panel floats at a default position on every run, because layout persistence is deliberately disabled. | Binding `DockBuilder` from F# is impossible without the same rebuild; folding the cast tree into the Asset Browser window avoids native entirely but contradicts the spec's panel deliverable and puts two unrelated authoring jobs in one window. The panel is fully functional without the rebuild — only its dock position is not. |

## Project Structure

### Documentation (this feature)

```text
specs/032-cast-binding/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── cast-binding.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
cast/
└── Roster.fs            # NEW, and compiled first. `Character` (fieldless, closed),
                         # `characterName`, `characters` — moved out of
                         # `narrative/Vocabulary.fs` verbatim in behaviour. Asset-free: it may
                         # not reference assets, entities, rendering, or physics, so the
                         # engine's independence survives the roster's move.

narrative/
├── Vocabulary.fs        # loses `Character`, `characterName`, `characters`; gains nothing.
│                        # `Trait`, `Event`, `Fact`, `FactKind`, `traitName`,
│                        # `factParticipants`, `factKind`, `sortParticipants` stay.
├── {ParticipantIndex,Canonical,Facts,Rules,Derive,Engine,Harness}.fs
│                        # `open Roster` where a character is named; `Canonical` reads
│                        # `Roster.characterName`. Behaviour unchanged.
└── Units/{Cast,Friendship,Romance}.fs
                         # unchanged — the rules name characters through the opened roster.

world/
├── Casting.fs           # NEW. `Look` (shared, closed), `lookName`, `allLooks`, `Slot`,
│                        # `Cast` (the sparse binding) and `tryAsset`.
└── Assets.fs            # unchanged — `Actors` is what the panel's combo iterates.

state/
└── SharedState.fs       # + `Cast : Casting.Cast`, beside `Assets`: session-scoped authoring
                         # state, constructed empty and mutated in place by the panel.

editor/
├── CastPanel.fs         # NEW. The cast tree, a combo per slot, assignment and clearing.
└── Editor.fs            # `Cast` joins `panelNames` (Ctrl+Tab) and the draw order.

render/
└── ImGui.fs             # + `igTreeNodeEx_Str`, `igTreePop`, `igBeginCombo`, `igEndCombo`,
                         # `igSelectable_Bool`, `igPushID_Str`, `igPopID`.

native/
└── cimgui_sdl3.cpp      # one line: dock "Cast" beside the Outliner. Needs build.sh.

test/
├── RosterTests.fs       # NEW. The roster facts move here from NarrativeVocabularyTests.
├── CastingTests.fs      # NEW. The binding: assign, resolve, clear, absent, sharing.
└── Narrative*Tests.fs   # references to the moved roster updated; no behaviour assertion.

docs/
├── ARCHITECTURE.md      # the narrative section's roster bullet, and the paragraph that says
│                        # `narrative/` is listed first.
├── PROJECT_STRUCTURE.md # the `cast/` cluster, the compile list, the cluster descriptions,
│                        # and the dependency chain.
├── ROADMAP.md           # phase 10's `world/Cast.fs` line becomes `world/Casting.fs`.
└── EDITOR_DESIGN.md     # the Workspace table and the Cast panel's dock, Principle 4's undo
                         # scope, the non-entity-surfaces bullet, and a new D-022 recording
                         # that the panel assigns in place and is not an undoable command.
```

**Structure Decision**: the roster's home is a new first cluster, `cast/`, because it is the
only position that is both visible to the engine and invisible to the game from the engine's
side. `world/Casting.fs` holds the appearance half — the look vocabulary, the slot, and the
binding — because none of it is the engine's business and all of it refers to assets. The
roster stays a struct union of fieldless cases so identity keeps its property that a
misspelling is not a character; the look vocabulary is a second closed union rather than a
payload on the first, so facts about a character cannot carry her clothes. `Roster.fs`
deliberately declares no alias into `narrative/Vocabulary.fs`: the engine's code should read
as consuming a roster it does not own, and deleting the members is what makes the compiler
enumerate every site that named them. The panel is a tab beside the Outliner and assigns in
place rather than through the Inspector, which is `docs/EDITOR_DESIGN.md`'s dock position and
its D-022; this plan follows that document rather than inventing a position of its own.

## Phase 0: Research

See [research.md](./research.md). It settles where the roster lives and why not elsewhere, why
identity and look are separate types, how the look vocabulary declares itself, why the map is
sparse and needs no availability list, where the binding lives and why it is not undoable, and
what the panel costs in bindings and in native code.

## Phase 1: Design

- [data-model.md](./data-model.md) — the hoisted roster, the look vocabulary, the slot and the
  binding, the resolution rule, and the layer rules.
- [contracts/cast-binding.md](./contracts/cast-binding.md) — the surfaces that change
  (`Roster`, `Casting`, `SharedState`, `CastPanel`, `ImGui`).
- [quickstart.md](./quickstart.md) — runnable validation scenarios.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: the roster is moved rather than copied; the binding is a map and one lookup; no availability table, no variant types per member, no assign/clear helpers. The new cluster and the native line are the only additions, both recorded above. |
| II. Fail Loudly | Pass — absence is `None`, an unresolvable id raises, and only discovered assets can be assigned. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — a struct slot keyed by value, one dictionary, no object graph and no string-keyed identity. |
| V. Minimise GC | Pass — the enumerations are module-initialised; the game loop is untouched; only the panel allocates its labels. |
