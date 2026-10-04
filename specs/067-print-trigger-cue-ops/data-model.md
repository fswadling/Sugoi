# Data Model: Print & Trigger Cue Ops

## `world/Cue.fs` — the ops

```fsharp
    // The trigger toggles (spec 067): arm or disarm a named trigger in a named place. A no-progress leaf; the
    // flag is authored state, so the op sets it and the next play tick recomputes the derived runtime state.
    | EnableTrigger of place: Place * trigger: string
    | DisableTrigger of place: Place * trigger: string
    // Print (spec 067): a developer-facing line written to the console where the walk reaches it. A no-progress
    // leaf — it is not Exposit, which is player-facing dialogue (spec 057).
    | Print of text: string
```

No `…State` case: none of the three spans time. Add them to the module's no-progress note (which lists
`Exposit`, `SetZone`, `SetActor`, `EnablePlayer`, …).

## `world/Zone.fs` — the lookup

```fsharp
// The first live trigger named `name`, in slot order, or None. Names are not unique: the first match wins,
// deterministically — the same rule `tryWaypointByName` follows. Used by the cue's trigger ops.
let tryTriggerByName (zone: Zone) (name: string) : EntityRef option
```

The `Trigger` payload is read with the existing `Zone.tryTriggerAt` (`Trigger.State : TriggerState`,
`TriggerState.Base.Enabled`) and written with the existing `Zone.setTrigger`.

## `world/CueEngine.fs` — the effects

| Op | Condition | Effect |
|----|-----------|--------|
| `Print text` | — | `printfn "cue: %s" text`; `Fin` |
| `EnableTrigger (place, name)` / `DisableTrigger (place, name)` | place not present | no-op (log `place '%s' is not present`); `Fin` |
| " | `tryTriggerByName` misses | no-op (log `no trigger named '%s' in '%s'`); `Fin` |
| " | the flag already holds | no-op (log `it is already enabled/disabled`); `Fin` |
| " | otherwise | `Zone.setTrigger zone ref { state with Base = { state.Base with Enabled = … } }`; `Fin` |

A small diagnostic is added because `noOp` is actor-shaped:

```fsharp
let private noOpTrigger (op: string) (place: Place) (trigger: string) (reason: string) : unit =
    eprintfn "cue: %s(%s/%s) performs nothing: %s" op (Place.placeName place) trigger reason
```

The toggle sets only the authored `Enabled` flag. The runtime `Triggered` / tracked-prompt state is recomputed
by the next `Systems.updateTriggers`, so disabling the trigger the player is standing in clears the prompt on
the next tick and a disabled trigger neither fires nor tracks.

## `editor/CueTree.fs` — the authoring control

- `kinds`: a new **Triggers** group (`Enable trigger`, `Disable trigger`); `Print` under a **Debug** group
  (or **Timing**).
- `kindName` / `label`: `Print: <text>`; `Enable trigger: <name> in <place>`; `Disable trigger: <name> in <place>`.
- `executing`: all three are `false` (no in-flight state).
- `children`: `[]`.
- `defaultOfKind`: `Print ""`; `EnableTrigger(Place.start, "")`; `DisableTrigger(Place.start, "")`.
- `withPlace`: extend to the two toggles (write the place).
- `withTrigger` (new): write the trigger name into either toggle.
- `textOf` / `withText`: `Print`'s text (so the panel's generic text field draws it).
- `drawEditor`: extend the existing place-combo arm to cover the two toggles (the place is a closed set, so the
  control offers it with no zone read).

## `editor/CueEditor.fs` — the panel

`drawNodeText` gains a branch for `EnableTrigger (place, current) | DisableTrigger (place, current)`: a combo
over the **named place's** triggers (resolved through `zoneOfPlace place`), reading each trigger's
`Base.Name`, committed through `CueTree.withTrigger`; a typed field when the place is not present. `Print`
falls through to the generic text field. No `EditorState` field is added (a combo needs no buffer).

## Validation rules

- **Place-scoped**: a toggle resolves the trigger in the named place's zone; a place that is not present is a no-op.
- **First match wins** on a shared trigger name, deterministically.
- **Flag only**: the op sets `Enabled`; derived runtime state is the trigger system's.
- **Re-run safe**: absent place, missing name and redundant toggle are no-ops (FR-003), each reported on stderr.
- **Runtime only**: no op writes the undo history (FR-005); `Print` has no state and no undo.
