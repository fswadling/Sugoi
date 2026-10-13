# Contract: Clear World — Retiring Every Zone

What the game says about clearing the world (`specs/084`), and what the editor offers.

## The operation

- **Clear World** removes **every** present zone and installs a single fresh **start** zone, which becomes active. The
  world holds exactly one zone afterwards.
- The fresh start zone is **empty**: no actors, props, parts, waypoints or triggers; no environment; the default light;
  no player — the state `GameplayState.create` begins in.
- The running **cue is reset** to its starting (empty) value `Fin`; the **scenario library**, the **camera**, the fade
  and the dialogue are untouched.

## The undo step — `Command.ZonesClear`

```fsharp
| ZonesClear of Previous : (string * ZoneContents) list * PreviousActive : string
```

- **Payload**: the whole previous world — each present zone's name and its `ZoneContents`, plus the active place.
- **State-level**: `Command.placeOf` returns `None`; the zone-level runner refuses it (raises); `History` carries it.
- **One step**: one accepted clear is exactly one gesture on the one history — a `Batch` of the `ZonesClear` and
  (when the cue is not already empty) a `SetCue (previous, Cue.Fin)`. Undo rebuilds the payload world exactly (each
  entity at its recorded slot, the previous active place restored) **and restores the previous cue**; redo installs the
  fresh start and the empty cue again.
- **Apply ignores the payload**: the target is always the same fresh start (`[(startZone, Zone.emptyContents)]`) and
  the empty cue, so nothing about it is parameterised — as `ZoneDelete` needs no "after".
- **The cue half reuses `SetCue`**: it is not a zone's, so it is a state-level step already carried by `History`;
  `pruneCueSteps` reaches into the batch on Play and keeps the zone step undoable (`specs/049`).

## The world swap — `GameplayState.replaceZones`

```fsharp
replaceZones : SharedState -> byref<GameplayState> -> (string * Zone.ZoneContents) list -> string -> unit
```

- **Retires** every present zone: `Zone.release` (bodies destroyed, renderers and static-scene batch freed) and each
  distinct environment unloaded via `Assets.releaseEnvironment` once no remaining zone uses it.
- **Rebuilds** the given set through `Zone.create` + `Zone.occupyZone` (the one path that builds live entities),
  deactivating every place but the active one; then sets the active place and drops the selection/drag baseline.
- Safe on the **active** zone (`Zone.release` removes a still-added body before destroying it), which `deleteZone` — 
  requiring an inactive zone — is not.
- **Raises** if the named active place is not among the zones it installs (a broken invariant).

## The write path — `History.clearZones`

- Captures the previous world (`Zone.captureZone` per present zone) and the running cue, and commits **one** step — a
  `Batch` of `ZonesClear` plus a `SetCue (previous, Cue.Fin)` when the cue is not already `Fin`.
- The **only** public entry point: both the Edit-menu item and the shortcut call it, so the snapshot is taken once and
  the path is drivable headlessly.

## The editor surfaces

- **Edit menu**: a **Clear World** item (after Undo/Redo), labelled with its shortcut, gated by the menu's `idle`
  predicate (not mid-drag, not typing).
- **Shortcut**: **`Ctrl+Shift+C`** — `Ctrl` and `Shift` held with `C` pressed — in `handleShortcuts`, inside the
  edit-mode block and the `Drag = None` guard. Suppressed while a text field has focus (`handleShortcuts` early-returns
  on `ImGui.wantTextInput()`), so `Ctrl+C` keeps its ordinary meaning in a field.
- **Input**: `core/Input.fs` gains a `C` key (`Key.C`, mask bit 22, `SDL.Scancode.C` mapping).

## Postconditions

- **One empty start zone** after a clear (spec FR-002, FR-003, SC-001).
- **Single-step, exact reversal**: undo restores every removed zone with its contents and the previously active place;
  redo re-clears (spec FR-004, SC-002).
- **No leak**: removed zones' bodies/renderers/batches released and their environments unloaded once unreferenced
  (spec FR-007, SC-003).
- **Zones and the running cue**: the running cue is reset to empty and undo restores the previous run; the scenario
  library and the camera are untouched (spec FR-008, FR-010).
- **No dangling selection** after the clear (spec FR-009).
- **Gated**: never while typing; not offered outside edit mode; not mid-drag (spec FR-005, FR-006, SC-004).

## Explicitly unchanged / out of scope

- **The one history** (`specs/048`) — `ZonesClear` is a state-level step like `ZoneDelete`; nothing else about undo
  changes.
- **Zone contents capture/restore** (`Zone.captureZone`/`occupyZone`) — reused as-is.
- **The scenario library and the camera** (`specs/070`/`083`) — the library is authored project state and the camera
  is a view; neither is touched by a clear.
- **The dock layout / panels** — no panel changes; the action lives in the menu bar.
- **Persistence** — deferred to phase 15; a clear has no persisted effect.
- **A confirmation prompt** — none; undo is the safety net.
