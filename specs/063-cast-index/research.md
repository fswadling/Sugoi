# Research: Cast Index

## D1 — The index lives on `GameplayState`, keyed by `Roster.Character`

**Decision**: `GameplayState` gains a cast index: a map from `Roster.Character` to the actor playing it,
carrying the zone it is in and its ref — `Dictionary<Roster.Character, Place * EntityRef>`, with helpers
(`tryFind`, `add`, `remove`, `removeZone`). A character resolves to its actor in one lookup.

**Rationale**: the undo/redo stack is the gameplay state's (`state.Editor.Undo`), and undo/redo run at that
level (`History.undoHistory`/`redoHistory` take the state), so the index can be maintained where actors enter
and leave existence rather than searched on every use. It is global because the invariant — one character at
most, across all zones — is global.

**Alternatives considered**: a **zone-level** index (your original proposal) is maintained by the three `Zone`
actor ops that add/remove slots, so it cannot be forgotten, but it is per-zone and cannot answer global
uniqueness without a cross-zone check. A **derived scan** (no index; scan the present zones' actors) has no
drift but is what you objected to at spawn. A **`KeyedSlotMap`** does not fit: the actor's storage already is
the zone's `SlotMap<Actor>`, and only cast members have a character, so a keyed map over it degenerates to this
dictionary — it would not own storage, and would not change the authority question.

## D2 — The cast role is authored on the actor

**Decision**: `ActorState` gains `Cast : Roster.Character option` — the character this actor plays, or `None`
for a non-cast actor (an Asset-Browser spawn). As authored state it is carried by `ActorCreate`/`ActorDelete`
and by `captureZone`/`occupyZone`, so undo/redo and zone capture/restore keep it with no extra plumbing.

**Rationale**: every point that must update the index already has this value in hand — the command payload
(`ActorCreate (r, actor)`, `actor.Cast`) and the zone contents. That is what lets the index be maintained at
the actor-existence points instead of searched.

## D3 — The index is maintained at the actor-existence points

**Decision**: the index is updated by `CastIndex`/`GameplayState` helpers at exactly the places an actor comes
into or leaves existence, and nowhere else:

| Point | Where | Update |
|-------|-------|--------|
| forward create | `Spawning.spawnActor` (`Zone.spawnActor` + push) | `add` |
| forward delete | `Selection.deleteRef`, routed through the state-level runner | `remove` |
| undo/redo of create/delete | `History.apply`/`History.undo` (`ActorCreate`/`ActorDelete` arms) | `add`/`remove` |
| zone delete | `GameplayState.deleteZone` (`Zone.release`) | `removeZone` |
| zone-delete undone | `History.undo`'s `ZoneDelete` arm, after `occupyZone` | `add` per restored actor |

**Rationale**: putting the update at the mutation is what keeps a maintained index honest. The undo/redo and
zone cases are all `History`'s or `GameplayState`'s, which have the state; the one forward path that is not a
command *apply* (`spawnActor` allocates the slot itself) updates directly. The alternative — giving
`Command.apply`/`undo` the index — would ripple a new parameter through `Selection`/`Spawning`/`History`/tests
to reach a mutation that `spawnActor` bypasses anyway.

**Alternatives considered**: a single `Command`-level hook (rejected above); rebuilding the index from the
actors after each change (a scan on the write path, and it reintroduces the cross-zone walk).

## D4 — Uniqueness: no-op, with a cross-zone diagnostic

**Decision**: a spawn naming a character already in the scene performs nothing. When the existing member is in
a **different zone** from the one being spawned into, the spawn records a diagnostic (`eprintfn`) naming the
character and the zone it is already in; the same-zone case is silent.

**Rationale**: spec Q1 = A. A no-op is safe to repeat (a cue re-runs, a double-click), and the cross-zone case
is the authoring mistake — one character in two places — worth surfacing without failing.

## D5 — The spawn path carries the cast role and enforces uniqueness

**Decision**: the editor's cast spawn (`editor/CastPanel.fs`, through `Spawning`) resolves the asset from
`Casting.tryAsset` and sets the new actor's `Cast` to the character; before spawning it checks the index and
no-ops (with the D4 diagnostic) when the character is already present. An Asset-Browser spawn sets no cast.

**Rationale**: FR-005; it makes a hand-placed member addressable exactly as a cue-placed one will be, and it is
where the uniqueness rule lives so the later cue op inherits it.

## D6 — No new module, no compile-order change

**Decision**: the index is a field on `GameplayState` with helpers beside it, and `ActorState`'s new field is
in `world/Entity.fs`. No file is added to `fsharp-game.fsproj`.

**Rationale**: `GameplayState` (and every updater — `History`, `Selection`, `Spawning`) is compiled after
`Entity`, `Place` and `Roster`, so the index needs no type moved earlier. `Command.fs` is untouched (D3).

## D7 — Verification is headless

**Decision**: assert in `test/GameplayStateTests.fs` (or a new `test/CastIndexTests.fs`) that a spawned member
resolves, that a duplicate spawn no-ops, and that create/delete/undo/redo/zone-delete/zone-undo leave the
index exact; `test/SpawningTests.fs` covers the editor spawn path. No device.
