# Research: Advents as an Ordered String Set

## D1 — The ordered set is Prime's `OSet<string>`

**Decision**: the flag set is `Prime.OSet<string>`. Add/remove/contains are `OSet.add`/`remove`/`contains`; the
author's order is read with `OSet.toSeq`.

**Rationale**: `OSet` is "an ordered persistent set based on UMap and FStack" — it preserves **insertion order**
and de-duplicates, so the panel's list and any future report read in the order the author named things. F#'s
`Set<string>` orders alphabetically, losing the authored order the old union had by declaration; a plain list would
preserve order but admit duplicates and make membership a scan. Measured on Prime 11.5.2: `OSet.ofSeq1 []` is a
usable empty set, `add` on an existing entry is a no-op that does not reorder, and `contains` allocates **0 bytes**
over 100,000 calls.

**Alternatives considered**: `Set<string>` (alphabetical — rejects the authored order); `ResizeArray<string>` with
a manual duplicate check (mutable, and membership a scan); a hand-rolled ordered set (no need, `Prime` is already a
dependency).

## D2 — `world/Advent.fs` stays, as the set's home

**Decision**: keep the module and its filename/compile slot. Remove the union, `all` and `name`; add
`type Set = OSet<string>` and four operations plus a normaliser:

```fsharp
module Advent
open Prime
type Set = OSet<string>
let empty : Set = OSet.ofSeq1 []
let contains (name: string) (set: Set) : bool = OSet.contains name set
let add (name: string) (set: Set) : Set = OSet.add name set
let remove (name: string) (set: Set) : Set = OSet.remove name set
let names (set: Set) : string list = OSet.toSeq set |> List.ofSeq
let normalise (name: string) : string = name.Trim()
```

**Rationale**: the module still owns "what an advent set is" and hides the `OSet` choice in one place, so a future
change to the representation is one file. Keeping the filename keeps the `.fsproj` untouched. `normalise` (trim)
centralises the one identity rule the panel and the cue chooser both apply.

**Alternatives considered**: delete the module and use `OSet<string>` raw everywhere (scatters the choice and the
trim rule); rename it `Advents.fs` (churn for no gain). The type is named `Set` — read as `Advent.Set.empty` — so
files that also use F#'s core `Set` must qualify it (`Microsoft.FSharp.Collections.Set`) or not `open Advent`; in
practice only advent code `open`s the module.

## D3 — `SharedState.Advents` is the set, and starts empty

**Decision**: `mutable Advents : Advent.Set`, initialised `Advent.empty` in `SharedState.create`. The five former
union cases are **not** seeded.

**Rationale**: the field is already the correct home (`specs/055` FR-002 — project state that outlives a zone).
Seeding the old cases would reinstate the built-in vocabulary the feature removes; nothing persisted them, and a cue
naming one still works because it sets membership by string (spec Clarifications).

**Alternatives considered**: seeding the five former cases (a hard-coded vocabulary in disguise); moving the field
to `GameplayState` (it must outlive a zone — `specs/055`).

## D4 — `Command.Advent` and the undo step are unchanged in shape

**Decision**: `Advent of Previous: Advent.Set * Next: Advent.Set`. `state/History.fs` assigns `next` on apply and
`previous` on undo, exactly as today; no logic change.

