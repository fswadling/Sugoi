# Contract: Cast Spawn

Public surface added or changed by this feature. Signatures are illustrative F#. The change is that
a bound slot can place its actor, through the one place the editor creates entities.

## CastPanel — `editor/CastPanel.fs`

```fsharp
// Changed: the gameplay state joins the arguments, because placing an actor is a scene edit and
// `Spawning` needs it to push the reversing command and to select what it created.
val draw : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> unit
```

The row, per look, inside the member's pushed-ID scope:

```fsharp
let slot : Casting.Slot = { Character = character; Look = look }
let bound = Casting.tryAsset slot shared.Cast
let preview =
    match bound with
    | Some id -> shared.Assets.Actors.[id].Name
    | None -> "(none)"

if ImGui.beginCombo(Casting.lookName look, preview, 0) then
    // ... `(none)` and the discovered assets, as 032 drew them ...
    do ImGui.endCombo()

// The spawn control: one per slot, inert while the slot is unassigned.
do ImGui.sameLine(0.0f, 8.0f)
do ImGui.beginDisabled (Option.isNone bound)
if ImGui.smallButton "Spawn" then
    match bound with
    | Some assetId -> do Spawning.spawnActor shared &state (Spawning.baseEntity (Roster.characterName character)) assetId
    | None -> ()
do ImGui.endDisabled()
```

- The button needs no unique label: the member's name is already on ImGui's ID stack, which is what
  keeps one member's `Spawn` distinct from another's.
- `bound` is read once per row per frame, so the preview and the control cannot disagree about
  whether the slot is assigned.

## Spawning — `editor/Spawning.fs`

```fsharp
// Added, and public: the entity a spawned asset becomes. Moved here from AssetBrowser's private
// copy, because this is the module that owns entity creation and this is its third call site.
val baseEntity : name:string -> Entity
```

- The module's own comment already claims the role: what it exists for is that every `Zone.spawn*` is
  paired with the command that reverses it, and that no panel calls `Zone.spawn*` directly.
- `spawnActor`/`spawnProp` are unchanged. Spawning an actor needs no new function: the panel supplies
  the entity and the asset id, which is what `spawnActor` already takes.

## AssetBrowser — `editor/AssetBrowser.fs`

- Its private `baseEntity` is deleted; its two call sites (`spawnActor`, `spawnProp`) call
  `Spawning.baseEntity`. Nothing else in the panel changes, and its behaviour is identical.

## Editor — `editor/Editor.fs`

```fsharp
do CastPanel.draw shared &state
```

## Corrections to `specs/032-cast-binding`

That feature's contract already specified this signature —
`val draw : shared -> state:byref<GameplayState> -> unit` — and its implementation took `shared`
alone, as a minimality call made while building the panel, so contract and code disagreed from 032
onward. What needs correcting here is therefore not the signature, which the code now matches, but
the one bullet that says what the panel does:

- `contracts/cast-binding.md`: "Draws nothing else, mutates nothing outside `shared.Cast`, and
  touches no undo stack" — true of a panel that only assigns, false of one that also places actors.
  It is corrected to name the supersession and to keep the assignment reasoning, which is what the
  rest of that section is about.

`specs/032-cast-binding`'s data model contains no claim this feature falsifies: "The panel's data,
and what it does not hold" is about selection state and roster lists, and both are still true.

## Not changed

- `world/Casting.fs` — the binding, the slot, the look vocabulary, and `tryAsset`. Spawning reads the
  binding and never writes it.
- `state/Command.fs` and `world/Zone.fs` — the create is the existing `ActorCreate` through the
  existing `Zone.spawnActor`. No command and no zone operation is added.
- `state/SharedState.fs` and `world/Entity.fs` — no field is added to either; in particular the actor
  does not learn which cast member it came from (FR-009).
- `render/ImGui.fs` — every widget the control needs is already bound, so this feature adds no
  `DllImport` and needs no native rebuild.
