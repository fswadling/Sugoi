# Research: Scenarios as a String-Keyed Ordered Library

## D1 — The library is Prime's `OMap<string, ScenarioContents option>`

**Decision**: the scenario library is `Prime.OMap<string, ScenarioContents option>` — an ordered, persistent map
from scenario **name** to its recorded contents. `add`/`remove`/`tryFind` are the map's operations; the authored
order is read with `OMap.toSeq`. A key present with `None` is a scenario that has been **named but not registered**;
`Some contents` is a registered one.

**Rationale**: `OMap` is "an ordered persistent map based on UMap and FStack" — it preserves **insertion order** and
de-duplicates by key, so the panel's list reads in the order the author named things, exactly as the advent set does.
It is **persistent**, so a before/after pair of references is a complete undo snapshot (the property `Dictionary`
lacks), and an overwrite keeps the key's position. Measured on Prime 11.5.2: `OMap.ofSeq1 []` is a usable empty map;
`toSeq` reads insertion order (`[("b",_);("a",_);("c",_)]`); `OMap.add` on an existing key updates the value **in
place, keeping its position**; `OMap.remove` drops the key; `OMap.tryFind` returns `None` for a missing key; and the
value may be an `option` (so "named but unregistered" is a first-class state).

**Alternatives considered**: `Map<string, …>` (F#'s core map orders **alphabetically**, losing the authored order);
a `Dictionary<string, …>` (mutable, unordered, and no snapshot — the shape being replaced); a two-structure model
(`OSet<string>` of names beside an `OMap` of contents — two things to keep in step and two snapshots per step, and
it still needs a membership check to express "named but unregistered"; the single `option`-valued map encodes all
four states in one ordered value).

## D2 — `world/Scenario.fs` is deleted; `world/ScenarioRegistry.fs` is the library's home

**Decision**: delete the vocabulary module `world/Scenario.fs` (the `Scenario` union, `scenarioName`, `all`) and its
`<Compile>` line. `world/ScenarioRegistry.fs` keeps its filename and compile slot and holds both `ScenarioContents`
and the `Registry` type and operations. Every file that `open Scenario` drops it.

**Rationale**: the vocabulary's only job was to name a scenario a cue op could carry; once `ForkScenario` carries a
`string` (D5), nothing references it, and the library — which depends on `Cue` and `ScenarioZone` — must be compiled
*after* them, so it cannot live in the (earlier) `Scenario.fs` slot. Keeping an empty or vestigial `Scenario.fs` would
be a dead file. This is the one structural difference from `specs/082`, where `world/Advent.fs` was *repurposed*
(rather than deleted) because the advent set needs no `Cue` and so could stay in its slot.

**Alternatives considered**: repurpose `world/Scenario.fs` as the library's home (forbidden by compile order — it
sits before `world/Cue.fs`); leave `Scenario.fs` as a comment-only stub (a dead file the compiler would still list).

## D3 — The library's operations

**Decision**: `world/ScenarioRegistry.fs` exposes:

```fsharp
type ScenarioContents = { Zones : Map<string, ScenarioZone>; Cue : Cue.Cue }   // unchanged
type Registry = OMap<string, ScenarioContents option>

let create () : Registry = OMap.ofSeq1 []
let names (registry: Registry) : string list = OMap.toSeq registry |> Seq.map fst |> List.ofSeq
let exists (name: string) (registry: Registry) : bool = OMap.containsKey name registry
let tryContents (name: string) (registry: Registry) : ScenarioContents option =
    match OMap.tryFind name registry with Some (Some contents) -> Some contents | _ -> None
// naming a present name is a no-op — `OMap.add` alone would overwrite a registered scenario's contents (FR-007)
let add (name: string) (registry: Registry) : Registry =
    if exists name registry then registry else OMap.add name None registry
let remove (name: string) (registry: Registry) : Registry = OMap.remove name registry
let setContents (name: string) (contents: ScenarioContents option) (registry: Registry) : Registry = OMap.add name contents registry
let normalise (name: string) : string = name.Trim()
let capture (zones: Dictionary<string, Zone>) (cue: Cue.Cue) : ScenarioContents = …   // unchanged
```

**Rationale**: this hides the `OMap` choice in one place. `tryContents` **flattens** the two ways a scenario can have
no contents — an absent key and a `None` value — into the one `option` the panel and the fork op care about, so a
caller never distinguishes "not named" from "named but unregistered" unless it asks (`exists`/`names`). `add` is
**guarded**: naming a scenario that is already present returns the library unchanged, so a duplicate add never clears
a registered scenario's contents (FR-007, spec Edge Cases) — a bare `OMap.add` would overwrite the value with `None`.
`capture` is untouched: it reads the live zones and the cue and is device-free. `create ()` is kept (not renamed to
`empty`) to avoid churning the six test fixtures that already call it.

