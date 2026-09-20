# Contract: A Registered Zone's Placed Props

Public surface added or changed by this feature. Signatures are illustrative F#.

## The entry — `world/ZoneTemplate.fs` (changed)

```fsharp
type ZoneTemplate =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      Parts : PropState array   // added to, in 034
      Props : PropState array } // added

// Unchanged in kind: still pure and zone-free, because a record can be built in a test without a
// physics world. It takes the zone's props as a second argument rather than reading them, because at
// this point in the compile list there is no zone to read.
val ofEnvironment : environment:Environment -> props:PropState array -> ZoneTemplate
```

- `Props` is the zone's placed props — assets of their own — where `Parts` remains the environment's
  own geometry. The two are separate fields because they are separate things: a part has no existence
  without its environment, a prop has none without its zone.
- An entry with no props is a complete entry, not a partial one; that is also the state every entry
  registered before this feature is in, and nothing needs to migrate.

## The capture — `world/Zone.fs` (changed)

```fsharp
// The zone's placed props, each with the ref it currently stands at — the ref a replacement has to
// record if undo is to put the prop back where it was, at the generation it had (FR-005).
val captureProps : zone:Zone -> (EntityRef * PropState) array

// The whole entry: the loaded environment paired with those props. `None` when the zone has no
// environment loaded, which is FR-007 expressed in the return type and the same condition that
// disables the panel's Register control.
val captureTemplate : zone:Zone -> ZoneTemplate option

// Unchanged: loads the environment half of a record. `Props` is not an environment's business and is
// placed by the caller as part of the same step.
val loadEnvironmentFrom : zone:Zone -> world:Physics.World -> assets:Assets.AssetManager ->
    template:ZoneTemplate.ZoneTemplate -> Environment
```

- `captureProps` reads the live props slot map, so a prop the author deleted is simply absent from the
  array and one she duplicated appears twice (FR-002).
- Capture is a copy: mutating the zone afterwards does not reach the record (inherited from 034's
  FR-009), and nothing about capture touches the scene or an undo stack (FR-008).

## The compound command — `state/Command.fs` (changed)

```fsharp
type Command =
    | …
    // Several mutations that were one gesture. Applied forwards, reversed backwards, so a create and
    // the delete that preceded it cannot come apart.
    | Batch of Commands : Command list
```

```fsharp
| Batch commands ->
    for cmd in commands do apply zone physics assets cmd

| Batch commands ->
    for cmd in List.rev commands do undo zone physics assets cmd
```

- Nothing else about the command model moves: one command is still one entry on the stack, `Applied`
  is still a `Command list`, and `UndoStack.push`'s existing transform-coalescing is unaffected. A
  `Batch` never merges.
- The commands inside a `Batch` are ones that already exist and carry their own payloads, so a batch
  cannot express anything a sequence of commands could not — it only says they were one action.

## The replacement — `editor/Spawning.fs` (changed)

```fsharp
// Instantiate a registered zone into the active zone: the environment it records, its parts, and its
// props in place of the zone's own. One scene edit, one undo step, whatever the entry holds.
val instantiateZone : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> ->
    template:ZoneTemplate.ZoneTemplate -> unit
```

What it does, in order — the mutation first, the reversal recorded immediately, which is what the
module exists for:

```fsharp
let previous = zone.Environment
let replaced = Zone.captureProps zone
for (ref, prop) in replaced do
    // Through the command that will be replayed, not a private removal path.
    let cmd = PropDelete (ref, prop)
    do apply zone shared.Physics shared.Assets cmd
let next = Zone.loadEnvironmentFrom zone shared.Physics shared.Assets template
let placed =
    template.Props
    |> Array.map (fun prop ->
        match prop.AssetId with
        | Some assetId ->
            let ref = Zone.spawnPropBinding zone shared.Physics shared.Assets prop.Base (bindingOf prop)
            (ref, prop)
        | None -> failwith "an entry's props must name an asset: this one is shaped like a part")
let step =
    Batch ([ for (ref, prop) in replaced -> PropDelete (ref, prop) ]
           @ [ SetEnvironment (previous, Some next) ]
           @ [ for (ref, prop) in placed -> PropCreate (ref, prop) ])
```

- It selects nothing, unlike every other function here: an instantiation may place none, one or twenty
  props, so there is nothing to select that would not be arbitrary. A selection naming a replaced prop
  goes stale and resolves again if the instantiation is undone (research Decision 7).
- It does **not** reconcile a stale part selection. The environment swap invalidates one, and the
  caller does it — the same step `Editor.undoStep` and `redoStep` already take.
- `Zone.spawnPropBinding` is the creation path, so the pairing rule holds: every `Add` is recorded by
  the batch pushed immediately after it.

## The panel — `editor/ZoneRegistryPanel.fs` (changed)

```fsharp
// Register: the whole entry, captured only when the control is activated — capturing walks the zone's
// props and allocates, so it must not happen per frame.
do ImGui.beginDisabled (Option.isNone environment)
if ImGui.smallButton "Register" then
    match Zone.captureTemplate (GameplayState.activeZone state) with
    | Some template -> shared.ZoneRegistry.Templates.[entry.Place] <- template
    | None -> failwith "the register control is disabled while no environment is loaded"
do ImGui.endDisabled()

// Instantiate: the scene edit, then the reconcile the swap's new home no longer does.
do ImGui.beginDisabled (Option.isNone registered)
if ImGui.smallButton "Instantiate" then
    match registered with
    | Some template ->
        do Spawning.instantiateZone shared &state template
        // The swap invalidates a Part selection; the caller reconciles it, as undo/redo do.
        do Selection.dropStalePartSelection &state
    | None -> failwith "the instantiate control is disabled while the place has no entry"
do ImGui.endDisabled()
```

- The five lines of environment swap that were here in 034 are gone: they moved to `Spawning`, where
  they stopped needing to be in a panel at all (research Decision 5).
- No new widget and no new ImGui binding, so nothing native changes.

## Superseded in `specs/034-zone-registry`

- `contracts/zone-registry.md`'s `Instantiate` block showed the swap inline in the panel, with a
  comment saying the compile order forbade sharing it. The block is marked superseded: the swap moved
  to `Spawning` and the panel reconciles after it.
- `research.md` Decision 7 recorded that the second call site would extract at the third. It is
  corrected to say what happened instead — the second site *moved*, the two remaining sites build
  different environments, and the trigger for extracting the three-line tail is a third
  environment-loading path.

## Not changed

- `world/Entity.fs` — no field on `Environment`, `Prop` or `PropState`. The props are recorded as the
  authored value they already are.
- `editor/AssetBrowser.fs` — its environment load builds from an asset id and is untouched, in
  behaviour and in shape.
- `Zone.setEnvironment`, `Zone.spawnPropBinding`, `Zone.removeProp`, `Zone.captureProp` — the
  replacement composes these; none of them changes.
- `state/Command.fs`'s other commands, the undo stack's shape, and the zone/registry model.
- `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` — no file is added or moved.
