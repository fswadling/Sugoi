# Quickstart: Cue Actors Addressed by Name

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).
- At least one discovered actor asset (so `AddActor` has something to place and the blend choosers have names).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with **no warnings** and the suite green, with no device opened.

- **actor op by string**: `CueTests` authors an actor op naming a placed actor's `Name` and asserts the op acts
  on her; a name with no actor performs nothing.
- **`AddActor` names an asset**: an `AddActor(place, "Aiko", "aiko", waypoint)` places an actor named `Aiko`
  drawn from the `aiko` asset; an undiscovered asset performs nothing.
- **no roster in cues**: `world/Cue.fs` names no `Roster`/`Casting` value (grep).
- **regression**: `test/DialogueTests.fs` and `test/CastingTests.fs` pass **unmodified** — portraits resolve
  through the binding, which this feature does not touch (SC-004).

## Manual

1. **The chooser is the scene**: place one actor (say `Akane`) in the active place and open a cue actor op's
   chooser. It offers `Akane` and no other roster member — the same list whether or not a cast binding exists.
2. **Blend without a binding**: with `Akane` placed but no cast binding, open an `Animate`/`Morph` op's chooser.
   It offers her asset's clip/morph names.
3. **`AddActor`**: add an actor op, choose a discovered actor asset, type a name (it defaults to the asset's),
   pick a place and a waypoint. Play: the actor appears there with that asset and name.
4. **Portraits still work**: with a cast binding and a portrait dialogue line, the portrait resolves exactly as
   before — the casts panel and the dialogue are untouched.

## References

- Contract: [contracts/cue-actor-by-name.md](./contracts/cue-actor-by-name.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
