# Quickstart: A Registered Zone's Placed Props

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- A display, for the interactive scenarios only. Both halves are asserted headlessly — capture in
  `test/ZoneTests.fs`, the replacement in `test/SpawningTests.fs` — so everything but the panel's two
  lines can be validated without one.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

`dotnet build --no-incremental` is the build to run: it reports warnings on unchanged files, which an
incremental build hides.

## Validation scenarios

Automated (headless):

1. **A zone's props are recorded (FR-001, FR-002, SC-001)** — load an environment, place several props,
   move one, rename another, delete a third and duplicate a fourth, then capture the entry and assert
   it holds exactly the props left standing: the moved one at its new placement, the renamed one under
   its new name, the deleted one absent, the duplicated one twice.
2. **The capture is a copy and it reads the zone (FR-008)** — taking an entry leaves the zone alone,
   and editing the zone afterwards does not change what was taken.
3. **Props and parts stay apart (FR-003)** — an entry's `Props` all name an asset and its `Parts` all
   name a piece of the environment, so instantiating cannot draw one as the other.
4. **Instantiating replaces the props (FR-004, SC-002, SC-003)** — register a zone with props, change
   the zone into a different environment with different props, instantiate the place, and assert the
   zone's props are exactly the entry's: same count, same assets, same names, same placements, and none
   of the ones it held before.
5. **An entry with no props clears them (FR-004)** — instantiate a place registered from an empty zone
   into a zone holding props: the props are gone rather than left alone, because the entry is the whole
   of what the zone's props become.
6. **One instantiation is one step (FR-005, SC-004)** — and this is the one to read if only one is:
   after an instantiation, **one** undo restores the zone's previous environment *and* its previous
   props, at the refs those props had before — assert the refs, not just the values, because restoring
   the props as copies at fresh slots would pass a values-only comparison and still break every ref the
   author held. Do it with several props, so a step-per-prop implementation cannot pass.
7. **Nothing else moves (FR-006, FR-009)** — instantiating leaves the entry unchanged, leaves any other
   place alone, and leaves the zone's actors, waypoints and triggers exactly as they were.
8. **No existing behaviour changes (SC-006)** — the whole suite passes with every assertion
   `specs/034-zone-registry` already made unmodified.

Interactive (editor; needs a display):

9. **Register takes the props with it (FR-001)** — set a room out with props, register a place, and
   confirm the entry is registered exactly as before: this feature adds nothing to the row, so the only
   visible change is what an instantiation does afterwards.
10. **Instantiate puts the room back (FR-004)** — change the zone's environment and its props entirely,
    then instantiate the registered place: the registered environment comes back with the registered
    props standing where they were.
11. **One Ctrl+Z, whatever the furniture (FR-005)** — place five props, instantiate the place, and undo
    once: the zone returns to its previous environment and its previous props in one step, not six.
12. **A prop you had selected survives (FR-005)** — select a prop, instantiate, and undo: the selection
    names the same prop again rather than a stale ref, because the restored props come back at their own
    identities.
13. **Nothing else in the room moved (FR-006)** — an actor, a waypoint and a trigger placed in the zone
    are untouched by the instantiation.

## Expected outcome

- An entry records a zone's placed props beside its environment, its settings and its parts, and the
  row that shows it is unchanged: the count is visible by instantiating.
- Instantiating replaces the zone's props along with its environment, so a registered place puts back
  the room the author set out rather than the room with extra furniture.
- One instantiation is one undo step whatever it replaced and whatever it placed, and undoing it leaves
  the zone exactly as it was, down to the identities of its props.
- Nothing about a zone's actors, waypoints or triggers is recorded or touched, and nothing about the
  registry's existing behaviour moved.
- `dotnet build --no-incremental` is clean and `dotnet test` passes, with every existing assertion
  unmodified.
