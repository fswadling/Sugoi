# Contract: Print & Trigger Cue Ops

What a cue can do to a trigger, and to the console — the last op-portable pieces of odin's cue family.

## The ops — `world/Cue.fs`

```fsharp
| EnableTrigger of place: Place * trigger: string
| DisableTrigger of place: Place * trigger: string
| Print of text: string
```

All three are no-progress leaves (no `…State`).

## Resolution

- **The place** is a value of the closed `Place` set; the toggle resolves its zone in `state.Zones`. Not
  present ⇒ the op performs nothing.
- **The trigger** is a name in that place's zone, resolved by `Zone.tryTriggerByName` (the first live trigger
  whose `Base.Name` matches, as `tryWaypointByName` does for waypoints). Absent ⇒ the op performs nothing.
- **The flag** is `TriggerState.Base.Enabled` (`specs/058`); the toggle sets it only.

## Behaviour

| Op | Does |
|----|------|
| `EnableTrigger (place, name)` | set that trigger's `Enabled` to true (no-op if already true) |
| `DisableTrigger (place, name)` | set it to false (no-op if already false) |
| `Print text` | write `cue: <text>` to the console at the point reached |

A toggle whose place is not present, whose trigger does not resolve, or whose effect already holds performs
nothing and reports its reason on stderr (the `specs/066` no-op convention), so a cue is safe to re-run. The
runtime `Triggered` / tracked-prompt state is left to the next `Systems.updateTriggers`.

## Postconditions

- **The flag flips** on the enabling/disabling op; the next play tick honours it (a disabled trigger does not
  fire nor track the player).
- **Re-run safe**: an absent place, a missing trigger, or a redundant toggle performs nothing and does not fault.
- **Runtime only**: no op is a step on the undo history (spec FR-005).

## Explicitly out of scope

- **The scenario ops** (`InlineScenario` / `ForkScenario` / `ClearScenario`) — ROADMAP phase 11.
- **The camera-follow and interactions toggles** (`Enable/DisableCameraFollow`, `Enable/DisableInteractions`).
- **New zone/actor vocabulary**, the undo history, and any player-facing text (that is `Exposit`).
