# Research: Clear the World Back to a Fresh Start

The spec's clarifications already fixed **what** clearing does (remove every zone → fresh start) and **where** it
lives (Edit menu + `Ctrl+Shift+C`). The decisions below settle **how** it is built, reusing what exists.

## D1 — The clear is a state-level command, not a `Batch` of deletes

**Decision**: a new `Command` case, `ZonesClear`, carried by `state/History.fs` — the same class as `ZoneDelete`,
`ZoneMove`, `SetCue`, `SetScenario`. `Command.placeOf` returns `None` for it and the zone-level runner refuses it.

**Rationale**: the whole world is the unit of work, and building it needs what only the state has — the physics
world, the GPU device (to release bodies/batches) and the active place. A `Batch` of existing commands cannot express
it: a `ZoneDelete` may not name the active zone (`History.fs` skips such a step; `specs/061` FR-011), and the step
must *also* install a fresh start zone. This mirrors every other whole-world/scoped step in the one history
(`specs/048`).

**Alternatives considered**: a `Batch` of `ZoneDelete`s plus a "create start" step (cannot remove the active zone,
and there is no create-a-fresh-zone step to reuse); a `ZoneDelete` per zone with a prior switch (the fresh start zone
must be new and empty, not a survivor).

## D2 — The payload is the whole previous world

**Decision**:

```fsharp
| ZonesClear of Previous : (string * ZoneContents) list * PreviousActive : string
```

Each present zone's **name** paired with its `ZoneContents` (`Zone.captureZone`), plus the name of the active place.
Apply ignores the payload and always installs the fresh start (there is nothing to parameterise — the target is
fixed); undo installs the payload.

**Rationale**: `ZoneContents` already carries everything a zone owns (its actors/props/waypoints/triggers with their
refs, its environment and its parts, its light and its player) — it is the same record `ZoneDelete` carries, so a
restore re-occupies each entity's original slot. The tuple list plus the active name is **total**: it can rebuild the
world exactly, so undo needs no other store. Apply needing no payload keeps the case honest — the "after" is always
the same fresh start, exactly as `ZoneDelete` needs no "after".

