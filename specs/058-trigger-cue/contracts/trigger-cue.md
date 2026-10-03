# Contract: Trigger Cue

The surfaces this feature adds or changes, and what explicitly does not change.

## `world/Cue.fs` — MOVED, CHANGED

```fsharp
// now compiled before world/Entity.fs
let forkInto (joined: Cue) (current: Cue) : Cue =
    match current with
    | Fin -> joined
    | _ -> Parallel(current, joined)
```

**Contract**: the cue is still one type and its run. `forkInto` (relocated from `world/CueEngine.fs`, body unchanged)
is the *only* way an arriving cue joins a running one; it runs the arrived cue beside the running one and disturbs
nothing. The module still names no zone, screen, asset or device (research D2, D3).

## `world/Entity.fs` — CHANGED

```fsharp
[<Struct>]
type TriggerState =
    { Base : Entity; Radius : float32; Height : float32; Interactable : bool; Prompt : string
      Cue : Cue.Cue }
```

**Contract**: a trigger's authored value now includes its cue; it still carries no renderer, collider, asset or runtime
flag. `TriggerState` stays a valid undo payload (a value, no resource) so `TriggerCreate`/`TriggerDelete`/`TriggerSet`
need no change (FR-001, FR-008, FR-012).

## `world/Zone.fs` — CHANGED

`defaultTrigger` adds `Cue = Cue.Fin`. No other `Zone` function changes.

## `world/Systems.fs` — CHANGED

```fsharp
let forkTriggerCue (zone: Zone.Zone) (ref: EntityRef voption) (cue: byref<Cue>) : unit
let updateTriggers (zone: Zone.Zone) (closest: byref<EntityRef voption>) (cue: byref<Cue>) : unit
```

**Contract**: detection is still one device-free pass over the active zone's triggers that reads/writes each payload
byref and allocates nothing on the idle path. `updateTriggers` additionally forks, on the rising edge, the cue of an
enabled, non-interactable trigger that has one. `forkTriggerCue` is the single "resolve a ref, skip `Fin`, `forkInto`"
rule, exercised headlessly (FR-002..FR-005).

## `screen/GameplayScreen.fs` — CHANGED

```fsharp
do Systems.updateTriggers zone &state.InteractableTrigger &state.Cue
// ... after the rest of the detection, still before CueEngine.tick ...
if Input.isPressed Input.Key.E frame.Input then
    do Systems.forkTriggerCue zone state.InteractableTrigger &state.Cue
```

**Contract**: the play chain and its order are otherwise unchanged — the cue still ticks last, so a fired cue begins
on the following tick. An interact press forks the tracked interactable trigger's cue; entry alone never does
(FR-004).

## `state/EditorState.fs` — CHANGED

New transient fields: `TriggerCueSelection`, `TriggerCueRef`, `TriggerCueTextBuffer`, `TriggerCueTextPath`,
`TriggerCueEditing`. **Contract**: Inspector UI scaffolding only; never written into a cue, never a command, reset when
the selected trigger changes (FR-007, FR-010).

## `editor/CueTree.fs` — MOVED (above `Inspector.fs`)

Unchanged control: same rows, lock rule, path helpers, node editor. **Contract**: it still names `Cue`, `Prime`, `ImGui`
and `Advent` only, so it remains reusable by the Inspector (FR-007).

## `editor/CueEditor.fs` — MOVED, CHANGED

```fsharp
let drawTrigger (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit
```

The per-node text and waypoint-combo helpers take the transient field state and a staged commit, so the Cue panel and
the inline editor share them. **Contract**: `draw` (the Cue panel) keeps taking only the gameplay state and writing
`SetCue`; `drawTrigger` writes `TriggerSet` through `Selection.updateTriggerRef`. The two use distinct ImGui id
prefixes and distinct `EditorState` fields, so they cannot collide (FR-007, FR-011).

## `editor/Inspector.fs` — CHANGED

The trigger section calls `CueEditor.drawTrigger shared &state ref`. **Contract**: the Inspector stays the single
editing surface; the cue is drawn inline for the selected trigger, as `docs/EDITOR_DESIGN.md` specifies.

## `fsharp-game.fsproj` — CHANGED

Three compile-order moves: `world/Cue.fs` above `world/Entity.fs`; `editor/CueTree.fs` and `editor/CueEditor.fs` above
`editor/Inspector.fs` (after `editor/Selection.fs`). No new file.

## Explicitly unchanged

- **`state/Command.fs`, `state/History.fs`** — `TriggerSet` already carries the whole `TriggerState`; no command or
  runner changes (research D7).
- **`state/GameplayState.fs`** — no new field; the fire writes the existing `Cue`.
- **`world/CueEngine.fs`** — `advance`/`tick` unchanged; it loses `forkInto` (relocated).
- **`editor/Selection.fs`** — `updateTriggerRef` is the commit path, unchanged.
- **`editor/Viewport.fs`, `editor/Outliner.fs`, `editor/Spawning.fs`** — a trigger's cue is drawn in the Inspector and
  rides the existing commands; duplicate/delete need no change.
- **Scenario library** — a scenario's cue, and a trigger's cue that names a scenario, are phase 11.

## `test/SystemsTests.fs`, `test/CuePanelTests.fs` — CHANGED

**Contract**: `SystemsTests` passes the new cue argument and gains entry-fire and `forkTriggerCue` cases; `CuePanelTests`
gains the inline trigger editor's commit-through-`TriggerSet` and reset-on-selection-change cases. Neither opens a
device (FR-006).
