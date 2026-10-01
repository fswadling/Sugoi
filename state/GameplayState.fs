module GameplayState

// Per-screen gameplay state. A struct; owns the screen's zones (a name-keyed map
// with exactly one active zone), its view (camera + per-screen render buffers), and
// its editor/play state. Device-level render resources (pipelines, formats, depth
// target) live on `SharedState`, not here. A zone's name is its identity and its key;
// `activeZone` is one dictionary lookup, done once per frame by the hot paths.

open System.Collections.Generic
open System.Numerics
open Command
open Entity

[<Struct>]
type GameplayState =
    { mutable Camera : Camera.Camera
      mutable DebugDraw : DebugDraw.DebugDraw
      mutable GizmoDraw : DebugDraw.DebugDraw
      // The game's 2D layer (spec 038), held as the two debug drawers are: a per-screen
      // renderer the screen draws with. It owns the pipeline, the batch and the font.
      mutable Ui : Ui.Ui
      // The dialogue state (spec 038): the screen's, not a zone's, so a zone switch does
      // not clear it. Runtime only — never authored, never persisted, never on an entity.
      mutable Dialogue : Dialogue.Dialogue
      Zones : Dictionary<string, Zone.Zone>
      mutable ActiveZone : string
      mutable Editor : EditorState.EditorState
      mutable Editing : bool
      mutable Follow : Systems.CameraFollow
      // Per-zone undo/redo history, swapped in on a switch so an edit's undo always
      // applies to the zone it was recorded in.
      UndoByZone : Dictionary<string, UndoStack>
      // The interactable trigger the player was last inside: what an interact prompt is
      // shown for. Runtime state, set by the trigger system each play frame, never
      // authored, and never read while editing. A `voption`, like `Zone.Player`, so
      // re-answering it every frame allocates nothing.
      mutable InteractableTrigger : EntityRef voption
      // The cue (spec 046): the run itself — an immutable value that shrinks as it is stepped, down
      // to `Fin`, which is what a finished cue is and where it rolls. The screen's, not a zone's, so
      // a zone switch does not disturb it. `PlayTime` is play-mode time, advanced only by the play
      // chain, so an edit freezes it and Play resumes a cue rather than leaping it forward (FR-002).
      mutable Cue : Cue.Cue
      mutable PlayTime : float }

let create (debugDraw: DebugDraw.DebugDraw) (gizmoDraw: DebugDraw.DebugDraw) (ui: Ui.Ui) : GameplayState =
    let zones = Dictionary<string, Zone.Zone>()
    zones.["main"] <- Zone.create ()
    { Camera = Camera.create (Vector3(0.0f, 3.0f, 6.0f)) Vector3.Zero
      DebugDraw = debugDraw
      GizmoDraw = gizmoDraw
      Ui = ui
      Dialogue = Dialogue.empty
      Zones = zones
      ActiveZone = "main"
      Editor = EditorState.create ()
      Editing = true
      Follow = Systems.createFollow ()
      UndoByZone = Dictionary()
      InteractableTrigger = ValueNone
      Cue = Cue.Fin
      PlayTime = 0.0 }

// The active zone (one dictionary lookup).
let activeZone (state: GameplayState) : Zone.Zone =
    match state.Zones.TryGetValue state.ActiveZone with
    | true, zone -> zone
    | false, _ -> invalidOp (sprintf "activeZone: '%s' is not a zone" state.ActiveZone)

// After `ActiveZone` has changed: park the outgoing zone's undo stack, load the
// incoming one, drop the (zone-scoped) selection/drag/nudge, and re-frame the camera
// on the incoming zone.
let private enterZone (state: byref<GameplayState>) (outgoing: string) : unit =
    state.UndoByZone.[outgoing] <- state.Editor.Undo
    let incoming =
        match state.UndoByZone.TryGetValue state.ActiveZone with
        | true, stack -> stack
        | false, _ -> UndoStack.create ()
    state.Editor <- { state.Editor with Undo = incoming; Selection = None; Drag = None; NudgeBefore = None }

    let zone = activeZone state
    let pivot =
        match zone.Player with
        | ValueSome handle when zone.Actors.IsAlive handle ->
            let actor = &zone.Actors.Get handle
            actor.State.Base.Transform.Position + Vector3(0.0f, state.Follow.Height, 0.0f)
        | _ -> Vector3.Zero
    let offset = state.Camera.Position - state.Camera.Target
    state.Camera <- { state.Camera with Target = pivot; Position = pivot + offset }

// Detach the outgoing zone's bodies, make `next` active, attach its bodies, then
// swap the view state.
let private swapZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (outgoing: string) (next: string) : unit =
    do Zone.deactivatePhysics state.Zones.[outgoing] shared.Physics
    state.ActiveZone <- next
    do Zone.activatePhysics state.Zones.[next] shared.Physics
    enterZone &state outgoing

// Make a listed zone active. A no-op when it already is; an unknown name is a bug.
let switchZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "switchZone: '%s' is not a zone" name) else
    if name = state.ActiveZone then () else
    swapZone shared &state state.ActiveZone name

// Bring a zone up in the editor: make it active, and make it the Inspector's subject by dropping any
// entity selection (spec 043 — the Inspector shows the active zone's light when no entity is
// selected). A switch drops the selection on the way (`enterZone`), so the case this adds is the
// zone already active, where clicking its row is otherwise a no-op.
let selectZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "selectZone: '%s' is not a zone" name) else
    if name = state.ActiveZone then
        state.Editor <- { state.Editor with Selection = None; Drag = None }
    else
        switchZone shared &state name

// Add an empty zone under `name` and make it active. The name must be non-empty and
// unused; the editor only offers the add when it is.
let addZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) : unit =
    if name = "" then invalidOp "addZone: a zone name must not be empty" else
    if state.Zones.ContainsKey name then invalidOp (sprintf "addZone: zone '%s' already exists" name) else
    let outgoing = state.ActiveZone
    state.Zones.[name] <- Zone.create ()
    swapZone shared &state outgoing name

// Delete a zone and release everything it owns. The last zone cannot be deleted;
// deleting the active zone first makes another zone active.
let deleteZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "deleteZone: '%s' is not a zone" name) else
    if state.Zones.Count <= 1 then invalidOp "deleteZone: cannot delete the last zone" else

    if name = state.ActiveZone then
        let replacement = state.Zones.Keys |> Seq.filter (fun key -> key <> name) |> Seq.sort |> Seq.head
        swapZone shared &state name replacement

    do Zone.release state.Zones.[name] shared.Physics shared.Gpu.Device
    state.Zones.Remove name |> ignore
    state.UndoByZone.Remove name |> ignore
