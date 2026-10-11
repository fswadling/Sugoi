# Research: Retire the Cast

## D1 — `Casting` is deleted, not replaced

**Decision**: delete `world/Casting.fs` entirely — `Look`, `lookName`, `allLooks`, `Slot`, `Cast`, `Bindings`,
`tryAsset`. No replacement map. `SharedState.Cast` and `editor/CastPanel.fs` go with it.

**Rationale**: after `specs/072`/`specs/073` the binding's only runtime consumer was `DialogueRender.tryPortrait`,
and the binding answered a question ("which asset is this character?") the asset manager can answer directly. There
is nothing else to keep (`Casting.tryAsset` is called only by the panel).

**Alternatives considered**: keeping the binding for portraits only (the bolt-on the feature exists to remove);
keeping `Look`/`Slot` for a future multi-look world model (speculative — no consumer, `Principle I`).

## D2 — The speaker is a string in `Dialogue.Kind`

**Decision**: `Dialogue.Kind.PortraitDialogue of speaker: string * Emotion` (`world/Dialogue.fs`). The name plate
is the string; the portrait is resolved from the string.

**Rationale**: a cue actor is a string (`specs/073`); a portrait speaker should be the same shape, so
`world/Dialogue.fs` no longer names `Roster`. It is what lets the roster leave the game (D4).

**Alternatives considered**: keeping `Roster.Character` as the speaker (keeps the game on the roster — the thing
the user is retiring).

## D3 — The roster moves into the narrative subsystem

**Decision**: move `cast/Roster.fs` → `narrative/Roster.fs`, **preserving the `module Roster` declaration**, and
list it **first** among the narrative files in `fsharp-game.fsproj` (before `narrative/Vocabulary.fs`). Remove the
`cast/` entry and the folder. Its comment is rewritten to say it is the engine's vocabulary.

**Rationale**: the narrative engine is the union's only remaining owner (`narrative/Canonical.fs`, `Engine.fs`,
`Facts.fs`, `ParticipantIndex.fs`, `Vocabulary.fs` all `open Roster`; `test/RosterTests.fs` tests it). Keeping the
module name means **zero** edits to those consumers and their tests, and the move is one file plus one compile-order
entry. The game simply stops referencing `Roster`.

