# Data Model: Cast Index

## `world/Entity.fs`

```fsharp
[<Struct>]
type ActorState =
    { Base : Entity
      AssetId : string
      ShowCollider : bool
      Cast : Roster.Character option }   // NEW: the character this actor plays (None for a non-cast actor)
```

Authored state, so it is carried by `ActorCreate`/`ActorDelete` and by `captureZone`/`occupyZone` unchanged.
(`Roster` is compiled first, so `Entity` may name it.)

## `state/GameplayState.fs`

```fsharp
type GameplayState =
    { ...
      Cast : Dictionary<Roster.Character, Place * EntityRef>   // NEW: the runtime cast index
      ... }

// The actor playing `character`, with the zone it is in, or None.
val tryCastActor : GameplayState -> Roster.Character -> (Place * EntityRef) option

// The actor-existence points (research D3) call these; they are the only writers.
val addCast    : byref<GameplayState> -> Roster.Character -> Place -> EntityRef -> unit
val removeCast : byref<GameplayState> -> Roster.Character -> unit
val removeCastZone : byref<GameplayState> -> Place -> unit   // a deleted zone's members
```

- Keyed by `Roster.Character`; the value carries the **zone** because `EntityRef` is zone-scoped (a bare ref
  cannot say which zone's slot map it belongs to).
- **One character at most**: `addCast` is only called when `tryCastActor` is `None` (the spawn path enforces
  it); `addCast`/`removeCast` otherwise assume the invariant holds and fail loudly on a broken one.
- `removeCastZone` drops every entry whose zone is `place` — what a zone delete needs, in O(its cast).

## `editor/Spawning.fs` / `editor/CastPanel.fs`

- `Spawning.spawnActor` gains `(cast: Roster.Character option)`; after `Zone.spawnActor` it calls `addCast`
  when the cast is `Some`.
- `CastPanel` resolves the asset from `Casting.tryAsset`, checks `tryCastActor` first, and no-ops (with the
  cross-zone diagnostic, research D4) when the character is already present. `AssetBrowser` passes `None`.

## `state/Command.fs` / `state/History.fs`

- `Command.fs` is **unchanged** (it is zone-scoped and cannot see the index).
- `History.apply`/`History.undo` gain `ActorCreate`/`ActorDelete` arms (beside the existing `ZoneAdd`/
  `ZoneDelete` ones) that delegate to `Command.apply`/`undo` and then update the index from the command's
  `ActorState.Cast` and the active place.
- `History.undo`'s `ZoneDelete` arm re-adds each restored actor's cast after `occupyZone`.

## `editor/Selection.fs`

- `deleteRef` routes its `ActorDelete` through the state-level runner (`History.commit`-style) so the index
  update lives in one place; it then adjusts the editor's selection as it does today.

## Validation rules

- **Resolve**: `tryCastActor character` answers the actor and its zone, or `None`; it does not depend on the
  active zone (FR-002, FR-006).
- **Unique**: at most one entry per character across all zones (FR-003).
- **Exact**: after any create, delete, undo, redo, zone delete or zone-delete undo, every present cast actor
  has exactly one entry and no absent actor has one (FR-004).
- **No-op spawn**: a spawn of an already-present character performs nothing; a cross-zone repeat logs (FR-005,
  FR-007).
- **No cue ops**: this feature adds none (FR-008); the index and the actor's cast role are the whole change.
