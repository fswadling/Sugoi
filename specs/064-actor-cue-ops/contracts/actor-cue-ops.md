# Contract: Actor Cue Ops

What a cue can say about the cast lifecycle, and what each op does.

## The ops — `world/Cue.fs`

```fsharp
| SetActor of place: Place * character: Roster.Character * look: Casting.Look * waypoint: string
| RemoveActor of character: Roster.Character
```

Both are no-progress leaves: reaching one performs its effect and the walk moves on. Neither has a `…State`
case. The set op names a place; the remove op does not — the cast index resolves her place and she is unique
across all zones.

## Resolution

- **The place** is a `Place`; `state.Zones.TryGetValue place` answers whether it is present. Not present ⇒ the
  op performs nothing (spec FR-006).
- **The member** is a `Roster.Character`; `GameplayState.tryCastActor state character` answers
  `(Place * EntityRef) option` — where she is, if she is in the scene at all (`specs/063`).
- **The waypoint** is a name in the named place's zone; `Zone.tryWaypointByName` resolves it. Not resolved ⇒ the
  op performs nothing. The actor stands at the waypoint's position and faces along its orientation.

## `SetActor` — one op for spawn and move (spec FR-002)

| The member is… | Then |
|----------------|------|
| **not in the scene** | resolve her art from `Casting.tryAsset { Character; Look = look }`; no art ⇒ nothing; else spawn her in the named place at the waypoint, with `Cast = Some character`, and index her |
| **in the named place** | set her transform to the waypoint's pose — same actor, same art, index unchanged |
| **in another place** | move her: remove her actor from the old place, spawn her into the named place at the waypoint with her **existing** `AssetId` (the op's look is not consulted), and re-point the index |

A set is idempotent with respect to existence: it never leaves two actors for one character (spec FR-003).

## `RemoveActor` (spec FR-003)

When the character is in the scene, remove her actor where the index says she is (destroy its body, release its
renderer) and drop her index entry. When she is not in the scene, the op performs nothing. No place is named.

## Postconditions

- **Index exact** (`specs/063` FR-004): after any set or remove, every present cast actor has exactly one entry
  and no absent actor has one.
- **One character, one actor** (`specs/063` FR-003), across all zones.
- **The active place is unchanged**: an actor op places a member; it never switches zones. A member placed in an
  inactive place exists, with her body detached from the simulation.
- **The cue moves on**: both ops answer `ValueSome Fin`.

## Explicitly unchanged / out of scope

- **The undo history.** An actor op is a runtime effect, not a step (`specs/062` FR-010, spec FR-007).
- **The narrative engine.** Actor ops read the game's runtime cast, not `narrative/`.
- **The zone vocabulary.** `specs/062` owns it; this feature adds none.
- **Movement, locomotion and animation/morph ops.** `MoveActor`, `RotateActor`,
  `Enable/DisableActorLocomotion`, `Animate`/`CrossFade`/`Morph`/`CrossMorph` and the camera/interaction/trigger
  toggles are deferred (spec FR-010).
- **The player designation.** Left exactly as an editor actor delete leaves it: a stale handle is "no player"
  (`specs/016`/`045`), so no clear step is added (spec FR-011).