**Alternatives considered**: folding `Character` into `narrative/Vocabulary.fs` and rewording every
`open Roster`/`Roster.characterName` (more churn for no behavioural gain); making it `namespace Narrative` with
`module Roster` (same effect, but it re-points `test/RosterTests.fs`'s `open Roster`; not needed).

## D4 — Resolution by declared `Name`, exact, lowest `Id` wins

**Decision**: a speaker string resolves to the actor asset whose `ActorAsset.Name` equals it exactly (ordinal). If
two assets share a name, the one with the lower `Id` (ordinal) wins. An asset that omits `Name` in its manifest
falls back to its `Id` (already the loader's behaviour), so it resolves a speaker only if that id is the string.

**Rationale**: the manifest's `Name` is the canonical speaker name (it already equals the roster case name,
`[Name "Akane"]`), and `Id` is a free lowercase slug. A deterministic tie-break keeps the drawn portrait stable
across runs, where the old binding walk was deterministic by look order (spec Clarifications; `specs/072`'s "a name
is a label" rule). An exact match keeps the contract simple and a mismatch a silent miss, like every other by-name
resolution.

**Alternatives considered**: matching `Id` (the slug is not the display name); case-insensitive (a just-in-case
fallback, `Principle I`); first-scanned (non-deterministic).

## D5 — A name index on the asset manager, built once at scan

**Decision**: add `ActorsByName : Dictionary<string, ActorAsset.ActorAsset>` to `AssetManager`
(`world/Assets.fs`). Build it once when actors are scanned — iterate the loaded actors sorted by `Id` and keep the
first per `Name` (the D4 tie-break). `Assets.scan` builds it in `loadActors`; `TestSupport.manager` builds it from
its `actors` argument via the same private helper. Add `Assets.actorNames (manager) : string list` — the sorted,
distinct `Name`s — for the choosers.

**Rationale**: the portrait resolve runs on the per-frame draw path (`specs/038` FR-010, project `Principle V`); a
per-frame scan of the actor dictionary allocates an enumerator. An index lookup with `ContainsKey` + the indexer
allocates nothing — the pattern `tryPortrait` already uses to avoid tuples. The chooser helper is a cold editor
path, so its sort is free to allocate.

**Alternatives considered**: scanning `assets.Actors` per resolve (per-frame allocation, breaks FR-010); caching
the resolved asset in the dialogue state (adds a field `specs/038` FR-007 keeps minimal).

## D6 — `DialogueRender` resolves by name; the `cast` parameter is dropped

**Decision**: `tryPortrait (assets) (speaker: string) (emotion) : PortraitResolution` reads
`assets.ActorsByName` (contains + indexer); on a hit it returns `portraitOf emotion asset.Portraits` (unchanged
mapping). `missReason`, `draw` and `GameplayScreen`'s call drop the `cast` parameter. `speaker` returns the string
directly (no `Roster.characterName`).

**Rationale**: the whole chain is now `string → asset → portrait`, with no binding. `portraitOf` is the one place an
expression names a portrait field, unchanged (`specs/038` FR-005).

## D7 — `MissReason` reduces to the states that can still occur

**Decision**: `MissReason` becomes `NotMissed | NoAsset | NoPortraits | TooTallForViewport`. `Unbound` (no binding)
and `AssetNotLoaded` (binding named an id not loaded) both collapse into `NoAsset` (no discovered asset of that
name). `DialoguePanel.advisory` rewords them without naming a Cast panel.

**Rationale**: with no binding there is no "bound but not loaded" state — a name either names a discovered asset or
it does not. Retaining a case that can no longer occur violates `Principle I` and `Principle II`.

## D8 — Both speaker choosers offer the discovered actor names

**Decision**: `CueTree.drawEditor` gains a `names: string list` parameter (the discovered actor names); its Exposit
speaker combo offers it, and a new portrait defaults to the first name (or `""` when none is discovered).
`CueEditor.draw` and `CueEditor.drawTrigger` pass `Assets.actorNames shared.Assets`. `DialoguePanel.draw` computes
the same list from `shared.Assets` for its speaker combo and its portrait default.

**Rationale**: the previous combo offered the roster, which is gone; the discovered actors are exactly the set that
can resolve, and offering them works whether or not the actor is placed (spec Clarifications). Three call sites
need the name list, so the `Assets.actorNames` helper earns its keep (`rule of three`).

**Alternatives considered**: offering only portrait-bearing assets (a nicety the `NoPortraits` advisory already
covers); reading the asset manager inside `CueTree` (the control stays pure and takes a list, as it takes the
`Cue`).

## D9 — `SharedState.Cast` and the Cast panel are removed

**Decision**: drop `Cast : Casting.Cast` from `SharedState` and from `SharedState.create`; delete
`editor/CastPanel.fs`, its `CastPanel.draw shared &state` call in `editor/Editor.fs`, and the `"Cast"` entry in
`panelNames`. Every test `SharedState` literal loses its `Cast = { Casting.Bindings = Dictionary() }` line.

**Rationale**: nothing builds a `Casting.Cast` once `Casting` is gone. Placement of an actor is unaffected — the
Asset Browser and the cue `AddActor` already name an asset directly (`specs/073`).

## D10 — Verification is headless

**Decision**: `test/DialogueTests.fs` is retargeted to string speakers and the new `tryPortrait` shape, with
`castBinding`/`emptyCast` deleted and the `allocates nothing per call` guard kept (now against a name lookup). A new
`test/AssetNameIndexTests.fs` asserts the index directly (by name; exact; lowest `Id` wins; missing → none; the
`Name → Id` fallback). `test/CastingTests.fs` is deleted with its module. `test/RosterTests.fs` and the narrative
suites stay **unmodified** — the proof the relocation cost the engine nothing (SC-003). The editor draws go through
ImGui (device-bound), so the pure halves (the index, `CueTree`'s combo/`filteredCombo`) are what the suite asserts.

**Rationale**: `specs/063`/`specs/072` established the headless fixture; a name index is pure data, so it is directly
testable with no device, and the retained allocation guard holds FR-011/SC-006.

## D11 — An in-flight actor op carries the resolved slot handle

**Decision**: `MoveActorState`, `RotateActorState` and `BlendState` gain `place: Place` and `handle: Handle`
(`core/SlotMap.fs`), captured at `begin*` from the `(place, zone, ref)` the resolve already returns
(`handleOf ref`). `stepMove`/`stepRotate`/`stepBlend` look the actor up in O(1) — `state.Zones` by the stored
`place`, then `zone.Actors.IsAlive handle` / `&zone.Actors.Get handle` — and no longer call `tryResolveActor`. The
authored op still names the actor by `string`; the one-shot ops (`RemoveActor`, `EnablePlayer`/`DisablePlayer`, the
`begin*` resolutions) keep the scan.

**Rationale**: the per-tick scan is the one per-frame cost the by-name work put on the play path (the removed cast
index answered it in O(1)), and the resolve returns an `option`/tuple, so it allocated per tick (FR-014–FR-016).
`Handle` is `{ Index; Generation }`, is compiled below `world/Cue.fs`, and `Zone.Actors` is a `SlotMap<Actor>`, so
the step takes it directly — no `actorRef`/`handleOf` round-trip. `ZoneContents.Player : Handle voption` is the
precedent for storing a `Handle`.

**Alternatives considered**: storing an `EntityRef` (it lives in `world/Entity.fs`, *after* `world/Cue.fs`, so the
module would have to name an entity type — rejected; FR-019); storing the `Zone` (same problem, and a zone is not
pure data); two raw uints `struct (Place * uint32 * uint32)` (works, but `Handle` is the existing, named type and
the slot map takes it).

## D12 — The op pins the actor it began on; the name is re-checked

**Decision**: the step ends the op when the stored place is absent, when the slot is dead or its generation has
changed, or when the slot's occupant's `Base.Name` is no longer the op's stored name. It never switches to another
actor of the same name. `MoveActorState`/`RotateActorState` also end when the place is no longer `state.ActiveZone`
(unchanged); `BlendState` has no active-zone check (a blend acts in the actor's own zone).

**Rationale**: this is a deliberate behaviour change from `specs/073`, where the per-tick name scan followed the
*first* match — so a same-named newcomer could take over a running move. Pinning is the correct reading of "this op
acts on that actor". The stored `handle` alone is not globally unique (indices are per-zone), so a `RemoveZone` then
`AddZone` in one tick could present a *new* zone whose slot 0 generation 1 matches a stale handle; re-checking the
name, one ordinal compare, closes that without a scan.

**Alternatives considered**: trusting the handle alone (aliases across a re-created zone — rejected); keeping the
per-tick scan (the cost being removed).

## D13 — The step allocates nothing: the residual per-frame allocations are fixed too

**Decision**: three small per-frame allocations the step would otherwise pay are removed as part of US4:
`Physics.getBodyTransform` returns a `struct (Vector3 * Quaternion)` (it ran every play frame from
`Systems.syncActors` and every move tick); `Actor.tryFindAnimIndex` becomes a loop returning `int voption` (the
`Array.tryFindIndex` closure captured `name` and allocated per call, and `Some` allocated too); and
`ActorRender.setMorphWeight`'s morph lookup becomes a loop the same way. `ActorRender.setClipWeight`/
`setMorphWeight` are called twice/once per step.

**Rationale**: FR-016 promises the step adds no per-frame allocation; with these, an allocation guard on a move
step holds at exactly 0 bytes. They are also genuine wins beyond cues — `syncActors` and the animation path run
every play frame regardless of any cue.

**Alternatives considered**: narrowing FR-016 to the resolve only (rejected — the step *can* be allocation-free
cheaply, and the guard then proves the whole step); leaving `getBodyTransform`/`tryFindAnimIndex` as separate
follow-ups (rejected — they mask the guard and are three lines each).
