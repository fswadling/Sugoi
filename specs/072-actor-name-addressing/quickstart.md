# Quickstart: Actors Addressed by Name

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).
- A cast binding for at least one `(character, look)` — the **Cast panel** still assigns one from a discovered
  actor asset (its assignment behaviour is unchanged; only its spawn control stops recording a role).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with **no warnings** and the suite green, with no device opened. The behaviour is
asserted through `CueEngine.tick` against a `GameplayState` whose asset manager holds a fabricated
`ActorAsset` (the `specs/063` fixture, retargeted), so the real spawn/remove paths run headlessly:

- **`AddActor` places** a member in the named place at the waypoint’s pose, named for the character; a second
  `AddActor` places a **second** actor of that name (there is no index to reconcile, no uniqueness);
- **`RemoveActor`** finds the member by name across the present places and removes her; a second remove
  performs nothing;
- **`MoveActor` / `RotateActor` / a blend / a player toggle** find the member by name and act;
- **`Zone.tryActorByName`** finds a named actor and misses an unknown name; the gameplay resolver finds a
  member across places in `Place.all` order and takes the first of a duplicate;
- each op **no-ops** on an absent place, an unbound look, an unknown waypoint, or an absent member — and a
  re-run never faults.

Regression proof for the untouched paths: `test/RosterTests.fs`, `test/CastingTests.fs` and
`test/DialogueTests.fs` pass **unmodified** (portraits resolve through the binding, which is unchanged).

## Manual

1. **Spawn a member with a cue**: bind a character’s look to an actor asset in the **Cast** panel; make a place
   present (`Add zone`); place a waypoint in it; author **`Add actor`** naming that place, character, look and
   waypoint. Play: the member appears at the waypoint, facing along it.
2. **Add, don’t upsert**: run the cue again — a **second** actor of that name appears. (This is the withdrawn
   uniqueness constraint: a name is a label.)
3. **Move a member**: author `Remove actor` for her, then `Add actor` in the new place. Play: one actor of her
   name remains, at the new placement.
4. **Remove**: author `Remove actor` naming her. Play: she is gone.
5. **Re-run safety**: run a cue that adds a member in an absent place, names an unknown waypoint, or names a
   character with no bound art — nothing happens and the cue carries on without an error.

## References

- Contract: [contracts/actor-name-addressing.md](./contracts/actor-name-addressing.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
