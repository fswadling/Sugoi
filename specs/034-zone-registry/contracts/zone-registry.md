# Contract: The Zone Registry

Public surface added or changed by this feature. Signatures are illustrative F#.

## Places and the registry — `world/ZoneRegistry.fs` (added)

```fsharp
// The game's places: a zone and one of the variants that zone declares, each zone's variants being
// its own union. Authored: adding a location is a case here, and adding what it can look like is a
// case in its own union.
type Period = Morning | Evening

type Place =
    | ArtRoom of Period
    | Island
```

// One row's label and the value the registry is keyed by.
type PlaceEntry =
    { Name : string
      Place : Place }

// One zone's node in the panel: its name, and one entry per place it declares.
type ZoneGroup =
    { Name : string
      Places : PlaceEntry list }

// Every zone, with its places, in declaration order. Computed once from the union rather than
// listed beside it, so the panel needs no change when the vocabulary grows (FR-002).
val byZone : ZoneGroup list

/// What is registered at each place. Sparse: an absent place is one nobody has registered yet.
type Registry =
    { Templates : Dictionary<Place, ZoneTemplate> }

val create : unit -> Registry

// An expected absence rather than a sentinel: `None` means no zone is registered there (FR-003).
val tryTemplate : place:Place -> registry:Registry -> ZoneTemplate option
```

- The registry offers no writer. Registering is a direct write to `Templates`, as assigning an actor
  asset is a direct write to `Casting.Cast.Bindings` (`specs/032-cast-binding`) — the panel holds the
  place and the environment, and one line records one against the other.
- Reflection raises on a case that is neither fieldless nor carries a single union type. That shape
  has no tree, and skipping it would hide a mistyped declaration rather than report it.
- `byZone` is a module-level `let`: the reflection runs once at type initialisation, not per frame.

## The record — `world/ZoneTemplate.fs` (added)

```fsharp
// One registered zone: the environment asset, the environment's own settings, and its parts as the
// author left them. A copy taken at a moment, not a link to the zone it was taken from.
type ZoneTemplate =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      Parts : PropState array }

// The copy: the record of a loaded environment, part for part (FR-004, FR-007).
val ofEnvironment : environment:Environment -> ZoneTemplate
```

- `PropState` is reused rather than shadowed: it is what `Zone.capturePart` answers and what
  `PartCreate`/`PartDelete` carry, so a registered part is not a fourth spelling of the same fields.
- `ofEnvironment` takes an `Environment`, not a `Zone`, so this module and `ZoneRegistry` depend on
  neither the live zone nor the editor.
- Compiled above `world/Zone.fs`, because the loader takes one as its argument.

## Loading — `world/Zone.fs` (changed)

```fsharp
// Unchanged surface, one shared implementation: `buildEnvironment` now takes the base, the
// orientation and the part states rather than reading all three from the asset, so an asset's own
// part list and a registered record's go through one activation path.
// (private) buildEnvironment : assetId:string -> baseEntity:Entity -> orientation:Quaternion ->
//     states:PropState array -> epoch:uint32 -> Environment
// (private) assetPartStates : EnvironmentAsset.EnvironmentAsset -> PropState array

// Unchanged: load an environment asset, replacing any previous one, and return the built value.
val loadEnvironment : zone:Zone -> world:Physics.World -> assets:Assets.AssetManager ->
    assetId:string -> Environment

// Added: the same, from a registered record rather than from an asset's own part list. Takes a fresh
// epoch, so refs from a previously loaded environment never resolve against this one (FR-012).
val loadEnvironmentFrom : zone:Zone -> world:Physics.World -> assets:Assets.AssetManager ->
    template:ZoneTemplate.ZoneTemplate -> Environment
```

- The parts map's capacity becomes `max 512 (uint32 states.Length)`: the map is also what
  `spawnPartBinding` adds to, so it carries headroom beyond the count it starts with, and the `max`
  keeps today's value for an asset while removing the failure an asset with more than 512 parts would
  hit at `Add`.
- A record whose submesh range runs past its asset's submeshes is not checked here: it is reported by
  `EnvironmentBatch.gather`'s existing range check, which is already loud (`specs/018-instanced-draws`).

## State — `state/SharedState.fs` (changed)

```fsharp
type SharedState =
    { mutable Gpu : Gpu.Gpu
      Assets : Assets.AssetManager
      Cast : Casting.Cast
      ZoneRegistry : ZoneRegistry.Registry   // added
      Physics : Physics.World }
