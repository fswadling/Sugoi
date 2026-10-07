# Data Model: State Ownership Boundary

## The rule

A member lives where its **readers** are:

- **`SharedState`** — state the engine itself, or more than one screen, may read.
- **`GameplayState`** — state whose only readers are the gameplay screen and its editor layer.

## `state/SharedState.fs` — after

A reference type (screens write shared values back rather than threading struct copies).

```fsharp
type SharedState =
    { mutable Gpu : Gpu.Gpu              // the engine's render loop reads it (Engine.fs)
      Assets : Assets.AssetManager       // any presenting screen may read it (e.g. a journal's portraits)
      Cast : Casting.Cast                // the slot -> actor-asset binding; a non-gameplay screen may read it
      mutable Advents : Set<Advent> }    // story-progress flags a non-gameplay screen may read
```

- Removed: `ZoneRegistry : ZoneRegistry.Registry`, `Physics : Physics.World`.
- The header states the readership rule and no longer cites per-zone undo stacks or `spec 034`.
- `create` no longer constructs the registry or the world.

## `state/GameplayState.fs` — after

Still a `[<Struct>]`. Two fields added, beside the `Zones` they serve.

```fsharp
[<Struct>]
type GameplayState =
    { ...
      Zones : Dictionary<Place, Zone>            // existing: the runtime zones
      ZoneRegistry : ZoneRegistry.Registry       // ADDED: the recipe table for those zones
      Physics : Physics.World                    // ADDED: the world their bodies attach to
      ... }

val create : DebugDraw -> DebugDraw -> Ui -> GameplayState
// builds the registry (`ZoneRegistry.create ()`) and the world (`Physics.create ()`)
```

- Neither added field is `mutable`: writers mutate the inner `Dictionary` (the registry) or call
  through the `World`'s methods (physics) — the references never change.
- `swapZone`, `populateZone` and `deleteZone` read the world from the state (`state.Physics`) rather
  than from `shared`; they keep their `shared` parameter for `Assets` (and `Gpu.Device` in release).
- `activeZone`, the cast-index helpers (`tryCastActor`/`addCast`/`removeCast`/`removeCastZone`) and
  `enterZone` are unchanged.

## Call sites (reads move from `shared` to `state`)

| Field | Readers |
|---|---|
| `Physics` | `screen/GameplayScreen.fs`, `state/History.fs`, `world/CueEngine.fs`, `editor/Editor.fs`, `editor/Selection.fs`, `editor/Spawning.fs`, `editor/AssetBrowser.fs`, `state/GameplayState.fs` (switch/populate/delete), `Engine.fs` (shutdown) |
| `ZoneRegistry` | `editor/ZoneRegistryPanel.fs`, `world/CueEngine.fs` (`AddZone`, `PreloadZone`) |

Every one of these already holds the gameplay state; only the qualifying expression changes.

## Validation rules

- Exactly **one** physics world exists, and it is the gameplay state's (FR-008). A zone never holds
  it; `Zone.*` continues to receive it as a parameter. Switching the active zone detaches the
  outgoing zone's bodies and attaches the incoming zone's, as before.
- Exactly **one** zone registry exists, and it is the gameplay state's (FR-002). A present place
  need not have a template, and a registered place need not be present (unchanged).
- No member remains on `SharedState` whose only readers are the gameplay screen/editor (FR-001).
- `SharedState.create` and `GameplayState.create` are both called once, in `ScreenManager.init`;
  neither field's lifetime changes (SC-001/SC-004).
