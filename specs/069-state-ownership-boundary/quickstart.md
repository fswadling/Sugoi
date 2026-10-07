# Quickstart: State Ownership Boundary

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with no warnings and the suite green, with no device opened. The tests that
hand-built a `SharedState` with `ZoneRegistry`/`Physics` no longer do; a test that needs a template
seeds the gameplay state's registry.

## Checks that the boundary holds

1. **No stale references.** Search the source for `shared.Physics`, `shared.ZoneRegistry`,
   `Shared.Physics` and `SharedState`'s old members — none remain; the reads are `state.Physics` /
   `state.ZoneRegistry` (or `manager.Gameplay.Physics` in `Engine.fs`).
2. **`SharedState` membership.** Its fields are exactly `Gpu`, `Assets`, `Cast`, `Advents`; each has
   an engine or any-screen reader. It no longer mentions per-zone undo stacks or `spec 034`.
3. **`GameplayState` membership.** It holds `Zones`, `ZoneRegistry`, `Physics`, and the screen's
   runtime state; `create` constructs the registry and the world.

## Manual

1. **Everything still works, unchanged.** Launch, open a zone, load an environment, place props and
   actors, and play — the world is stepped once per play frame exactly as before (one physics
   world).
2. **Zone switch.** Switch between two present places; the outgoing zone's bodies detach and the
   incoming zone's attach (movement collides only in the active place), as before.
3. **Register and instantiate.** In the **Zone Registry** panel, **Register** the active zone
   against a place, then **Instantiate** it — the place becomes present and active with its template
   loaded, exactly as before (the registry is read from the gameplay state).
4. **A cue's zone ops.** Add an **Add zone** and a **Preload zone** node naming a registered place;
   run the cue and confirm the place is brought in / its environment starts loading, as before.
5. **Return to the menu and back.** Play → menu → Play; the zones, the registry and the world are
   intact (the gameplay state is not cleared on a screen change).

## References

- The rule and membership: [contracts/state-ownership.md](./contracts/state-ownership.md)
- Data model (before/after): [data-model.md](./data-model.md)
- Decisions and call sites: [research.md](./research.md)