```

- Project state that belongs to no zone, beside the cast binding and for the same reason: it must
  outlive every zone (FR-010), and the undo stacks are per zone.
- The field appears in four record constructions — `SharedState.create` and the fixtures in
  `test/GameplayStateTests.fs`, `test/SelectionTests.fs` and `test/SpawningTests.fs` — which is why
  `ZoneRegistry.create ()` exists rather than a dictionary literal at each site.

## Panel — `editor/ZoneRegistryPanel.fs` (added)

```fsharp
val draw : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> unit
```

Per place, inside the zone's pushed-ID scope:

```fsharp
for group in ZoneRegistry.byZone do
    if ImGui.collapsingHeader(group.Name, 0) then
        do ImGui.pushId group.Name

        for entry in group.Places do
            // One row's own scope: both buttons' labels repeat on every row, which is the collision
            // `specs/033-cast-spawn` shipped (research.md Decision 9).
            do ImGui.pushId entry.Name

            let registered = ZoneRegistry.tryTemplate entry.Place shared.ZoneRegistry
            let preview =
                match registered with
                | Some template -> shared.Assets.Environments.[template.AssetId].Name
                | None -> "(none)"

            do ImGui.text entry.Name
            do ImGui.sameLine(0.0f, 8.0f)
            do ImGui.text preview

            // Register: the active zone's loaded environment, filed against this place. Not a scene
            // edit, so no command records it (FR-009).
            do ImGui.sameLine(0.0f, 8.0f)
            do ImGui.beginDisabled (Option.isNone environment)
            if ImGui.smallButton "Register" then
                match environment with
                | Some loaded -> shared.ZoneRegistry.Templates.[entry.Place] <- ZoneTemplate.ofEnvironment loaded
                | None -> failwith "the register control is disabled while no environment is loaded"
            do ImGui.endDisabled()

            // Instantiate: put the registered zone into the active zone, as one undoable scene edit
            // (FR-013) — the same swap the Asset Browser performs for a bare asset.
            do ImGui.sameLine(0.0f, 8.0f)
            do ImGui.beginDisabled (Option.isNone registered)
            if ImGui.smallButton "Instantiate" then
                match registered with
                | Some template ->
                    let zone = GameplayState.activeZone state
                    let previous = zone.Environment
                    let next = Zone.loadEnvironmentFrom zone shared.Physics shared.Assets template
                    do Selection.dropStalePartSelection &state
                    let undo = UndoStack.push state.Editor.Undo (SetEnvironment (previous, Some next))
                    state.Editor <- { state.Editor with Undo = undo }
                | None -> failwith "the instantiate control is disabled while the place is unregistered"
            do ImGui.endDisabled()

            do ImGui.popId()

        do ImGui.popId()
```

- `environment` is `(GameplayState.activeZone state).Environment`, read once for the whole panel, so
  the Register control's enabled state and its action cannot disagree about whether there is one.
- The disabled branches are unreachable — a disabled widget returns false — so they raise rather than
  no-op, which is how a broken precondition reports itself instead of looking like a click that did
  nothing (constitution II).
- The five-line instantiate block is the Asset Browser's, repeated: the compile order forbids sharing
  it today (`research.md` Decision 7).
- No new ImGui binding: `text`, `sameLine`, `smallButton`, `beginDisabled`/`endDisabled`,
  `collapsingHeader` and `pushId`/`popId` are all already declared in `render/ImGui.fs`.

## Orchestration — `editor/Editor.fs` (changed)

```fsharp
let private panelNames = [| "Outliner"; "Inspector"; "Asset Browser"; "Cast"; "Zone Registry" |]

// in `draw`, after `CastPanel.draw`:
do ZoneRegistryPanel.draw shared &state
```

- The panel joins `panelNames` so Ctrl+Tab reaches it, in the order the dock layout shows the tabs.

## Native — `native/cimgui_sdl3.cpp` (changed)

```cpp
ImGui::DockBuilderDockWindow("Cast", left);
ImGui::DockBuilderDockWindow("Zone Registry", left);   // added
```

- The left node, per `docs/EDITOR_DESIGN.md`'s Workspace table. `native/libcimgui.so` is a gitignored
  build artifact and must be rebuilt with `native/build.sh` for the panel to dock.

## Not changed

- `world/Entity.fs` — no field on `Environment`, `PropState` or `Prop`; the record is built out of
  them rather than added to them.
- `state/Command.fs` — no new command. Instantiating reuses `SetEnvironment`, which already carries
  whole environments and already reverses them.
- `world/Casting.fs`, `cast/Roster.fs` — untouched. `Place` is a second, unrelated vocabulary.
- `editor/AssetBrowser.fs` — behaviour identical; its own swap stays where it is.
- `editor/Spawning.fs` — untouched: this feature creates no entity, and the environment swap is not
  its to own while it compiles above `editor/Selection.fs` (`research.md` Decision 7).
