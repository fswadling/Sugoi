# Data Model: Zone Cue Ops

## `world/Cue.fs`

Three new leaf cases beside `SetZone`; all carry the authored identity, a `Place`:

```fsharp
| SetZone of place: Place          // existing; now a no-op (not a raise) when the place is not present
| AddZone of place: Place          // NEW: make a declared place present, streaming its template
| RemoveZone of place: Place       // NEW: make a present (inactive) place absent
| PreloadZone of place: Place      // NEW: start its environment's async load
```

- All four are "no progress of their own" leaves: reached, performed and gone (`ValueSome Fin`), like
  `Exposit` and the advent ops. The module's opening note (its list of no-progress ops) gains them.
- No `…State` case is needed: none of them spans time.

## `state/GameplayState.fs`

```fsharp
// NEW: populate a present zone from a registered template (environment, light, placed props); detaches the
// built bodies when the zone is not the active one. `presentZone` uses it; so does a preload that finds a
// present place with no environment of its own.
val populateZone : shared:SharedState.SharedState -> state:byref<GameplayState> -> name:Place -> template:ZoneTemplate.ZoneTemplate -> unit

// NEW: make an absent place present without changing the active place.
val presentZone :
    shared:SharedState.SharedState ->
    state:byref<GameplayState> ->
    name:Place ->
    template:ZoneTemplate.ZoneTemplate option ->
    unit
```

- `presentZone` refuses (raises) if the place is already present; the cue op guards first, the primitive stays
  loud. It creates the zone, populates it from `template` when there is one, and detaches the built bodies.
- `populateZone` is the content half alone: load the template, then detach the bodies unless the zone is the
  active one. Used both by `presentZone` and by a preload that resolves a bare registered place.
- `addZone`/`switchZone`/`selectZone`/`deleteZone` are **unchanged** (they keep raising; the cue guards).

## `world/Zone.fs`

```fsharp
// NEW: populate a fresh zone from a registered template — its environment, light and placed props.
val loadTemplate :
    zone:Zone -> world:Physics.World -> assets:Assets.AssetManager -> template:ZoneTemplate.ZoneTemplate -> unit
```

- `loadEnvironmentFrom` + the template's `Light` + a spawn per `template.Props` entry via `spawnPropBinding`.
- Mirrors the content half of `editor/Spawning.instantiateZone`, without the replace/undo machinery: a zone
  this loads into is newly created and empty.

## `world/CueEngine.fs` — the four arms and their guards

The ops perform their effects in `advance`, guarded so a redundant or contradictory op performs nothing
(spec FR-007/FR-008). The guards live here, not in `GameplayState`, so the editor's loud behaviour is
untouched (research D2).

| Op | Guard (perform nothing when) | Effect |
|----|------------------------------|--------|
| `SetZone p` | `p` is not present | `GameplayState.switchZone p` |
| `AddZone p` | `p` is already present | `GameplayState.presentZone p (ZoneRegistry.tryTemplate p shared.ZoneRegistry)` |
| `RemoveZone p` | `p` is not present, or `p` is the active place | `GameplayState.deleteZone p` |
| `PreloadZone p` | `p` is not present | the zone's environment load is requested (`Assets.requestEnvironment`): its own, or — when it has none — the environment of its registered template, streamed into the zone first | `p` is absent, or has neither an environment nor a registered template |

- `ZoneRegistry` and `Assets` are reached fully qualified; no `open` is added.
- Each arm answers `ValueSome Fin`: an op with no progress of its own, costing no subsequent time.

## `editor/CueTree.fs`

- `kinds` and `kindName`: add `"Add zone"`, `"Remove zone"`, `"Preload zone"` (beside `"Set zone"`).
- `label`: `"Add zone: "` / `"Remove zone: "` / `"Preload zone: "` + `Place.placeName`.
- `defaultOfKind`: each new kind → `<Op> Place.start`.
- `withPlace`: extend from `SetZone` to all four zone ops.
- `drawEditor`: the existing place-chooser arm widens from `SetZone place` to
  `SetZone place | AddZone place | RemoveZone place | PreloadZone place`.
- `executing`: the three new cases join the "not executing" leaves. `textOf` needs no change (its `_` arm
  answers `ValueNone`), so none of the four is offered a text field.

## `editor/CueEditor.fs`

- No change: the place chooser is drawn by `CueTree.drawEditor`, and `drawNodeText` only special-cases the
  camera ops' waypoint combo. The zone ops are not text-bearing.

## Validation rules

- **Presence**: at most one zone per place; `presentZone` raises on a present place and the cue op guards it
  into a no-op (FR-002, FR-007).
- **No entry**: `AddZone` never changes `ActiveZone`; only `SetZone` does (FR-002, Q1 = A).
- **Removal**: a remove releases the zone and unloads its environment unless a still-present place shares it;
  the active place is never removed (FR-003, by the cue guard and `deleteZone`).
- **Residency**: a preload is an ordinary request — de-duplicated, non-blocking, evictable (FR-004, FR-006);
  a zone with no environment of its own resolves one from its registered template first; eviction never unloads
  the active zone's environment (FR-005).
- **No-op**: every zone op on a place it cannot act on performs nothing and lets the cue continue, so a cue is
  safe to re-run (FR-007/FR-008, Q3 = B).
- **Not a history step**: cue-driven zone changes are runtime effects on `GameplayState`; they add nothing to
  `Editor.Undo` (FR-010), as `SetZone` already does not.
