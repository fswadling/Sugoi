# Contract: Zone Container

Public surface added or changed by this feature. Signatures are illustrative F#.
Zones are held as a plain name-keyed dictionary on the gameplay state, with one
active name — there is no separate container type. Player designation and undo
history move to zone scope.

## Gameplay state — `state/GameplayState.fs`

```fsharp
[<Struct>]
type GameplayState =
    { mutable Camera : Camera.Camera
      mutable DebugDraw : DebugDraw.DebugDraw
      mutable GizmoDraw : DebugDraw.DebugDraw
      Zones : Dictionary<string, Zone>          // keyed by the zone's unique name
      mutable ActiveZone : string               // exactly one active zone name
      mutable Editor : EditorState.EditorState
      mutable Editing : bool
      mutable Follow : Systems.CameraFollow
      UndoByZone : Dictionary<string, Command.UndoStack> }

// Seeded with one empty zone, named "main", made active.
val create : debugDraw:DebugDraw.DebugDraw -> gizmoDraw:DebugDraw.DebugDraw -> GameplayState

// The active zone (one dictionary lookup).
val activeZone : state:GameplayState -> Zone

// Switch the active zone: save the outgoing undo stack, swap physics, clear the
// zone-scoped selection/drag/nudge, load the incoming undo stack, re-frame camera.
// An unknown name raises.
val switchZone : shared:SharedState.SharedState -> state:byref<GameplayState> -> name:string -> unit

// Add a new empty zone under `name` and make it active. An empty or duplicate name
// raises.
val addZone : shared:SharedState.SharedState -> state:byref<GameplayState> -> name:string -> unit

// Delete a zone: reject the last zone, make another zone active if `name` is the
// active one, then release the zone and drop its map and undo entries.
val deleteZone : shared:SharedState.SharedState -> state:byref<GameplayState> -> name:string -> unit
```

- The zone's name is the map key and its identity; it is not stored on the `Zone`.
- A name is unique and non-empty: `addZone` rejects an empty or duplicate name
  (`invalidOp`), and the editor does not offer the add in that case.
- `switchZone` on an unknown name raises; on the active name it is a no-op.
- `deleteZone` on the last zone or an unknown name raises; deleting the active zone
  first makes another zone active. Deletion discards that zone's undo history and is
  not undoable.

## Zone lifecycle — `world/Zone.fs`

```fsharp
// Added field: mutable Player : SlotMap.Handle voption   // the zone's designation
// (No name field: the zone's name is its key in GameplayState.Zones.)

// Detach every body the zone owns from the shared simulation (RemoveBody), so it
// neither collides nor is stepped. Bodies, shapes, renderers, and Physics
// attachments are left intact.
val deactivatePhysics : zone:Zone -> world:Physics.World -> unit

// Re-attach every body (AddBody) and rebuild the broadphase.
val activatePhysics : zone:Zone -> world:Physics.World -> unit

// Delete-time release: destroy the zone's bodies, release its renderers and
// EnvironmentBatch, and clear BodyToEntity. Only called when the zone is inactive.
val release : zone:Zone -> world:Physics.World -> device:Gpu.Device -> unit
```

- Nothing is destroyed or rebuilt on a switch; the bodies keep their `Physics`
  attachments and mesh-shape BVHs, and renderers are never touched.
- Activation finishes with `PhysicsSystem.OptimizeBroadPhase` (adding many bodies
  one at a time degrades the broadphase). `release` is only for deletion.

## ImGui binding — `render/ImGui.fs`

```fsharp
// cimgui's igInputText: `buf` is a mutable NUL-terminated C string buffer.
[<DllImport("cimgui", EntryPoint = "igInputText", CallingConvention = CallingConvention.Cdecl)>]
extern bool inputText(string label, byte[] buf, unativeint bufSize, int flags, nativeint callback, nativeint userData)
```

- The symbol is already exported by the vendored `libcimgui.so`; no native rebuild.

## Editor state — `state/EditorState.fs`

```fsharp
// Added field: NewZoneName : byte[]   // one reusable UTF-8 buffer for the pending zone name
```

## Player systems — `world/Systems.fs`

```fsharp
// Read from zone.Player instead of taking a player argument.
val isPlayerLive : zone:Zone -> bool

val playerInput  : world:Physics.World -> zone:Zone -> input:Input -> dt:float32 -> unit
val cameraFollow : zone:Zone -> follow:byref<CameraFollow> -> camera:byref<Camera.Camera> -> unit
val animate      : zone:Zone -> input:Input -> unit

// Unchanged: an editor selection as a player handle; raises for a non-actor.
val playerHandleOf : ref:EntityRef -> SlotMap.Handle
```

Each system resolves the designation with `isPlayerLive zone`; a zone with no live
designation is inert. `syncActors` is unchanged and not player-gated.

## Editor UI — `editor/Outliner.fs`

```fsharp
// Draws a "Zones" collapsing header at the top of the Outliner, above the scene
// objects: one select row per zone (active marked, with a Del button), a name text
// field, and an Add Zone button. Select -> switchZone; Del -> deleteZone; Add ->
// addZone. Add is disabled while the name is empty or already used, and Del is
// disabled for the last remaining zone, so the container's guards are unreachable
// through the UI. Shown only while editing (the editor workspace already gates on
// `Editing`).
val draw : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> unit
```

`Inspector.draw` *Set as Player* writes `GameplayState.activeZone state.Player`
instead of `state.Player`. `AssetBrowser`, `Selection`, and `Spawning` resolve the
active zone rather than reading `state.Zone`.

## Gameplay screen — `screen/GameplayScreen.fs`

```fsharp
// Resolves the active zone once per frame and uses it (and its batch) for the
// update chain and the scene render.
val update : shared:SharedState.SharedState -> frame:Screen.Frame -> state:byref<GameplayState.GameplayState> -> Screen.ScreenTransition
val renderScene : shared:SharedState.SharedState -> state:inref<GameplayState.GameplayState> -> pass:Screen.ScenePass -> unit
```

## Engine — `Engine.fs`

- The per-second stats line reads `GameplayState.activeZone`.
- Shutdown releases **every** zone's `EnvironmentBatch` by iterating
  `GameplayState.Zones.Values`, not just the active zone's.

## Rules

- Exactly one zone is active; only its bodies are attached to the shared physics
  world. Inactive zones' bodies are detached but still allocated until the zone is
  deleted.
- A zone name is unique and non-empty; adding under an existing or empty name
  fails loudly, and the last zone cannot be deleted. Zone names are never generated.
- A player designation is a `SlotMap.Handle` into its own zone's `Actors`; a
  deleted/recycled actor makes it inert with no clear step. Only the active zone's
  designation is live.
- Adding and switching zones are view/container actions: they are not recorded as
  undoable scene edits. Undo/redo applies to the zone the edit was recorded in.
- No new per-frame allocation: the active zone is resolved once per frame and the
  name buffer is reused.
