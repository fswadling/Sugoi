# Quickstart: Entity Authored State

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- For the interactive scenario: a display.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # the whole suite, including 030's behaviour tests
```

Everything here is headless: the reshaped constructions, the per-kind round-trips 030
added, and the two classification experiments below.

## Validation scenarios

Automated (headless):

1. **Every existing behaviour is unchanged (FR-007, SC-003, SC-006)** — the behaviour
   tests from `specs/030-entity-command-families` pass **unmodified**: per-kind
   delete/undo exactness, the link travelling with a waypoint, a copy carrying it, the
   trigger's authored shape restoring without its runtime flag, and the transform-merge
   undo granularity.
2. **A payload cannot hold a resource (FR-002, FR-003, SC-001)** — a structural test
   reflects over the three authored record types and asserts that none exposes a member
   named for a physics attachment, a renderer, or a runtime flag.
3. **Recording is a read, not a projection (FR-005)** — for each of the three kinds, the
   value a deletion carries equals the authored value the live entity had, field by field,
   and the recording path contains no step that writes to or clears anything.
4. **Restoring rebuilds resources from the authored value (FR-004)** — undo a prop, part,
   and trigger deletion: each returns with its authored value identical and its resources
   newly built. (An actor's restore is excluded for the reason 030 recorded: building and
   releasing its renderer needs a GPU device, so scenario 10 covers it.)
5. **An authored property needs no recording-path edit (FR-006, SC-004)** — add a
   throwaway field to `ActorState`, confirm the solution builds once the sites that
   *construct* an actor state are updated (and nothing in the recording or restoring
   path had to change), then remove it.
6. **A derived resource is forced (FR-006, SC-004)** — add a throwaway derived field to
   `Actor`, confirm the build fails in `Zone.occupyActor` and `Zone.spawnActor` until it
   is produced, then remove it.
7. **Runtime state still recomputes in place (FR-010)** — the trigger containment tests
   (entering, leaving, disabled, interactable) pass unmodified, and nothing records or
   restores the flag.
8. **The environment lifecycle is unchanged (FR-011)** — the environment load/swap tests
   pass unmodified, including that an inactive zone's parts hold no bodies and no GPU
   resources and are rebuilt on return.
9. **No per-frame kind check (FR-008, SC-005)** — no `EntityKind` match was added outside
   `world/` and `state/`, and the systems still scan their own kind's store.

Interactive (editor; needs a display):

10. **Nothing editor-visible changed (FR-007, SC-003, SC-006)** — select, transform, nudge,
    rename, duplicate, and delete each kind; edit a trigger's radius, height,
    interactable, prompt, and enabled; toggle an actor's collider overlay; author a
    waypoint chain and delete its middle; switch zones. Every value and every undo step
    behaves as before, and the stats line's frame rate is unchanged across a session.
    This walk is also where the adjacent name-attribution correction (`tasks.md`) is
    observable, so re-check it here: add an actor and then a waypoint — the actor keeps its
    name — and likewise for a trigger's prompt.

## Expected outcome

- The three kinds that carry derived or runtime state record and restore through an
  authored value of their own; a payload cannot express a body, a renderer, or a runtime
  flag.
- No code clears derived fields in order to make something recordable; the one remaining
  teardown is the environment lifecycle's, and it behaves exactly as before.
- Both classification experiments behave as stated: an authored property flows into what
  is recorded and restored without touching those paths, and a derived resource stops the
  build until it is produced.
- `dotnet build` is clean and `dotnet test` passes with every behaviour test unmodified.
