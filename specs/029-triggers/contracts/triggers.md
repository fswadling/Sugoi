# Contract: Triggers

Public surface added or changed by this feature. Signatures are illustrative F#.
A trigger composes the shared entity base, is stored like a waypoint, is edited
through one command, and is detected by one system function. Nothing runs a cue.

## Entity — `world/Entity.fs`

```fsharp
// A cylindrical volume that detects the player. Composes the shared base entity
// (name, transform, editor flags); carries no renderer, collider, or asset.
// `Triggered` is runtime state, written by the update and never authored.
[<Struct>]
type Trigger =
    { Base : Entity
      Radius : float32
      Height : float32
      Interactable : bool
      Prompt : string
      mutable Triggered : bool }

val triggerRef : index:uint32 -> generation:uint32 -> EntityRef
```

- `Base.Transform.Position` is the centre of the base; `Height` extends upward;
  `Radius` is the cross-section. `Rotation` and `Scale` do not affect the volume.
- `Base.Enabled` is the trigger's disabled flag.

## Zone — `world/Zone.fs`

```fsharp
// Added field (capacity 32): Triggers : SlotMap<Trigger>

// Replace a trigger's payload. Raises for a ref of another kind; no-op for a stale
// or absent one.
val setTrigger : zone:Zone -> ref:EntityRef -> trigger:Trigger -> unit
```

Existing operations gaining a trigger case (signatures unchanged): `isAlive`,
`tryEntityAt`, `setTransform`, `removeEntity`, `setActorWithAsset`, `bindingAt`
(answering `bindingNone`). A trigger never appears in `BodyToEntity` and never
carries a body, renderer, or binding.

## Systems — `world/Systems.fs`

```fsharp
// Update every trigger's inside state from the player's position and answer the
// nearest interactable trigger the player is inside, or None. With no live
// designated player, every trigger is cleared and the answer is `ValueNone`. Needs no
// graphics device and no physics world.
val updateTriggers : zone:Zone -> EntityRef voption
```

- Containment: horizontal distance to the base `<= Radius`, and the player's
  height between the base and `base + Height`, inclusive.
- A disabled trigger is never inside and never the answer.
- The player is the designated actor, at its entity position (already synced from
  its body earlier in the frame).

## Command — `state/Command.fs`

```fsharp
// One authored change to a trigger's payload: shape, interactable flag, prompt, or
// the enabled flag. A move or rotate remains a Transform command.
| TriggerSet of Ref : EntityRef * Before : Trigger * After : Trigger
```

- `apply` writes `After` via `Zone.setTrigger`, `undo` writes `Before`; neither
  merges, so one edit is one step.
- The runtime flag rides along in the records but is never authored.

## Gameplay state — `state/GameplayState.fs`

```fsharp
// Added field: mutable InteractableTrigger : EntityRef voption
```

- Assigned each play frame from `updateTriggers`, and cleared by it when nothing
  qualifies. Read by the play-mode overlay, and by the interaction once it exists.

## Render — `render/DebugDraw.fs` and `render/ImGui.fs`

```fsharp
// A cylinder wireframe: two 16-segment rings at the ends and a vertical line at each
// segment angle. Matches the family's centre/half-extent form.
val cylinder : dd:DebugDraw -> center:Vector3 -> halfHeight:float32 -> radius:float32 -> c:Color -> DebugDraw

// The one new P/Invoke; already exported by the vendored libcimgui.so.
[DllImport("cimgui", EntryPoint = "igDragFloat", ...)]
extern bool dragFloat(string label, float32& v, float32 speed, float32 min, float32 max, string format, int flags)
```

- `isItemDeactivatedAfterEdit` (bound by `specs/027-entity-rename`) is the commit
  signal for a drag, so one drag is one undo step.

## Editor — `editor/`

- `Spawning.spawnTrigger`: create a trigger in the active zone, push the reversing
  `Spawn` command, and select it; `duplicate` gains a `Trigger` case (the `Entity`
  base is copied and the payload reconstructed, matching the waypoint case).
- `Outliner`: the Triggers section lists the active zone's triggers with the shared
  row (select, *Del*, *Dup*) and an *Add Trigger* button.
- `Inspector`: a trigger block with drag floats for radius and height, a checkbox
  for interactable, a text field for the prompt, and a checkbox for enabled (which
  writes `Base.Enabled`). Each committed edit is one `TriggerSet` step. The
  trigger's runtime `Triggered` flag is not shown.
- `Viewport.buildLines`: a cylinder wireframe per trigger in the active zone, at
  the volume's centre (base + half the height), in the selection colour when
  selected and a dedicated trigger colour otherwise.

## Screen — `screen/GameplayScreen.fs`

- While playing (after the body→entity sync): `state.InteractableTrigger <- Systems.updateTriggers zone`.
- While playing, draw the overlay: the tracked interactable trigger's prompt (when
  it has one), and the names of the triggers the player is inside.
- Editing is unchanged: the update is not called, and no trigger state is written.
