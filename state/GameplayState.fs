module GameplayState

// Per-screen gameplay state. A struct; owns the gameplay world and that world's configuration —
// the zones (a place-keyed map with exactly one active zone), the zone registry (the recipe table
// those zones are instantiated from), and the physics world their bodies attach to — beside the
// screen's own runtime state (camera + render buffers, dialogue, the cue run, editor/play state).
// The test for what belongs here is readership: a member is the gameplay screen's (and its editor
// layer's) when nothing else reads it. Device-level render resources (pipelines, formats, depth
// target) live on `SharedState`, not here. A zone's name is its identity and its key; `activeZone`
// is one dictionary lookup, done once per frame by the hot paths.

open System.Collections.Generic
open System.Numerics
open Command
open Entity
open Place
open ZoneTemplate

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
      // The gameplay state's zones, keyed by the place they are (spec 061). A place is either present
      // (it has a zone here) or absent; the active place is always present.
      Zones : Dictionary<Place, Zone.Zone>
      // The zone registry (spec 034): the recipe table for those zones — the config `presentZone`/
      // `populateZone` instantiate a place from. Here rather than on `SharedState` because only the
      // gameplay screen and its editor read it.
      ZoneRegistry : ZoneRegistry.Registry
      // The scenario library (spec 070): the contents captured under each scenario. Here beside the zone registry
      // for the same reason — only the gameplay screen and its editor read it.
      Scenarios : ScenarioRegistry.Registry
      // The physics world: one Jolt world shared by every zone (a zone keeps only its body→entity
      // map), owned here beside the zones because only the gameplay screen simulates. A zone never
      // holds it — `Zone.*` receives it as a parameter, so there is one world for the whole game.
      Physics : Physics.World
      // The runtime cast (spec 072): resolving which actor plays a character is a name scan over the present
      // places, not an index (`tryActorByName` below). There is no map to keep exact.
      mutable ActiveZone : Place
      mutable Editor : EditorState.EditorState
      mutable Editing : bool
      mutable Follow : Systems.CameraFollow
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
      mutable PlayTime : float
      // The screen's fade level (0 = the game view untouched, 1 = fully black). Screen-scoped and
      // runtime-only: a zone switch does not touch it, and it is never authored, persisted, or a step
      // on the undo history. The cue ops FadeOut/FadeIn will drive it when they arrive.
      mutable Fade : float32 }

let create (debugDraw: DebugDraw.DebugDraw) (gizmoDraw: DebugDraw.DebugDraw) (ui: Ui.Ui) : GameplayState =
    let zones = Dictionary<Place, Zone.Zone>()
    zones.[Place.start] <- Zone.create ()
    { Camera = Camera.create (Vector3(0.0f, 3.0f, 6.0f)) Vector3.Zero
      DebugDraw = debugDraw
      GizmoDraw = gizmoDraw
      Ui = ui
      Dialogue = Dialogue.empty
      Zones = zones
      ZoneRegistry = ZoneRegistry.create ()
      Scenarios = ScenarioRegistry.create ()
      Physics = Physics.create ()
      ActiveZone = Place.start
      Editor = EditorState.create ()
      Editing = true
      Follow = Systems.createFollow ()
      InteractableTrigger = ValueNone
      Cue = Cue.Fin
      PlayTime = 0.0
      Fade = 0.0f }

// The active zone (one dictionary lookup).
let activeZone (state: GameplayState) : Zone.Zone =
    match state.Zones.TryGetValue state.ActiveZone with
    | true, zone -> zone
    | false, _ -> invalidOp (sprintf "activeZone: '%s' is not present" (Place.placeName state.ActiveZone))

// ── The runtime cast (spec 072) ──

// The actor playing `character`: the first live actor whose `Name` is the character's canonical name, scanning
// the present places in `Place.all` order and returning the place, its zone and the ref. A name is a label, not
// an identity, so a duplicate resolves to the first match; there is no index to keep exact. Global: it does not
// depend on which place is active, and `None` means no present place holds an actor of that name.
let tryActorByName (state: GameplayState) (name: string) : (Place * Zone.Zone * EntityRef) option =
    let mutable found = None
    for place in Place.all do
        if found.IsNone then
            match state.Zones.TryGetValue place with
            | true, zone ->
                match Zone.tryActorByName zone name with
                | Some ref -> found <- Some (place, zone, ref)
                | None -> ()
            | false, _ -> ()
    found