**Alternatives considered**: a named `ZonesSnapshot` record (one more type for a two-field payload; Minimalism);
carrying a full `Zone` per name (a live zone holds bodies and renderers a payload may not hold — `ZoneContents'` raison
d'être); carrying only the deleted names and re-fetching contents (nothing to re-fetch — the zones are gone).

## D3 — One world-swap helper, `GameplayState.replaceZones`

**Decision**: add to `state/GameplayState.fs`:

```fsharp
let replaceZones (shared: SharedState.SharedState) (state: byref<GameplayState>) (zones: (string * Zone.ZoneContents) list) (active: string) : unit
```

It (1) releases every present zone — `Zone.release zone state.Physics shared.Gpu.Device` — and unloads each distinct
environment via `Assets.releaseEnvironment`; (2) clears the zone map; (3) rebuilds `zones` through `Zone.create ()` +
`Zone.occupyZone`, deactivating every place but `active`; (4) sets `ActiveZone <- active` and drops the
selection/drag/nudge baseline. Apply calls it with `[(startZone, Zone.emptyContents)]`/`startZone`; undo with the
payload.

**Rationale**: apply and undo are the **same operation with different arguments**, so they share one implementation —
the pattern the codebase already follows for capture/restore. `Zone.release` is safe on the **active** zone: it uses
`Physics.releaseBody`, which removes a body from the simulation if it is still added before destroying it (`deleteZone`
instead sidesteps the question by requiring an inactive zone and is not reusable here). Building the restored world
through `Zone.occupyZone` is the one path that constructs live entities, and the build-then-deactivate for a non-active
place is exactly what `ZoneDelete`'s undo already does. The retired `Zone` objects are managed values — dropping them
from the map is the whole of their disposal; only the native bodies and GPU handles need the explicit teardown above.

**Alternatives considered**: two functions, `clearWorld` and `restoreWorld` (duplication of the retire step, which is
the fiddly half); reusing `deleteZone` in a loop (refuses the active zone); releasing only bodies via `destroyBody`
(misses renderers and the batch that `release` frees).

## D4 — `Zone.emptyContents` names "a fresh start"

**Decision**: add `world/Zone.fs`, beside `captureZone`/`occupyZone`:

```fsharp
/// The contents of a fresh, empty zone — what a reset installs as the new `start` place.
let emptyContents : ZoneContents =
    { Actors = []; Props = []; Waypoints = []; Triggers = []
      Environment = None; Light = Texture.defaultLight; Player = ValueNone }
```

**Rationale**: apply's target is "an empty zone", and this states it once, next to the two functions that read and
write a zone's contents. `Texture.defaultLight` is the same default `Zone.create ()` sets.

**Alternatives considered**: `Zone.captureZone (Zone.create ())` (constructs a throwaway zone and batch just to read
empty lists — allocation for nothing); an inline record literal in `History` (the shape belongs where the type lives,
as `ZoneContents` itself does).

## D5 — The zone-level runner refuses `ZonesClear`

**Decision**: add `ZonesClear` to `Command.placeOf`'s `None` arm and to both `Command.apply`/`undo` `invalidOp` arms
(alongside `ZoneDelete`, `SetCue`, `SetScenario`).

**Rationale**: it spans zones and needs the physics world/GPU/active place, so `Command` cannot carry it out against
one zone; reaching it in the zone-level runner is a broken invariant to report loudly (`Command.apply`'s existing
stance), not a value to ignore.

**Alternatives considered**: none — this is the same wiring every state-level step already has.

## D6 — `History.clearZones`: the one public write path

**Decision**: add to `state/History.fs`:

```fsharp
/// Retire every present zone and reset the running cue, as one step on the one history (spec 084).
let clearZones (shared: SharedState.SharedState) (state: byref<GameplayState>) : unit =
    let zones = state.Zones
    let previous = [ for KeyValue (name, zone) in zones -> name, Zone.captureZone zone ]
    let cueSteps = if state.Cue.IsFin then [] else [ SetCue (state.Cue, Cue.Fin) ]
    do commit shared &state (Batch (ZonesClear (previous, state.ActiveZone) :: cueSteps))
```

Both the menu item and the shortcut call it; the snapshot is taken here, once per invocation.

**Rationale**: the two surfaces (menu, shortcut) must not each re-derive the payload, and factoring it out makes the
write path drivable headlessly — the same reason `ScenarioPanel.commit` and `AdventPanel.commit` are public. Binding
`let zones = state.Zones` first is required: a list comprehension may not capture the `state` byref.

**Alternatives considered**: inline the capture at each call site (two copies of the snapshot, neither testable
without ImGui); putting it on `GameplayState` (compiled before `History`, so it cannot commit).

## D6a — The running cue is reset by a `SetCue` in the same `Batch`

**Decision**: the cue half of the clear is an ordinary `SetCue (state.Cue, Cue.Fin)` committed in the **same** `Batch`
as the `ZonesClear`, and skipped when the cue is already `Fin` (so an empty cue records no cue step, as a fork of an
empty cue does). `History.clearZones` builds the batch.

**Rationale**: the cue is *not* a zone — it is the screen's own value (`specs/046` FR-013), written by the existing
state-level `SetCue` step — so reusing `SetCue` keeps `ZonesClear`'s name and payload honest (zones + active place
only) and adds no cue handling to `Command` or its runner. `Batch` is exactly "several mutations that were one
gesture": one clear is still one undo step, applied forwards and reversed backwards. `pruneCueSteps` already reaches
**into** a batch and drops the nested `SetCue` on Play while keeping the zone step undoable (`specs/049`), which is the
behaviour a clear should have on the way into play.

**Alternatives considered**: widening `ZonesClear`'s payload with the previous cue (a second concern packed into a
zone step, and a rename to match — churn); a separate command for "clear cue" (a `SetCue` to `Fin` already is one).

## D6b — The camera is left where it is

**Decision**: a clear does not touch the camera; `replaceZones` drops the selection/drag/nudge baseline but leaves
`state.Camera` as it stands. Only undo/redo's existing switch behaviour re-frames (when they change the active place).

