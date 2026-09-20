# Quickstart: Spawning a Cast Member from the Cast Panel

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- A display, for the interactive walk. There is nothing to validate without one: the control lives in
  a panel that only draws on a real ImGui frame, and this feature adds no headless test (see
  `research.md` Decision 5).

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # the whole suite, unchanged
```

The suite is the feature's regression guard rather than its proof: the spawn path it reuses —
`Zone.spawnActor` and `ActorCreate` — is already covered by `test/SpawningTests.fs`, including its
undo, and the two facts the control decides are covered by `test/CastingTests.fs` (`tryAsset`) and
`test/RosterTests.fs` (`characterName`).

## Validation scenarios

Automated (headless):

1. **No existing behaviour changes (FR-008, SC-006)** — the whole suite passes unmodified. In
   particular `test/SpawningTests.fs`'s spawn/undo exactness still holds, because this feature adds no
   command and no zone operation: it calls the ones that exist.
2. **The two spawn paths cannot drift (FR-007, SC-005)** — read `editor/AssetBrowser.fs` and
   `editor/CastPanel.fs`: both build their entity from `Spawning.baseEntity`, which is now the only
   copy of that literal. A second copy would be the drift the requirement forbids.

Interactive (editor; needs a display):

3. **A bound slot spawns its model (FR-001, FR-002, SC-001)** — open the Cast panel, expand a member,
   and confirm the row whose look is bound shows an enabled **Spawn** beside the combo; activate it
   and confirm an actor appears in the active zone drawn with that asset.
4. **The actor is named for the member (FR-003, SC-002)** — the new actor's name in the Outliner and
   the Inspector is the member's case name, not the asset's name.
5. **An unassigned slot cannot spawn (FR-005)** — a row showing `(none)` draws the control greyed out,
   and clicking it does nothing.
6. **Spawning is a scene edit (FR-004, SC-003)** — the new actor is selected as soon as it appears,
   and Ctrl+Z removes exactly it, in one step. The row's binding is unchanged by both the spawn and
   the undo.
7. **Assigning is still not a scene edit (FR-006, SC-004)** — reassign the slot's combo; nothing
   already placed changes, and Ctrl+Z does not undo the reassignment.
8. **Two looks, two controls (Assumptions)** — bind both of a member's looks; each row's control
   spawns its own asset, and both actors are named for the member. Duplicate names are allowed.
9. **The two entry points agree (FR-007, SC-005)** — spawn the same asset from the Asset Browser and
   from the Cast panel: same kind, same asset, same placement, same selection behaviour, differing
   only in the name.
10. **Nothing else moved (FR-008)** — the tree, the combos, the Asset Browser, the Outliner, the
    Inspector, Ctrl+Tab cycling, and the stats line's frame rate behave as they did before.

## Expected outcome

- Every bound slot offers one control that places its asset; every unbound slot offers the same
  control, inert. There is no default model and no fallback.
- What is placed is an ordinary actor: a name, a model, and a body, carrying no cast identity.
- The panel now places actors as well as assigning them, and the two behave differently on purpose:
  placing is undoable, assigning is not.
- `dotnet build` is clean and `dotnet test` passes with every existing assertion unmodified.