**Rationale**: an `OSet` is **persistent/immutable**, so a before/after pair of references is a complete snapshot —
the same property `Set` had. The step stays state-level (the set is not a zone's), so `Command.placeOf` still
returns `None` and the zone-level runner still refuses it.

**Alternatives considered**: storing the operation (add-name / remove-name) rather than the two sets (partial — an
add and a remove are then different shapes, and undo needs the branch; the set pair is total, as today).

## D5 — The four cue ops carry a `string`; `world/Cue.fs` drops `Advent`

**Decision**: `AddAdvent of advent: string`, `RemoveAdvent of advent: string`, `Await of advent: string`,
`AwaitState of advent: string`, `If of advent: string * thenCue: Cue * elseCue: Cue`. Remove `open Advent` from
`world/Cue.fs`.

**Rationale**: with no vocabulary, the payload is the name; the module no longer names any milestone type, so it
sheds its one non-`Prime` narrative dependency. The ops' behaviour is unchanged (FR-013).

**Alternatives considered**: a single `AdventOp of string` case (loses the named ops the panel and engine branch
on); keeping the union (the thing being removed).

## D6 — `CueEngine` uses the set's operations; the hot path stays allocation-free

**Decision**: `AddAdvent n -> shared.Advents <- Advent.add n shared.Advents`,
`RemoveAdvent n -> shared.Advents <- Advent.remove n shared.Advents`, and the awaits/branch test with
`Advent.contains n shared.Advents`.

**Rationale**: `OSet.contains` allocates nothing (D1), so the existing per-step allocation guard on an in-flight
`Await` still holds — the check runs every tick and must not allocate (project Principle V). `add`/`remove` run once
when the op is reached, on the cold path.

**Alternatives considered**: reading `Advent.names` and searching (allocates a list — breaks the guard).

## D7 — The panel is a text field, an Add, and a list with Remove

**Decision**: rewrite `editor/AdventPanel.fs`. A reused `inputText` over a new editor buffer
(`EditorState.AdventNameBuffer`) names a new advent; an **Add** control records `Advent.normalise` of the buffer and
is disabled when that is blank or already present; a **Remove** control per row clears that name from the set.
Adding clears the field. `commit` keeps its signature shape (a next set pushed as one `Advent` step).

**Rationale**: there are no fixed cases to check off, so the checklist becomes a small list editor; the
add-zone field (`editor/Outliner.fs`, `ZoneNameBuffer`) is the exact precedent (a typed name, disabled Add when
invalid). A private `bufferText` is added, as the four existing panels each keep their own.

**Alternatives considered**: a combo of names to add (nothing to offer — the set is what the author is
building); Enter-to-add with no button (the Outliner precedent uses a button, and a disabled Add is clearer).

## D8 — The cue chooser offers the current names with a typed fallback

**Decision**: `CueTree.drawEditor` gains an `advents: string list` parameter; its milestone control becomes
`filteredNameCombo`, seeded with `advents` and the op's current name, committing the picked or **typed** name (via
`Advent.normalise`). `CueEditor.draw` and `CueEditor.drawTrigger` pass `Advent.names shared.Advents`. A
newly-created advent op defaults to `""`. `withAdvent` takes a `string`.

**Rationale**: `filteredNameCombo` already exists for exactly this (the `specs/074` speaker: pick from discovered
names, or type one that matches none). Typing a name does **not** add it to the set (spec Clarifications) — the op
just carries it. The default `""` is honest: with no vocabulary there is no milestone to default to.

**Alternatives considered**: a plain `filteredCombo` (pick-only — an op could not name an advent before it is
added); offering a fixed list (there is none).

## D9 — A name is trimmed, then matched exactly (ordinal)

**Decision**: a typed name is trimmed of surrounding whitespace before it is stored or compared
(`Advent.normalise`), and then matched exactly — ordinal, case-sensitive — consistent with `specs/074`'s by-name
addressing. A blank-after-trim add is refused; a duplicate add is a no-op.

**Rationale**: trimming removes an invisible class of accidental duplicates; exact-case keeps the rule identical to
the speaker/asset name rule and needs no custom comparer on the set (whose default is ordinal). Case-insensitivity
would require a bespoke `IEqualityComparer` threaded through `ofSeq1`/`makeEmpty`, for a benefit the author owns.

**Alternatives considered**: case-insensitive (a custom comparer; rejected for the cost/benefit and for diverging
from `specs/074`); no trimming (allows `" MetAiko "` and `"MetAiko"` as different advents).

## D10 — Labels and defaults read the raw name

**Decision**: `CueTree.label` renders an op's milestone as the raw string, appended only when non-empty
(`"Add: MetAiko"`, but `"Add"` for an empty name). `defaultOfKind` makes an advent op with `""`.

**Rationale**: `Advent.name`/`Advent.all` are gone, so the label is the string itself; omitting the suffix for an
empty name keeps an unnamed op readable rather than `"Add: "` with a dangling colon (spec Edge Cases).

## D11 — Verification is headless

**Decision**: `test/AdventTests.fs` is rewritten to assert the ordered set (insertion order, de-dup, trim,
blank refusal, one-step undo, zone survival, and that the zone-level runner refuses an `Advent` step).
`test/CueTests.fs` runs the four ops against string milestones, keeping the `Await` allocation and blocking guards.
`test/CuePanelTests.fs` asserts the kinds, the `""` defaults, the labels and the `If` children over strings. Every
`SharedState` literal in the suite uses `Advent.empty`.

**Rationale**: the set and the ops are plain data, so they are assertable with no device (`specs/074`'s stance).
The editor's draws go through ImGui; the pure halves (the set operations, `CueTree`'s defaults/labels) are what the
suite asserts.

## D12 — Docs are corrected

**Decision**: update `docs/ARCHITECTURE.md` (the `Advent_Set` node and milestone passages), `docs/EDITOR_DESIGN.md`
(D-011/D-013 and the panel description), `docs/PROJECT_STRUCTURE.md` (the `Advent.fs` line), `docs/ROADMAP.md`
(phase 10), and `docs/CUE_MODEL_DECISION.md` (the advents-off-the-one-history passages).

**Rationale**: the docs are the project's source of truth and currently state the union is closed and cannot be
misspelled; that is now false.