**Rationale**: a clear is not a *switch* — the thing in this codebase that re-frames the view (`enterZone`) — and the
author's viewpoint is a view, not world state. Moving it would be a side effect the command does not record and undo
does not carry. This matches how the camera is treated elsewhere: it is re-derived from the active place on a switch,
never stored in a command payload.

**Alternatives considered**: re-framing on the fresh start / resetting to the creation framing (surprising, and would
need the camera carried in the payload for an exact reversal).

## D7 — The `C` key is appended at the next free bit

**Decision**: in `core/Input.fs`, add `| C` to the `Key` union, `| C -> 1u <<< 22` to `Key.mask`, and
`int SDL.Scancode.C, Key.mask Key.C` to `keyMap`.

**Rationale**: `Key.mask` is a `uint32` (bits 0–21 used by the existing cases), so bit **22** is free; appending
touches no existing case and cannot collide. Minimalism: the addition is three lines and renumbers nothing.

**Alternatives considered**: inserting `C` among the letters and renumbering `Digit`/modifiers (churn and a wider
blast radius for no benefit; the `Digit n` scheme only has headroom for a few more digits before it would reach the
modifier bits).

## D8 — The surfaces: Edit menu + shortcut, both gated

**Decision**: add a `Clear World` item to the Edit menu in `Editor.draw` (after Redo), with the shortcut label
`"Ctrl+Shift+C"` and the menu's existing `idle` gate (`Drag = None && not (ImGui.wantTextInput())`). Add the binding to
`Editor.handleShortcuts`, inside the `if state.Editing` block **and** the `Drag = None` guard (for the shortcut) —
`Ctrl` **and** `Shift` held with `C` pressed.

**Rationale**: the Edit menu already mirrors the Undo/Redo shortcuts and is the feature's chosen home; `handleShortcuts`
already early-returns while `ImGui.wantTextInput()` and is called only while editing, which covers FR-006. Gating the
shortcut on `Drag = None` matches the spec's edge case (a clear mid-gesture is not carried out), the same guard
undo/redo use. `Ctrl+Shift+C` (not `Ctrl+C`) keeps the conventional Copy binding free (spec Clarifications).

**Alternatives considered**: `Ctrl+C` (rejected in `/speckit-clarify` — shadows Copy); a dedicated key with no modifier
(discoverability, and it would fire during typing of a field only if not gated — the gating is the real protection and
is already present).

## D9 — The label is `Clear World`

**Decision**: the Edit-menu item reads **"Clear World"**.

**Rationale**: the Scenario panel's per-row **Clear** empties a scenario but keeps it (`specs/083`); naming this action
after its true effect (the whole live world) tells the two apart (spec Clarifications).

**Alternatives considered**: "Clear Zones" (closer to the request, but reuses the loaded word with the opposite
meaning); "Reset World" (clear, but drops the `C` mnemonic the shortcut uses).

## D10 — Verification is headless, in the existing fixtures

**Decision**: extend `test/GameplayStateTests.fs` (clear → fresh start; one-step undo/redo; active place restored;
environments unloaded; a place whose slot is reused is not confused; the zone-level runner refuses the step) and
`test/InputTests.fs` (add `Key.C` to the uniqueness list). No new test file, so no test `.fsproj` change.

**Rationale**: `GameplayStateTests` already builds a device-free `SharedState`/`GameplayState` and drives `History`
(the delete/undo tests call `Zone.release` with a defaulted device already, so the clear's teardown is covered by the
same fixture). The clear and its step are plain data + the state runner, assertable with no device — the project's rule
(constitution N-V). `InputTests` asserts the key map's uniqueness property, which the new case must join.

**Alternatives considered**: a new `test/ClearZonesTests.fs` (one `<Compile>` line for a handful of tests that fit the
existing zone/history suite; Minimalism).

## D11 — Docs

**Decision**: update `docs/EDITOR_DESIGN.md` — the Edit menu/shortcut description, the D-011 command list (add the
`ZonesClear` step), and the module map. `docs/ARCHITECTURE.md` and `docs/PROJECT_STRUCTURE.md` need no change (no new
module, and the zone section is unchanged).

**Rationale**: the docs are the source of truth for the editor's command model, and the Edit menu is documented there.
Nothing structural moves, so the other docs are untouched.
