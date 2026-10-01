# Data Model: One undo history on the gameplay state

Three changes to the model: three new `Command` cases with the record one of them carries, the module where the
command *runners* live, and the removal of the per-zone side map. The existing per-kind command family, the
`UndoStack` type and the entity readers it is built from are unchanged.

## `state/Command.fs` — the type gains three cases

```fsharp
type Command =
    | Transform of Ref : EntityRef * Before : Transform * After : Transform
    | …                                    // UNCHANGED: every existing case, and `Batch`
    // The active zone moved (spec 048). No command carries a zone: recording the moves is what keeps the
    // active zone the one the step being undone belongs to (research D2).
    | ZoneMove of Previous : string * Next : string
    // A zone was added, and made active. `Previous` is the zone active before the add, restored by undo.
    | ZoneAdd of Previous : string * Name : string
    // A zone was deleted. `Previous` is the zone active before the delete — when the deleted zone *was*
    // active, that is the deleted zone itself, so undoing puts the developer back where they were (D3/D4).
    | ZoneDelete of Previous : string * Name : string * Contents : ZoneContents
```

`ZoneDelete` carries a `ZoneContents` — the authored contents of one zone, which lives in `world/Zone.fs` with the
reader and writer that build it (the next section).

## `world/Zone.fs` — the contents record and its reader/writer

```fsharp
type ZoneContents =
    { Actors : (EntityRef * ActorState) list
      Props : (EntityRef * PropState) list
      Waypoints : (EntityRef * Waypoint) list
      Triggers : (EntityRef * TriggerState) list
      Environment : Environment option     // carries its own parts (and their refs), so parts are not listed
      Light : Texture.LightSettings
      Player : Handle voption }

val captureZone : zone: Zone -> ZoneContents
val occupyZone  : zone: Zone -> physics: Physics.World -> assets: Assets.AssetManager -> contents: ZoneContents -> unit
```