// After `ActiveZone` has changed: drop the (zone-scoped) selection/drag/nudge and re-frame the camera on the
// incoming zone. The undo history is **not** touched: it is one timeline for the whole gameplay state, and the
// zone moves that carry it are commands on it (spec 048), so nothing is parked and nothing is loaded.
let private enterZone (state: byref<GameplayState>) : unit =
    state.Editor <- { state.Editor with Selection = None; Drag = None; NudgeBefore = None }

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
let private swapZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (outgoing: Place) (next: Place) : unit =
    // Request the incoming environment. This never blocks: the switch completes in-frame and the
    // environment pops in when it lands (FR-004). The pin set is a gameplay-layer concern, refreshed
    // to the active environment every frame (`GameplayScreen.update`), so it is not touched here.
    match state.Zones.[next].Environment with
    | Some environment -> do Assets.requestEnvironment shared.Assets environment.AssetId
    | None -> ()
    do Zone.deactivatePhysics state.Zones.[outgoing] state.Physics
    state.ActiveZone <- next
    do Zone.activatePhysics state.Zones.[next] state.Physics
    enterZone &state

// Make a present place active. A no-op when it already is; an absent place is a bug.
let switchZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: Place) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "switchZone: '%s' is not present" (Place.placeName name)) else
    if name = state.ActiveZone then () else
    swapZone shared &state state.ActiveZone name

// Bring a zone up in the editor: make it active, and make it the Inspector's subject by dropping any
// entity selection (spec 043 — the Inspector shows the active zone's light when no entity is
// selected). A switch drops the selection on the way (`enterZone`), so the case this adds is the
// zone already active, where clicking its row is otherwise a no-op.
let selectZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: Place) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "selectZone: '%s' is not present" (Place.placeName name)) else
    if name = state.ActiveZone then
        state.Editor <- { state.Editor with Selection = None; Drag = None }
    else
        switchZone shared &state name

// Make an absent declared place present and active. A place already present is a bug — the editor offers
// only the absent ones (spec 061 FR-003).
let addZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: Place) : unit =
    if state.Zones.ContainsKey name then invalidOp (sprintf "addZone: '%s' is already present" (Place.placeName name)) else
    let outgoing = state.ActiveZone
    state.Zones.[name] <- Zone.create ()
    swapZone shared &state outgoing name

// Populate a present zone from a registered template: its environment, light and placed props (spec 062).
// `Physics.createBody` joins the simulation as it runs, so a zone that is not the active one has its built
// bodies detached at once — the same "alive but detached" state a switch away leaves it in.
let populateZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: Place) (template: ZoneTemplate) : unit =
    let zone = state.Zones.[name]
    do Zone.loadTemplate zone state.Physics shared.Assets template
    if name <> state.ActiveZone then Zone.deactivatePhysics zone state.Physics

// Make an absent declared place present, populated from its registered template when it has one, and leave
// the active place, the camera and the selection untouched (spec 062): a cue *prepares* a zone here, and
// `switchZone` is what enters it. A place already present is a bug — the cue guards it — so this raises.
let presentZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: Place) (template: ZoneTemplate option) : unit =
    if state.Zones.ContainsKey name then invalidOp (sprintf "presentZone: '%s' is already present" (Place.placeName name)) else
    state.Zones.[name] <- Zone.create ()
    match template with
    | Some template -> do populateZone shared &state name template
    | None -> ()

// Make a present place absent and release everything it owns, including its environment from the GPU
// unless another present place still uses that environment (spec 061 FR-010). The active place cannot be
// made absent — the author switches first (FR-011), which also covers a lone present place.
let deleteZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: Place) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "deleteZone: '%s' is not present" (Place.placeName name)) else
    if name = state.ActiveZone then invalidOp (sprintf "deleteZone: '%s' is the active place — switch away first" (Place.placeName name)) else

    let zone = state.Zones.[name]
    do Zone.release zone state.Physics shared.Gpu.Device
    state.Zones.Remove name |> ignore

    // The environment is the asset manager's, shared by id: unload it only when no still-present place uses it.
    match zone.Environment with
    | Some environment when not (state.Zones.Values |> Seq.exists (fun other -> match other.Environment with Some e -> e.AssetId = environment.AssetId | None -> false)) ->
        do Assets.releaseEnvironment shared.Assets environment.AssetId
    | _ -> ()
