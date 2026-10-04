# Quickstart: Cast Index

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with no warnings and the suite green, with no device opened. The new behaviour is
asserted through the gameplay state and the editor's spawn path: a spawned cast member resolves by its
character; a second actor spawned for the same character does not (the cross-zone case logs); resolving a
character does not depend on the active zone; and create, delete, undo, redo, zone delete and zone-delete
undo each leave the index exactly consistent with the actors that exist.

## Manual

1. **Bind and spawn**: in the **Cast** panel, bind a character and look to an actor asset and spawn it. Select
   the actor in the Inspector — it now belongs to the cast (its character is known), and the state resolves
   that character to it.
2. **Non-cast actors are unaffected**: spawn an actor from the **Asset Browser**; nothing about the cast
   refers to it.
3. **One character, one actor**: with a character already in the scene, spawn it again from the Cast panel —
   no second actor appears. Move to another zone and spawn it there — still no second actor, and the console
   reports that the character is already in the other zone.
4. **Undo/redo and zone delete**: spawn a member, Ctrl+Z (the character no longer resolves), Ctrl+Y (it
   resolves again). Delete the zone holding a member and undo it — the character resolves again at the same
   actor; redo and it does not.

## References

- Contract: [contracts/cast-identity.md](./contracts/cast-identity.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
