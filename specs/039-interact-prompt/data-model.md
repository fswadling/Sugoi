# Data Model: Interact Prompt

Almost all of this feature's data already exists. It is presented here only as the boundary between
what is authored, what the systems resolve, and what the view draws.

## The trigger's authored prompt — `world/Zone.fs` (unchanged, spec 029)

```fsharp
[<Struct>]
type TriggerState =
    { Base : Entity            // name, transform (position/size carry the volume), flags
      Height : float32
      Radius : float32
      Interactable : bool      // whether the prompt is shown when the player is inside
      Prompt : string }        // the authored line; "" means "say nothing"

[<Struct>]
type Trigger =
    { State : TriggerState
      Triggered : bool }       // runtime only, set by the play pass
```

The prompt and the flag are **authored** (the trigger Inspector's "Interactable" checkbox and "Prompt"
field, spec 029) and unchanged by this feature. A trigger with `Interactable = false` or
`Prompt = ""` never produces a prompt.

## The resolved prompt — a string, resolved per frame

The systems already resolve *which* trigger counts (spec 029, unchanged):

- `Systems.updateTriggers (zone) (closest: byref<EntityRef voption>)` walks the zone's triggers and
  writes the **nearest enabled, interactable** one the player is inside — distance to the volume's
  centre — or `ValueNone`. This is `GameplayState.InteractableTrigger`.
- `Zone.tryTriggerAt : Zone -> EntityRef -> Trigger voption` answers that ref's live trigger, as a
  `voption` (a struct) so the per-frame read allocates nothing.

The screen derives the drawn text from those, adding the two gates this feature owns:

```
prompt =
    if editing or dialogue.Shown then ""           // FR-012, and play mode only
    else match InteractableTrigger with
         | ValueSome ref ->
             match Zone.tryTriggerAt zone ref with
             | ValueSome trigger -> trigger.State.Prompt      // FR-002: exactly as authored
             | ValueNone -> ""
         | ValueNone -> ""
```

The written form is an explicit `match`, not `ValueOption.bind (Zone.tryTriggerAt zone)`: the latter
closes over `zone` and allocates a closure every frame (FR-010).

## The pure layout — `world/PromptRender.fs` (new)

```fsharp
/// The prompt's text block at this viewport: centred horizontally, sitting above a proportional
/// bottom margin. Pure, so it is asserted headlessly.
val layout : viewportW:float32 -> viewportH:float32 -> textW:float32 -> textH:float32 -> Ui.Rect
```

Every figure is a fraction of the viewport, so the prompt stays on screen at any size (FR-007): the
horizontal centre from `viewportW`, and the bottom margin from `viewportH`. The returned `Ui.Rect` is
the text block's top-left corner and its measured size — the same shape `DialogueRender.layout`
returns for its sub-rects. The shadow's rect is this rect offset by a small constant, computed in
`draw`.

The palette is one small struct, as `DialogueRender`'s is:

```fsharp
[<Struct>]
type private PromptColours =
    { Text : Vector4      // a near-white
      Shadow : Vector4 }  // a semi-transparent near-black, drawn offset for contrast (FR-008)
```

## Authored versus runtime boundary

| | Authored | Runtime |
|---|---|---|
| A trigger's `Interactable` flag and `Prompt` text | yes — the trigger's Inspector (spec 029) | no |
| Which trigger is the player's (the resolved prompt string) | no | yes — recomputed each play frame; never persisted |
| The prompt's layout and colours | no (presentation constants) | no |

Nothing new is authored, and no authored format changes.

## Validation rules

- **An absent or empty prompt draws nothing** — no box, no empty line (FR-005). It is an expected
  state, not a failure.
- **Only one prompt is ever resolved.** The systems pick the nearest interactable trigger, so the view
  draws one string, never several (FR-004).
- **The string is drawn as authored.** The screen neither prefixes, alters nor trims it (FR-002).
- **A prompt longer than the window is wrapped** to a maximum width, so it stays on screen rather than
  running off an edge (FR-007); the block then grows upward, still centred and bottom-anchored.
- **With nothing to show, nothing is measured or drawn**, so the frame is the pre-feature frame
  (FR-009).
