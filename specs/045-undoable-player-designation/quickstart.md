# Quickstart: Undoable Player Designation

## Prerequisites

- A built tree: `dotnet build`
- The editor: `dotnet run` (the game starts in the editor; the Inspector is on the right)

## Automated coverage (headless)

`dotnet test` — the suite runs without a GPU device, and covers the whole of FR-001 to FR-004:

1. **The round trip** (`test/ZoneTests.fs`, beside the `SetLight` case): with a zone whose player is
   `ValueNone`, `apply` a `SetPlayer (ValueNone, ValueSome handle)` and assert the designation is set;
   `UndoStack.undo` it and assert it is back to `ValueNone`; `UndoStack.redo` it and assert it is set
   again.
2. **The previous value is restored, not "no player"** (the same case): re-designate from actor B to
   actor C, undo, and assert the designation is **B** — the case FR-002 exists for.
3. **The zone's own history** (`test/GameplayStateTests.fs`): a designation in one zone, an unrelated edit
   in the other, then an undo while the second zone is on screen — the second zone's own edit comes back
   and the first zone's designation is untouched; switching back, the designation is still there to undo.
4. **A no-op records nothing** (`test/SelectionTests.fs`): binding the actor who already is the player
   twice leaves exactly one step and an unchanged designation.
5. **The suite is otherwise unmoved**: the existing assertions pass unmodified (FR-009, SC-006).

## Interactive walkthrough

In a zone with at least one actor (load the environment, drop an actor in, and select her in the
Outliner).

1. **Set and take back (FR-001, FR-002, Story 1, SC-001, SC-002)** — with no player designated, press the
   actor section's **Set as Player**. The section should now read back that she *is* the player. Press
   Ctrl+Z once: the section is back to offering the action, i.e. the zone has no player. Press Ctrl+Y (or
   redo): she is the player again. One click is one step — a single undo is enough.
2. **A re-designation restores the previous actor (FR-002)** — set actor A as the player, then actor B.
   One undo must leave **A** as the player, not "no player"; a second undo leaves no player at all.
3. **A no-op adds nothing (FR-004, SC-002)** — with A the player, select A again. The control reads that
   she already is the player (and does not record a step); press undo and confirm it takes back the
   *previous* edit, not a no-op.
4. **The control reads the zone (FR-005, Story 3, SC-005)** — select an actor who is not the player and
   confirm the section says so; switch to another zone (Outliner's Zones list) and confirm the reading
   follows that zone's own designation, not the previous zone's.
5. **The history is the zone's own (FR-003, Story 2, SC-003)** — designate a player in zone A, switch to
   zone B, and press undo: whatever zone B's last edit was is taken back, and **zone A's designation is
   untouched**. Switch back to A and undo there to take it back.
6. **Play mode still works (FR-006, SC-004)** — press Play: the camera follows the designated actor and
   she can be walked as before. Undo the designation while editing, press Play again, and confirm the
   zone now drives no character (and that this is not an error).
7. **Nothing else moved (FR-009)** — the light controls, the other entity edits, the cast panel and the
   zone registry behave exactly as before.

## Expected outcome

The player designation behaves like every other edit to a zone's own state: one Ctrl+Z takes it back, one
Ctrl+Y puts it back, it is undone in the zone it was made in, and the editor tells you who the player is.
