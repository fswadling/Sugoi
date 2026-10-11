# Quickstart: An Explicit Zone on Every Entity Cue Op

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).
- A scene with **two present zones** holding actors/waypoints (to tell the named zone apart from the active
  one), and at least one discovered actor asset.

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with **no warnings** and the suite green, with no device opened.

- **an op acts in its named zone**: `CueTests` places two actors of one name in two zones and asserts an
  `Animate`/`RemoveActor` naming the second acts on the second and not the first (spec SC-001).
- **a miss is a no-op**: an op naming an absent place, or a place holding no actor of that name, performs
  nothing and the run continues (spec FR-003).
- **motion is active-zone-only**: `MoveActor`/`RotateActor` name no zone and act on the active zone
  (spec FR-009).
- **camera ops raise**: `SetCamera`/`FlyCamera` on a name that does not resolve, or a place that is not
  present, raises (`specs/054` FR-009).
- **the control**: `CuePanelTests` asserts the labels (with the zone), the kind dropdown, `withPlace` on the
  nine ops, the place-preserving writes, and that `defaultOfKind` uses the given place.
- **the global scan is gone**: `GameplayState.tryActorByName` no longer exists (grep / build).

## Manual

1. **The chooser follows the op's zone**: place an actor `Akane` in `Island` and one `Bo` in the active zone.
   Author a `RemoveActor`, pick `Island` from its zone selector, and open its actor chooser — it offers
   `Akane`, not `Bo`. Pick `Akane`; switch the op's zone back to the active zone and the chooser offers `Bo`.
2. **Default is the current zone**: add an `Animate` from the kind dropdown while standing in a zone — its
   zone is that zone.
3. **One step**: change an op's zone, then undo — the previous zone returns.
4. **Motion has no selector**: select a `MoveActor`/`RotateActor` — it shows no zone selector, and its actor
   and waypoint choosers read the active zone.
5. **Labels read the zone**: the tree shows `Remove actor: Akane in Island`, so two same-named actors in
   different zones are told apart.
6. **The two editors are independent**: give an op in the Cue panel a non-active zone, then open the inline
   trigger-cue editor for a trigger — its ops and choosers are unchanged by the panel's choice.

## References

- Contract: [contracts/cue-entity-zone.md](./contracts/cue-entity-zone.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
