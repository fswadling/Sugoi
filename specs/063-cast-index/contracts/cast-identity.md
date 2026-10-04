# Contract: Cast Identity and the Cast Index

What "which actor plays which character" means at run time, and what keeps it true.

## Identity

- An actor MAY record the `Roster.Character` it plays, as part of its **authored** state
  (`ActorState.Cast : Roster.Character option`). `None` is a non-cast actor (an Asset-Browser spawn).
- The runtime cast is keyed by `Roster.Character`, **not** by `Casting.Slot`: a look chooses which art a spawn
  uses, it is not part of the runtime identity, and the scene holds **one actor per character**.
- `Casting.tryAsset (character, look)` remains the *binding* — the game's sparse map from a member-and-look to
  an actor asset; it is how a spawn resolves an asset, and its absence is "no art for that look".

## The index

- `GameplayState` holds the index: `Roster.Character -> (Place * EntityRef)` — the actor, and the zone it is
  in. The zone is carried because `EntityRef` is zone-scoped.
- **`tryCastActor character`** answers the actor and its zone, or nothing. It does **not** depend on which zone
  is active: the index is global.
- **One character at most, across all zones.** The key is the invariant.

## When it changes

The index is written at exactly the points an actor comes into or leaves existence, and nowhere else:

| Change | Where | Result |
|--------|-------|--------|
| create (forward) | `Spawning.spawnActor` | entry added |
| delete (forward) | `Selection.deleteRef` (via the state-level runner) | entry removed |
| create/delete undone or redone | `History.apply`/`undo` | entry removed/added to match |
| zone deleted | `GameplayState.deleteZone` | every entry in that zone removed |
| zone-delete undone | `History.undo`'s `ZoneDelete` arm | entries re-added from the restored actors |

After any of these the index is **exact**: every present cast actor has one entry, no absent actor has one.

## Spawning a member

- A spawn resolves the actor asset from the character and look through `Casting`, sets the new actor's `Cast`,
  and records it in the index.
- **A spawn of a character already in the scene performs nothing** — one character, one actor; safe to repeat.
- When the existing member is in a **different zone** from the one being spawned into, the spawn records a
  diagnostic naming the character and its zone: one character in two places is the authoring mistake to
  surface, not to fail on.
- The editor's cast panel spawn and (in the next feature) a cue's spawn obey the same rule.

## Not in this contract

- **Cue ops.** This feature adds none; addressing actors from a cue is the next feature, which will read this
  index (and intends a single "set actor" op that both brings a member in and places it).
- **The narrative engine.** It names characters too, but binds nothing to actors here; this is the game's
  runtime cast, not the engine's.
- **Changing a live actor's character.** The cast role is set at spawn; changing who an actor plays is a
  despawn and a spawn.