- **Why the record lives here and not in `Command.fs`.** A command's payload is defined where the thing it
describes is — `ActorState`, `PropState`, `Environment` in `world/Entity.fs`, `LightSettings` in
`render/Texture.fs` — and `ZoneTemplate`, the same kind of composite recorded for a different purpose, is in
`world/` too. `ZoneContents` is a zone's record, and the functions that read and write it belong with the
`capture*`/`occupy*` family they mirror. `world/Zone.fs` is compiled before `state/Command.fs`, so the `ZoneDelete`
case can name it.
- **Why pairs of ref and state, and not a deep copy of `Zone`.** The command family's payload rule: a payload is
  the authored record and *cannot* express a physics attachment or a renderer (`state/Command.fs`'s header). A
  `Zone` holds live bodies (`Physics.BodyId`), renderers and an `EnvironmentBatch`, so retaining the `Zone` value
  would retain resources the zone no longer owns and give the restored zone two owners of the same bodies
  (research D4). The pairs are what `Zone.captureActor`/`captureProp`/`captureWaypoint`/`captureTrigger` already
  answer and what `ActorCreate`/`WaypointCreate`/… already carry.
- **Why `Environment` is the value and not a list of parts.** `Environment` owns its `Parts` slot map, so loading
  it restores the parts and their refs in one step — the same property the existing `SetEnvironment` command
  relies on (its undo moves the value rather than respawning).
- **Why no `Batch`, no `NextEnvironmentEpoch`, no `BodyToEntity`, no `Name` of the zone in `ZoneContents`.** The
  batch is rebuilt, the epoch counter is monotonic runtime state, the body map is rebuilt by `occupy*`, and the
  zone's name is `ZoneDelete`'s own field (the map key), not part of its contents.
- `captureZone` reads the live maps with the existing `capture*` readers plus `zone.Environment`, `zone.Light` and
  `zone.Player`. It is the one place a delete's snapshot is taken.
- `occupyZone` loads the environment, then `occupyActor`/`occupyProp`/`occupyWaypoint`/`occupyTrigger` for each
  recorded ref, then the light and the player — the same calls an entity create already makes, so a restored zone
  is built by the one path that builds live entities at all. It takes a `Zone` and nothing else from the state,
  which is why it is here; the zone's *existence* and the active zone are `GameplayState`'s business, and that is
  `History.fs`'s half (below).

## `state/History.fs` — new module, compiled after `state/GameplayState.fs`

```fsharp
/// The state-level runner: applies one command to the gameplay state. An entity or zone-value command goes to
/// the active zone; a zone step creates, removes or switches a zone.
val apply : SharedState.SharedState -> byref<GameplayState.GameplayState> -> Command -> unit

/// The inverse of `apply`.
val undo : SharedState.SharedState -> byref<GameplayState.GameplayState> -> Command -> unit

/// Apply a command and push it onto the one history (clearing redo). The editor's write path for a zone step.
val commit : SharedState.SharedState -> byref<GameplayState.GameplayState> -> Command -> unit

/// Step the one history back / forward, applying or reversing the command it reaches. A no-op on an empty side.
val undoHistory : SharedState.SharedState -> byref<GameplayState.GameplayState> -> unit
val redoHistory : SharedState.SharedState -> byref<GameplayState.GameplayState> -> unit
```

- **Why this module and not `Command.fs`.** Adding/removing a zone and setting `ActiveZone` are `GameplayState`
  operations, and `Command.fs` is compiled above it. Moving the *type* below the state is cyclic — `EditorState`
  names `UndoStack`, which names `Command` — so only the runners move (research D5). This is the second use of the
  project's one deliberate inversion (`world/CueEngine.fs` after `state/GameplayState.fs`).
- **`apply`/`undo` are named as they are in `Command.fs`** and take the state rather than a zone: the zone comes
  from `state.ActiveZone`, which is what makes "a command carries no zone" true in the code as well as on paper.
- **`ZoneMove`** is `switchZone Next` forwards and `switchZone Previous` back. It re-frames the camera and clears
  the selection by construction, because that *is* `switchZone` (research D6).
- **`ZoneAdd`** forwards is `addZone Name` (which makes it active); backwards is "make `Previous` active, then
  remove `Name`". The removed zone is empty — every edit made to it came later on the history and has been undone
  first — so its removal is a release of an empty zone.
- **`ZoneDelete`** forwards is `captureZone` (already taken, in the payload) then remove the zone; backwards is
  "create a zone named `Name`, `occupyZone` its contents, then `switchZone Previous`". When the deleted zone was
  not active, `Previous` is the still-active zone and the final switch is a no-op.

## `state/GameplayState.fs` — the per-zone side map goes

```fsharp
// REMOVED
UndoByZone : Dictionary<string, UndoStack>
```

- **`UndoByZone` is deleted**, and with it the two lines in `enterZone` that parked and loaded a stack. `enterZone`
  keeps its other work — dropping the zone-scoped selection/drag/nudge and re-framing the camera — because those
  are about what is being edited, not about the history.
- **No new removal function was needed.** The plan drew a `removeZone` equal to `deleteZone` without its guards, on
  the assumption that a guard could re-fire during an undo. It cannot: the last-zone guard tests the count *before*
  the removal, and removing a zone a later step added leaves at least one behind, so `deleteZone` — guards, switch
  away when it is the active zone, release and drop — is exactly what a `ZoneDelete` applies and what a `ZoneAdd`'s
  undo calls. One removal path, not two.
- **`switchZone`/`selectZone`/`addZone`/`deleteZone` are otherwise unchanged.** What changes is who calls them:
  the Outliner's zone controls go through `History.commit` (below), so the step is recorded.

## `state/EditorState.fs` — unchanged field, one meaning

```fsharp
Undo : UndoStack        // UNCHANGED TYPE — now the one history, not the active zone's
CueHistory : …          // UNCHANGED — the cue keeps its own history (spec 047, research D8)
```

`UndoStack` itself is unchanged: `{ Applied : Command list; Redo : Command list }`, with `create`/`push` staying
in `Command.fs`. Its zone-level `undo`/`redo` also stay, and `History` adds the **state-level** `undoHistory`/
`redoHistory` beside them.

## `state/Command.fs` — the steppers, split by what they need

| Function | Where it lives | Why |
|---|---|---|
| `create`, `push` (incl. the transform merge) | `Command.fs` | pure list operations; `EditorState` must name them above `GameplayState` |
| `undo`, `redo` — step a stack against **one zone** | `Command.fs`, unchanged | they need no gameplay state, so a bare `Zone` can drive them; the zone-level suite tests them that way, and they keep that coverage |
| `undoHistory`, `redoHistory` — step the one history on the **state** | `History.fs` | a step may be a zone move, an add or a delete, which only the state can carry out |

The merge rule is untouched: consecutive `Transform`s of the same ref collapse; a `ZoneMove` between them prevents
the merge, which the rule already does by refusing to merge anything that is not a `Transform` of the same ref
(research D7).

## Command semantics

| Command | `apply` | `undo` |
|---|---|---|
| `ZoneMove (previous, next)` | `switchZone next` | `switchZone previous` |
| `ZoneAdd (previous, name)` | `addZone name` (makes it active) | `switchZone previous`; `deleteZone name` |
| `ZoneDelete (previous, name, contents)` | `deleteZone name` (contents captured before) | create `name`; `occupyZone contents`; `switchZone previous` |
| every existing case | unchanged — the active zone | unchanged — the active zone |

## Validation rules

| Rule | Where it is enforced |
|---|---|
| A `ZoneMove` names zones that exist | `switchZone`, unchanged: it raises on an unknown name. The undo path can only name zones that exist, because the steps that removed one are later on the history and are undone first |
| A `ZoneAdd`'s name is non-empty and unused | the Outliner's disabled control, and `addZone`'s guard, unchanged |
| A `ZoneDelete` of the last zone is refused | `deleteZone`'s guard, unchanged |
| A restored zone's entities keep their identity | `occupyZone` occupies the recorded `EntityRef`s, the path an entity undo already takes (FR-009) |
| A `ZoneDelete`'s contents are authored state only | `ZoneContents` is built by `captureZone` from `capture*`; it cannot carry a body or a renderer |
| A `ZoneAdd`'s zone is empty when its undo removes it | the steps that filled it are later on the history and are undone first |
| An undo of a move re-frames the camera and clears the selection | `switchZone` does both; a move carries no camera pose (research D6) |
| Undo/redo does not fire while dragging or typing | `Editor.handleShortcuts`, unchanged (FR-013) |
