# Quickstart: Clear the World Back to a Fresh Start

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`) for the editor.
- No asset setup needed for the clear itself: it touches no asset beyond unloading environments a removed zone held.

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with **no warnings** and the suite green, with no device opened.

- **the clear and its reversal**: `GameplayStateTests` builds a world with several zones (entities, an environment,
  a light, a player) and a non-empty cue, clears it, and asserts exactly one zone remains — at the start place, empty,
  active — with an empty cue, and that **one** undo restores every zone at its original refs with its contents, the
  previously active place **and the previous cue**, and that redo re-clears (spec FR-002, FR-003, FR-004, FR-010;
  SC-001, SC-002, SC-006).
- **no leak**: `GameplayStateTests` builds a zone whose environment is loaded (device-free fixture) and asserts the
  clear releases it and unloads the environment; a clear of an already-empty world is a no-op that loses nothing
  (spec FR-007, SC-003).
- **the step is state-level**: `GameplayStateTests` asserts the zone-level runner (`Command.apply`/`undo`) refuses a
  `ZonesClear` (raises), as it does every other state-level step (spec FR-004).
- **the shortcut's key**: `InputTests` includes `Key.C` in the "every key maps to a distinct bit" list, so the new
  binding cannot collide with an existing one (spec FR-005).

## Manual

1. **Build a world**: in the editor, add a second zone from the Outliner and author some entities (an actor, a prop, a
   waypoint, a trigger), load an environment, and set an actor as the player.
2. **Clear**: open **Edit → Clear World** (or press **Ctrl+Shift+C**). Every zone is gone; the Outliner's Zones
   section lists just the **start** place, the viewport is an empty scene, the 3D cue is reset to empty, and the
   Inspector shows no selection — but the camera and the Scenario panel are unchanged.
3. **Undo**: press **Ctrl+Z** (or **Edit → Undo**). Every zone returns with its contents and the previously active
   place is active again. **Ctrl+Y** clears once more. A single `Ctrl+Z` suffices for the whole clear.
4. **Typing is safe**: focus a text field (a zone name, an entity name) and press **Ctrl+C** — it copies/does nothing
   to the world; the clear only fires on **Ctrl+Shift+C** outside a field.
5. **World and cue, nothing else**: note the Scenario panel is unchanged by a clear, and the running cue is reset to
   empty (undo brings the previous run back).
6. **Play mode**: while playing, neither the menu item nor the shortcut is available.

## References

- Contract: [contracts/clear-world.md](./contracts/clear-world.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
