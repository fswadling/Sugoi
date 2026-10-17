module GameplayState

// Per-screen gameplay state. A struct; owns the gameplay world and that world's configuration —
// the zones (a name-keyed map with exactly one active zone) and the physics world their bodies
// attach to — beside the
// screen's own runtime state (camera + render buffers, dialogue, the cue run, editor/play state).
// The test for what belongs here is readership: a member is the gameplay screen's (and its editor
// layer's) when nothing else reads it. Device-level render resources (pipelines, formats, depth
// target) live on `SharedState`, not here. A zone's name is its identity and its key; `activeZone`
// is one dictionary lookup, done once per frame by the hot paths.

open System.Collections.Generic
open System.Numerics
open Command
open Entity
open ZoneTemplate

[<Struct>]
type GameplayState =
    { mutable Camera : Camera.Camera
      mutable DebugDraw : DebugDraw.DebugDraw
      mutable GizmoDraw : DebugDraw.DebugDraw
      // The game's 2D layer (spec 038), held as the two debug drawers are: a per-screen
      // renderer the screen draws with. It owns the pipeline, the batch and the font.
      mutable Ui : Ui.Ui
      // The shared skydome resource (spec 092), held like the UI: a device-lifetime sphere the screen
      // draws the active zone's sky with. The authored value itself lives on the zone.
      mutable Skydome : Skydome.Skydome
      // The dialogue state (spec 038): the screen's, not a zone's, so a zone switch does
      // not clear it. Runtime only — never authored, never persisted, never on an entity.
      mutable Dialogue : Dialogue.Dialogue
      // The gameplay state's zones, keyed by their name (spec 079). A zone is either present (it has an
      // entry here) or absent; the active zone is always present.
      Zones : Dictionary<string, Zone.Zone>
      // The scenario library (spec 070; a string-keyed ordered library since spec 083): the contents captured
      // under each scenario, keyed by its name. Only the gameplay screen and its editor read it. Mutable because
      // a change produces a new library value.
      mutable Scenarios : ScenarioRegistry.Registry
      // The physics world: one Jolt world shared by every zone (a zone keeps only its body→entity
      // map), owned here beside the zones because only the gameplay screen simulates. A zone never
      // holds it — `Zone.*` receives it as a parameter, so there is one world for the whole game.
      Physics : Physics.World
      // The runtime cast (spec 072): resolving which actor plays a character is a name scan over the present
      // places, not an index (`tryActorByName` below). There is no map to keep exact.
      mutable ActiveZone : string
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

/// The name of the zone the game starts in (spec 079; replaces `Place.start`). The runtime world starts
/// with it present and empty.
let startZone = "start"

let create (debugDraw: DebugDraw.DebugDraw) (gizmoDraw: DebugDraw.DebugDraw) (ui: Ui.Ui) (skydome: Skydome.Skydome) : GameplayState =
    let zones = Dictionary<string, Zone.Zone>()
    zones.[startZone] <- Zone.create ()
    { Camera = Camera.create (Vector3(0.0f, 3.0f, 6.0f)) Vector3.Zero
      DebugDraw = debugDraw
      GizmoDraw = gizmoDraw
      Ui = ui
      Skydome = skydome
      Dialogue = Dialogue.empty
      Zones = zones
      Scenarios = ScenarioRegistry.create ()
      Physics = Physics.create ()
      ActiveZone = startZone
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
    | false, _ -> invalidOp (sprintf "activeZone: '%s' is not present" state.ActiveZone)

// ── The runtime cast (spec 072, spec 075) ──

// Resolution is per zone now: a cue op names its place and resolves the actor there with `Zone.tryActorByName`
// (`state/CueEngine.fs`). There is no scan of the present places, so no resolver lives here.

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
let private swapZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (outgoing: string) (next: string) : unit =
    // Request the incoming environment. This never blocks: the switch completes in-frame and the
    // environment pops in when it lands (FR-004). The pin set is a gameplay-layer concern, refreshed
    // to the active environment every frame (`GameplayScreen.update`), so it is not touched here.
    match state.Zones.[next].Environment with
    | Some environment -> do Assets.requestEnvironment shared.Assets environment.AssetId
    | None -> ()
    do Zone.deactivatePhysics state.Zones.[outgoing] state.Physics
    state.ActiveZone <- next
    do Zone.activatePhysics state.Zones.[next] state.Physics
    do Zone.ensurePlayerDynamic state.Zones.[next] state.Physics shared.Assets
    enterZone &state

// Make a present place active. A no-op when it already is; an absent place is a bug.
let switchZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "switchZone: '%s' is not present" (name)) else
    if name = state.ActiveZone then () else
    swapZone shared &state state.ActiveZone name

// Bring a zone up in the editor: make it active, and make it the Inspector's subject by dropping any
// entity selection (spec 043 — the Inspector shows the active zone's light when no entity is
// selected). A switch drops the selection on the way (`enterZone`), so the case this adds is the
// zone already active, where clicking its row is otherwise a no-op.
let selectZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "selectZone: '%s' is not present" (name)) else
    if name = state.ActiveZone then
        state.Editor <- { state.Editor with Selection = None; Drag = None }
    else
        switchZone shared &state name