**Alternatives considered**: `tryContents` returning `ScenarioContents option option` (leaks the two-state model to
every caller); a `Registry` record wrapper (no field to add — a bare alias is simpler, as `Advent.Set` is).

## D4 — `Command.SetScenario` carries the whole library before and after

**Decision**: `SetScenario of Previous : ScenarioRegistry.Registry * Next : ScenarioRegistry.Registry` — a
before/after pair of the whole library, replacing the current `Name : Scenario * Previous : ScenarioContents option *
Next : ScenarioContents option`. `History` assigns `next` on apply and `previous` on undo.

**Rationale**: with add and delete of **names**, a per-entry payload cannot express "the name now exists" versus
"the name is gone", because `Previous = None`/`Next = None` already mean "no contents". The whole-library pair is
total and matches `Command.SetCue` (the same state-level, `Previous * Next` shape) and `Command.Advent`. It is cheap:
`OMap` is persistent, so the pair shares structure. The name `SetScenario` (rather than `Advent`-style `Scenario`) is
kept for consistency with the sibling `Set*` state-level steps.

**Alternatives considered**: a per-entry payload with a `Named | Absent` discriminated union (more shapes, and two
different cases for add/delete vs register/clear); renaming the case to `Scenario` (churn with no gain, and it would
diverge from `SetCue`).

## D5 — `Cue.ForkScenario` carries a `string`; `world/Cue.fs` drops `Scenario`

**Decision**: `| ForkScenario of scenario: string` (was `of scenario: Scenario`). Remove `open Scenario` from
`world/Cue.fs`.

**Rationale**: with no vocabulary, the payload is the name; the module no longer names any scenario type, so it sheds
its scenario dependency entirely, exactly as `specs/082` proved for `Advent`. The op's behaviour is unchanged
(FR-014).

**Alternatives considered**: keeping the union (the thing being removed); a wrapper string type (a vocabulary in
disguise).

## D6 — `CueEngine` reads a string and reports the name it named

**Decision**: in `world/CueEngine.fs`, `ForkScenario scenario` uses `ScenarioRegistry.tryContents scenario
state.Scenarios` and, when absent/unregistered, `noOp "ForkScenario" scenario "it is not registered"`. The
`Scenario.scenarioName scenario` call is gone; the diagnostic prints the raw string.

**Rationale**: the op is a cold path reached once when the walk reaches it, so no allocation concern applies. The
report must name the scenario the author wrote, which is now the string itself.

**Alternatives considered**: none material.

## D7 — `GameplayState.Scenarios` is mutable and holds the persistent library

**Decision**: `mutable Scenarios : ScenarioRegistry.Registry` on `GameplayState` (the field becomes `mutable`; only
the type and mutability change). `create` keeps `ScenarioRegistry.create ()`.

