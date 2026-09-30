# Data Model: Undoable Player Designation

The feature adds one command and one recording helper. It adds **no** data: the value it moves is the one
`Zone.Player` already holds, and `Zone`, `Systems` and `ZoneTemplate` are untouched.

## `state/Command.fs` — changed

One case joins the union, next to the other two zone-level cases (`SetEnvironment`, `SetLight`):

```fsharp
    // Zone-level rather than an entity: the player designation (spec 045). The payload is the value
    // `Zone.Player` already holds, so no new type is introduced; `Previous` is what lets the undo of a
    // re-designation restore the *previous actor* rather than "no player".
    | SetPlayer of Previous : Handle voption * Next : Handle voption
```

and its two arms, which are total and symmetric:

```fsharp
    // apply
    | SetPlayer (_, next) ->
        zone.Player <- next

    // undo
    | SetPlayer (previous, _) ->
        zone.Player <- previous
```

`Handle` is `core/SlotMap.fs`'s `[<Struct>]` `{ Index : uint32; Generation : uint32 }`, so a payload is a
value option of two words — no boxing, and no allocation at the command site beyond the command value
itself, which is built once per click.

## `editor/Selection.fs` — changed

The recording helper, shaped like its neighbour `setLight`:

```fsharp
/// Set the actor at `ref` as the active zone's player, as one undoable step. A ref that is not an actor
/// fails loudly in `Systems.playerHandleOf` before any command exists; re-designating the actor who is
/// already the player records nothing.
let setPlayer (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let zone = GameplayState.activeZone state
    let before = zone.Player
    let after = ValueSome (Systems.playerHandleOf ref)
    if after = before then () else

    let cmd = SetPlayer (before, after)
    apply zone shared.Physics shared.Assets cmd
    let undo = UndoStack.push state.Editor.Undo cmd
    state.Editor <- { state.Editor with Undo = undo }
```

Three properties are the contract here:

- **The zone is resolved once**, from the active zone — the same zone the Inspector is showing a
  selection from, so a designation is always made in the zone it will be undone in (FR-003).
- **`after = before` records nothing** — the FR-004 rule, and the only place it can be decided, since
  `Command.apply` cannot know whether the value changed.
- **Apply-then-push** — the stack holds the command only after it has been applied, so the stacks and the
  zone can never disagree (`setLight` does the same).

## `Zone.Player` — unchanged, and the only state

```fsharp
mutable Player : Handle voption   // world/Zone.fs
```

| State | Meaning | Read by |
|---|---|---|
| `ValueNone` | the zone drives no character | `Systems.isPlayerLive` → false |
| `ValueSome handle` *live* | that actor is the avatar | `playerInput`, `cameraFollow`, `animate`, `updateTriggers` |
| `ValueSome handle` *stale* | the actor is gone (or its slot was reused) | `isPlayerLive` → false; every system no-ops |

Validation rules, all of them existing ones this feature does not disturb:

- **A designation is never trusted as-is.** Every reader goes through `isPlayerLive`, which requires
  `zone.Actors.IsAlive handle`; a handle whose `Generation` no longer matches is simply not live. This is
  what makes a *restored* designation safe (Edge Cases).
- **A ref that is not an actor is a bug, not a no-op**: `Systems.playerHandleOf` fails loudly, before a
  command can be built (constitution II).
- **An unchanged designation is not a step** (FR-004), decided in `Selection.setPlayer` above.
- **The value is per zone and never aliased**: handles are per zone and generation-checked, so the same
  index in another zone resolves to nothing (`test/GameplayStateTests.fs` already asserts this).

## State transitions

One click, one step; undo is the inverse of the click, not of "no player".

| Before | Action | After | Steps pushed | One undo |
|---|---|---|---|---|
| no player | set actor A | A | 1 | no player |
| A | set actor B | B | 1 | A |
| A | set actor A | A | 0 | *(unchanged history)* |
| A | undo | no player *(or the actor designated before A)* | — | — |
| A | redo | A | — | — |

## `docs/EDITOR_DESIGN.md` — changed

- **Principle 4** ("Undo everywhere"): the direct-write exceptions become the collider overlay's
  visualisation preference and the cast binding. The player designation leaves the list, because the
  property that put it there — that it *outlives a zone* — is not true of it: it lives on the zone
  (`docs/ARCHITECTURE.md`: "the player designation and the undo history are held per zone").
- **D-022** (the cast panel): the sentence offering the designation as a precedent for a direct write
  loses it. That sentence is where the misclassification is actually written down, and it has to stop
  claiming the designation is a binding action. **D-011** needs no change: it lists only the
  project-scoped surfaces and never mentions the designation.
- **D-025** (new, in the same style as D-022/D-023/D-024): why the designation is a command — it is a
  zone's own setting, so its undo rides the zone's own stack, which is D-024's reasoning for the light
  applied to the other value of the same kind; and why the control reads its state.

## Deliberately not modelled

- **Clearing the designation** (FR-008): there is still no action for it, and no command is added for one.
  "No player" is reachable only by undoing a first designation (the reference's checkbox can clear; the
  port's button cannot, and this feature is about reversibility, not new capability).
- **The designation in a registered place** (FR-007): `ZoneTemplate` carries the environment, the props
  and the light, and still no player. Registering and instantiating are unchanged.
- **Any persistence**: the project has no save/load, so the designation is and stays session state.
