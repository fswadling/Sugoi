# Quickstart: Actor Cue Ops

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).
- A cast binding for at least one `(character, look)` — the Cast panel assigns one from a discovered actor asset.

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with no warnings and the suite green, with no device opened. The new behaviour is
asserted through `CueEngine.tick` against a `GameplayState` whose asset manager holds a fabricated `ActorAsset`
(the `specs/063` `test/CastIndexTests.fs` fixture), so the real spawn/remove paths run headlessly:

- a **set** places a member in the named place at the waypoint's pose, and the cast index resolves her there;
- a **set** on a member already in that place moves the same actor — still exactly one actor for her;
- a **set** naming a place she is elsewhere in **moves** her, and the index follows;
- a **remove** drops her actor and her index entry;
- each op **no-ops** on an absent place, an unbound look (first placement), an unknown waypoint, and a remove of
  a member who is not there — and a re-run of the same cue does not fault.

`test/CuePanelTests.fs` asserts the panel's pure half — the new labels, kinds and write helpers
(`withPlace`, `withCast`, `textOf`/`withText` for the set op's waypoint).

## Manual

1. **Spawn a member with a cue**: make a place present (`Add zone`), place a waypoint in it, and author
   `Set actor` naming that place, a character, a look, and the waypoint. Play: the member appears at the
   waypoint, facing along it.
2. **Move, don't duplicate**: run the cue again with a different waypoint — the same actor moves; there is still
   one of her.
3. **Cross-zone move**: put the member in one place, then author a set naming another present place. Play: she
   leaves the first and appears in the second, in the same outfit.
4. **Remove**: author `Remove actor` naming her place and character. Play: she is gone, and the cast index no
   longer resolves her.
5. **Re-run safety**: run a cue that sets a member in an absent place, or names a waypoint that does not exist,
   or names a character the game has no art for — nothing happens and the cue carries on without an error.

## References

- Contract: [contracts/actor-cue-ops.md](./contracts/actor-cue-ops.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
