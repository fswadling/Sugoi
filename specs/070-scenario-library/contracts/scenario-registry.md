# Contract: the scenario library surface

This feature exposes no external interface — the project is a single desktop app. The contract below is the
**internal** surface other modules depend on: the vocabulary, the store, and the step. It fixes the names and
signatures so `tasks.md` and the implementation agree with the panels and the runner that call them.

## `world/Scenario.fs` — the vocabulary (pure, before `world/Cue.fs`)

```fsharp
module Scenario

type Scenario =
    | Intro

val scenarioName : Scenario -> string
val all : Scenario list
```

**Guarantees**

- Consumes nothing; names no zone, asset, screen or device.
- `all` is derived from the union, so a case cannot be omitted; its order is declaration order.
- Adding a scenario is adding a case — nothing else lists them.

## `world/ScenarioZone.fs` — the ref-free authored zone (after `world/Zone.fs`)

```fsharp
module ScenarioZone

type ScenarioEnvironment =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      Parts : PropState array }

type ScenarioWaypoint =
    { Base : Entity
      Next : int option }

type ScenarioZone =
    { Environment : ScenarioEnvironment option
      Light : Texture.LightSettings
      Actors : ActorState array
      Props : PropState array
      Waypoints : ScenarioWaypoint array
      Triggers : TriggerState array
      Player : int option }

val capture : Zone.Zone -> ScenarioZone
```

**Guarantees**

- **Ref-free:** no `EntityRef`, no live `Environment` (and so no epoch), no slot handle survives a capture —
  waypoint links and the player designation are indices (FR-012). This is the whole point of a separate type:
  `ZoneContents` keeps those, and a scenario must not.
- `capture` is pure and device-free, so it is asserted headlessly. It de-refs in two passes for waypoint chains
  (collect, then resolve `Next` to an index).
- There is no `occupy` yet: loading a scenario into fresh zones (the fork) is the later increment.

## `world/ScenarioRegistry.fs` — the store (after `world/ScenarioZone.fs`)

```fsharp
module ScenarioRegistry

type ScenarioContents =
    { Zones : Map<Place, ScenarioZone>
      Active : Place
      Cue : Cue.Cue }

type Registry =
    { Entries : Dictionary<Scenario, ScenarioContents> }

val create : unit -> Registry
val tryContents : Scenario -> Registry -> ScenarioContents option
val setContents : Scenario -> ScenarioContents option -> Registry -> unit
val capture : Dictionary<Place, Zone.Zone> -> Place -> Cue.Cue -> ScenarioContents
```

**Guarantees**

- `tryContents` answers `None` for a scenario that holds no contents — an expected absence, never a sentinel.
- `setContents` is the one write path: `Some` files, `None` removes; it is the only place the option rule lives.
- `capture` is pure and device-free: it maps each live zone through `ScenarioZone.capture` and builds an
  **immutable** `Map`, so the result is safe to hold as an undo snapshot.

## `state/Command.fs` + `state/History.fs` — the step (state-level)

```fsharp
// Command.fs
| SetScenario of Name : Scenario
              * Previous : ScenarioContents option
              * Next : ScenarioContents option
```

**Guarantees**

- `Command.apply` / `Command.undo` (zone-level) **refuse** `SetScenario` with the other state-level steps
  (`ZoneMove`/`ZoneAdd`/`ZoneDelete`/`SetCue`/`Advent`) — it is not a zone edit.
- `History.apply` writes `setContents Name Next`; `History.undo` writes `setContents Name Previous`. Neither
  touches a zone, entity, camera or selection.
- `History.commit` applies then pushes — the panel's only write path, so a registration is one step on the one
  history.

## `editor/ScenarioPanel.fs` — the panel

```fsharp
val draw : SharedState.SharedState -> byref<GameplayState.GameplayState> -> unit
```

**Guarantees**

- Draws the `Scenario` window; enumerates `Scenario.all`; one row each, showing whether it holds contents.
- **Register** (always offered): commits `SetScenario (s, tryContents s state.Scenarios, Some (capture state.Zones state.ActiveZone state.Cue))`.
- **Clear** (offered only when it holds contents): commits `SetScenario (s, tryContents s state.Scenarios, None)`.
- Shows, for a scenario that holds contents, the cast characters it contains as a tag list (FR-013) — derived
  from the stored zones' actors' authored roles, aggregated and sorted, in the shape the Outliner's zone rows use.
- Never runs, forks or alters the scene; adds no cue vocabulary.

## Wiring contract

- `state/GameplayState.fs`: `Scenarios : ScenarioRegistry.Registry`, seeded with `ScenarioRegistry.create ()`.
- `editor/Editor.fs`: `"Scenario"` in `panelNames`; `ScenarioPanel.draw shared &state` submitted with the
  left-docked panels.
- `native/cimgui_sdl3.cpp`: `DockBuilderDockWindow("Scenario", left)` beside `"Cast"` / `"Zone Registry"`.
- `fsharp-game.fsproj`: `world/Scenario.fs` before `world/Cue.fs`; `world/ScenarioZone.fs` after
  `world/Zone.fs`; `world/ScenarioRegistry.fs` after `world/ScenarioZone.fs`; `editor/ScenarioPanel.fs` before
  `editor/Editor.fs`.
