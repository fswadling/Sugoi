# Quickstart: The Cast — a Roster Hoisted from the Engine and Bound to Actor Assets

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- For the interactive scenario: a display. The dock layout (`/speckit-plan`, Decision 7) also
  needs `native/libcimgui.so` rebuilt — `./native/build.sh`, which requires network access and
  SDL3 development headers. Without it the panel still draws; it merely floats.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # the whole suite, including the narrative engine's
```

Everything below except the interactive walk is headless.

## Validation scenarios

Automated (headless):

1. **One declaration of the roster (FR-001, SC-001)** — read the solution and confirm exactly one
   file declares the member names: `cast/Roster.fs`. `narrative/Vocabulary.fs` declares no
   character, and no other module lists one.
2. **The roster is enumerable and self-describing (FR-001, FR-003)** — `Roster.characters` yields
   every case exactly once, in declaration order, and `Roster.characterName` renders each. This is
   `NarrativeVocabularyTests`' roster fact, moved to `RosterTests`.
3. **Adding a member reaches both sides with no edit (FR-003, SC-002)** — add a throwaway case to
   `Character`, confirm the build reports the matches that must account for it (the engine's
   render match, any total match over the cast) while `Casting`, `CastPanel`, and `Roster.characters`
   need no edit, then remove it.
4. **The look vocabulary declares itself once (FR-005, SC-002)** — add a throwaway case to `Look`,
   confirm `lookName`'s match fails to compile until it is named and that `Casting.allLooks`
   reports it with no other edit, then remove it.
5. **A slot resolves to what was assigned (FR-007, SC-003)** — assign an asset id to a slot, confirm
   `Casting.tryAsset` returns it; assign other slots and confirm each returns its own; reassign one
   and confirm it returns the new id.
6. **An unassigned slot resolves to nothing (FR-006, FR-010)** — a slot with no entry answers
   `None`, and clearing an assigned slot makes it answer `None` again. There is no third state and
   no default model.
7. **A member need not have every look (FR-006, SC-003)** — a member with one look assigned and one
   unassigned is representable, and the unassigned slot changes nothing about the assigned one.
   This is the "existence in the map is the existence check" rule, asserted directly.
8. **Two members may share an asset (FR-007, edge case)** — bind two members to one asset id;
   both resolve to it and neither assignment is disturbed.
9. **The cross product is fully representable (SC-003)** — every `Roster.characters` ×
   `Casting.allLooks` slot can be assigned, resolved, and cleared.
10. **Nothing is undoable (FR-011, SC-003)** — the panel's assignment path pushes no `UndoStack`
    entry; a structural read of `CastPanel` shows no `UndoStack.push`, and a zone's applied/redo
    lists are unchanged across a simulated assignment.
11. **The engine's independence holds (FR-004, SC-005)** — add a reference to `Assets`, `Casting`,
    `Zone`, or `Gpu` from a file under `narrative/` and confirm the build fails with a
    "not defined" error, then remove it.
12. **The engine is unaffected and still headless (FR-012, FR-013, SC-006)** — the narrative suite
    passes with every assertion unmodified, and runs with no window, device, or assets.

Interactive (editor; needs a display):

13. **The cast is a tree (FR-009, FR-010)** — open the Cast panel: every member is a collapsible
    header, closed by default, and under each is one row per look showing the look's name, a combo
    of discovered actor assets, and `(none)` while unassigned.
14. **An author binds and clears a slot (FR-009, SC-003)** — expand `Akane`, assign one asset to
    `SchoolUniform` and another to `Casual`, and confirm each row shows the assigned asset's name;
    set one back to `(none)` and confirm the row reads unassigned. Expand a second member and
    assign the same asset as Akane's — both rows show it.
15. **Only discovered assets are offered (FR-008, SC-004)** — the combo lists exactly the actor
    assets the Asset Browser lists, and nothing else can be written into a slot.
16. **The panel joins the editor (FR-009)** — Ctrl+Tab cycles Outliner → Inspector → Asset
    Browser → Cast, and the panel is docked as a tab beside the Outliner (after the native
    rebuild; floating otherwise).
17. **Nothing else changed (FR-012, SC-007)** — the Asset Browser, Outliner, Inspector, viewport,
    gizmos, play mode, and the stats line's frame rate behave exactly as before, and the panel
    being open does not change the frame rate measurably.

## Expected outcome

- The engine's roster is declared once, outside the engine, and both the engine and the game read
  that one declaration; the engine still cannot reach the game, and its tests still run headless.
- A character is an identity without a model, a look is a variant without a character, and the
  binding is what pairs them — so an engine fact about a character carries no clothes.
- The editor presents the cast as a tree and binds a discovered actor asset to each slot; not every
  member has every look, and an unassigned leaf means "no model here" rather than an error.
- `dotnet build` is clean and `dotnet test` passes, with the narrative assertions unmodified.