// Make an absent declared place present and active. A place already present is a bug — the editor offers
// only the absent ones (spec 061 FR-003).
let addZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) : unit =
    if state.Zones.ContainsKey name then invalidOp (sprintf "addZone: '%s' is already present" (name)) else
    let outgoing = state.ActiveZone
    state.Zones.[name] <- Zone.create ()
    swapZone shared &state outgoing name

// Apply a zone's staging (spec 079): free its placed props, set its environment (loading the recipe's when it has
// one, clearing it when not), its light and its placed props. `Physics.createBody` joins the simulation as it
// runs, so a zone that is not the active one has what it built detached at once.
let private applyStaging (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) (staging: ZoneStaging) : unit =
    let zone = state.Zones.[name]
    let physics = state.Physics

    for (ref, prop) in Zone.captureProps zone do
        do Command.apply zone physics shared.Assets (Command.PropDelete (name, ref, prop))

    match staging.Environment with
    | Some template -> do ignore (Zone.loadEnvironmentFrom zone physics shared.Assets template)
    | None -> do Zone.setEnvironment zone physics shared.Assets None

    zone.Light <- staging.Light
    zone.Skydome <- staging.Skydome

    for prop in staging.Props do
        let assetId =
            match prop.AssetId with
            | Some assetId -> assetId
            | None -> failwith "applyStaging: a recorded prop must name an asset"
        let binding = { AssetId = Some assetId; SubmeshIndex = prop.SubmeshIndex; SubmeshCount = prop.SubmeshCount }
        do ignore (Zone.spawnPropBinding zone physics shared.Assets prop.Base binding)

    if name <> state.ActiveZone then Zone.deactivatePhysics zone physics

// Make an absent zone present, staged from `staging`, and leave the active zone, the camera and the selection
// untouched (spec 062, 079): a cue *prepares* a zone here, and `switchZone` is what enters it. A zone already
// present is a bug — the cue, or a fork, guards it — so this raises.
let presentZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) (staging: ZoneStaging) : unit =
    if state.Zones.ContainsKey name then invalidOp (sprintf "presentZone: '%s' is already present" name) else
    state.Zones.[name] <- Zone.create ()
    do applyStaging shared &state name staging

// Make a present place absent and release everything it owns, including its environment from the GPU
// unless another present place still uses that environment (spec 061 FR-010). The active place cannot be
// made absent — the author switches first (FR-011), which also covers a lone present place.
let deleteZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "deleteZone: '%s' is not present" (name)) else
    if name = state.ActiveZone then invalidOp (sprintf "deleteZone: '%s' is the active place — switch away first" (name)) else

    let zone = state.Zones.[name]
    do Zone.release zone state.Physics shared.Gpu.Device
    state.Zones.Remove name |> ignore

    // The environment is the asset manager's, shared by id: unload it only when no still-present place uses it.
    match zone.Environment with
    | Some environment when not (state.Zones.Values |> Seq.exists (fun other -> match other.Environment with Some e -> e.AssetId = environment.AssetId | None -> false)) ->
        do Assets.releaseEnvironment shared.Assets environment.AssetId
    | _ -> ()

// Replace a present zone's staging (spec 079). The one restager — a fork into an existing zone and the
// `ZoneStage` command both come through here. `Zone.Player` is not staging and is left alone.
let stageZone (shared: SharedState.SharedState) (state: byref<GameplayState>) (name: string) (staging: ZoneStaging) : unit =
    if not (state.Zones.ContainsKey name) then invalidOp (sprintf "stageZone: '%s' is not present" name) else
    do applyStaging shared &state name staging

// Replace the whole present world with `zones` (each name paired with its authored contents), making `active` the
// active place (spec 084). Every present zone is retired — its bodies destroyed, its renderers and batch freed, and
// its environment unloaded once no zone still references it — and the given set is brought up through the one
// `Zone.occupy*` path. The retired zones are then simply dropped: they are managed objects, so the GC reclaims them;
// only the native bodies and GPU handles need this explicit teardown. State-level, because only this layer knows the
// physics world, the GPU device and the active place. `Zone.release` is safe on the active zone (it removes a body
// from the simulation before destroying it), unlike `deleteZone`, which requires an inactive one.
let replaceZones (shared: SharedState.SharedState) (state: byref<GameplayState>) (zones: (string * Zone.ZoneContents) list) (active: string) : unit =
    // ── retire the present world ──
    let environments = HashSet<string>()
    for KeyValue (_, zone) in state.Zones do
        match zone.Environment with
        | Some environment -> environments.Add environment.AssetId |> ignore
        | None -> ()
        do Zone.release zone state.Physics shared.Gpu.Device
    // Nothing references them any more, so unload each distinct environment once.
    for assetId in environments do do Assets.releaseEnvironment shared.Assets assetId
    state.Zones.Clear()

    // ── bring the new world up ──
    for (name, contents) in zones do
        state.Zones.[name] <- Zone.create ()
        do Zone.occupyZone state.Zones.[name] state.Physics shared.Assets contents
        if name <> active then do Zone.deactivatePhysics state.Zones.[name] state.Physics

    if not (state.Zones.ContainsKey active) then invalidOp (sprintf "replaceZones: '%s' is not among the zones it installs" active)
    state.ActiveZone <- active
    state.InteractableTrigger <- ValueNone
    state.Editor <- { state.Editor with Selection = None; Drag = None; NudgeBefore = None }