**Rationale**: an `OMap` is a value, so a change produces a new library the state must store — the same reason
`SharedState.Advents` is `mutable`. Ownership is unchanged (the spec's Assumptions): the library stays on the gameplay
world, not `SharedState`, because only the gameplay screen and its editor read it.

**Alternatives considered**: moving it to `SharedState` (nothing else reads it — the readership test fails).

## D8 — The panel is a text field, an Add, and a list with Register/Clear/Fork/Delete

**Decision**: rewrite `editor/ScenarioPanel.fs`. A reused `inputText` over a new editor buffer
(`EditorState.ScenarioNameBuffer`) names a new scenario; an **Add** control records `ScenarioRegistry.normalise` of
the buffer and is disabled when that is blank or already present; the list shows `ScenarioRegistry.names` in order,
each row with **Register** (file `capture state.Zones state.Cue` under the name), **Clear** (empty the contents but
keep the name), **Fork** (enabled only when registered), and **Delete** (remove the name and its contents). Adding
clears the field. One private `commit shared &state (next: Registry)` reads `before = state.Scenarios` and pushes
`SetScenario (before, next)` as one step; `fork` keeps its shape.

**Rationale**: there are no fixed cases to check off, so the fixed list becomes a small list editor; the Advent panel
(`editor/AdventPanel.fs`, `AdventNameBuffer`) is the exact precedent (a typed name, a disabled Add when invalid, a
per-row control). The row's Register/Clear/Fork controls each commit a registry transform; no history of its own.

**Alternatives considered**: dropping Clear (rejected in clarification — keeping both preserves the existing
capability and is no more work); a combo of names to add (nothing to offer — the library is what the author is
building); auto-registering on Add (conflicts with the spec's separation of Add and Register).

## D9 — The cue chooser offers the current names with a typed fallback

**Decision**: `editor/CueEditor.fs`'s `drawNodeText` gains a `scenarios: string list` parameter; its `ForkScenario`
case becomes `CueTree.filteredNameCombo` seeded with those names and the op's current name, committing the picked or
**typed** name. Both call sites (`draw` and `drawTrigger`) pass `ScenarioRegistry.names state.Scenarios`. A
newly-created fork op defaults to `""` (`editor/CueTree.fs`'s `defaultOfKind`); `withScenario` takes a `string`;
`label` renders the raw name, appended only when non-empty ("Fork scenario: Intro", but "Fork scenario" for an empty
name).

**Rationale**: `filteredNameCombo` already exists for exactly this (the `specs/074` speaker / `specs/082` milestone:
pick from current names, or type one that matches none). Typing a name does **not** add it to the library (spec
Clarifications). The default `""` is honest: with no vocabulary there is no scenario to default to. `CueTree.drawEditor`
keeps "Fork scenario" in its `kinds` list (so a node can be re-kinded) but, as today, offers no scenario chooser — the
chooser lives in the main Cue panel's `drawNodeText`, which is where `ForkScenario` is authored now.

**Alternatives considered**: a plain `filteredCombo` (pick-only — an op could not name a scenario before it is added);
moving the chooser into `CueTree.drawEditor` (the trigger editor would then offer it too — a change in scope, and the
control already reads no state it does not have).

## D10 — A name is trimmed, then matched exactly (ordinal)

**Decision**: a typed name is trimmed of surrounding whitespace before it is stored or compared
(`ScenarioRegistry.normalise`), then matched exactly — ordinal, case-sensitive — consistent with `specs/074`'s
addressing and `specs/082`'s milestone rule. A blank-after-trim add is refused; a duplicate add is a no-op
that does not clear a registered scenario's contents (enforced in `add`, which returns the library unchanged when the
name is present, since `OMap.add` alone would overwrite it).

**Rationale**: trimming removes an invisible class of accidental duplicates; exact-case keeps the rule identical to
the speaker/asset/milestone rule and needs no custom comparer on the map (whose default is ordinal). Case-insensitivity
would require a bespoke `IEqualityComparer` threaded through `ofSeq1`/`makeEmpty`, for a benefit the author owns.

**Alternatives considered**: case-insensitive (a custom comparer; rejected for the cost/benefit and for diverging
from `specs/074`/`specs/082`); no trimming (allows `" Intro "` and `"Intro"` as different scenarios).

## D11 — Verification is headless

**Decision**: `test/ScenarioTests.fs` is rewritten to assert the string-keyed library (insertion order, add/delete,
trim, blank refusal, duplicate no-op, named-but-unregistered), `register`/`clear`, the one-step undo and its
round-trip, zone survival, and that the zone-level runner refuses a `SetScenario` step. `test/ScenarioForkTests.fs`
migrates every `Intro` to `"Intro"` and keeps its materialiser/fork assertions. `test/CueTests.fs` and
`test/CuePanelTests.fs` migrate to string scenario names (the `describe` helpers drop `Scenario.scenarioName`; the
default op becomes `""`).

**Rationale**: the library and the op are plain data, so they are assertable with no device (`specs/074`'s stance).
The editor's draws go through ImGui; the pure halves (the library operations, `CueTree`'s defaults and labels) are
what the suite asserts.

**Alternatives considered**: none — headless verification is the project's rule (constitution N-V).

## D12 — Docs are corrected, and one terminology is settled

**Decision**: update `docs/ARCHITECTURE.md` (the Scenario & Cue section and the `Scenario_Library` mermaid node),
`docs/EDITOR_DESIGN.md` (the Scenario panel), `docs/PROJECT_STRUCTURE.md` (the `Scenario.fs`/`ScenarioRegistry.fs`/
`ScenarioPanel.fs` lines), `docs/ROADMAP.md` (phase 11) and `docs/CUE_MODEL_DECISION.md`. The prose term is
**"scenario library"**; the code type stays `Registry` in the `ScenarioRegistry` module (unchanged names).

**Rationale**: the docs are the project's source of truth and currently state the vocabulary is a closed union. The
terminology question raised in `/speckit-clarify` is settled as "library" in prose — it already matches the docs
("the scenario library") — with the module and type names left as they are, so no rename churn is introduced.
