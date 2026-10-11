# Data Model: Retire the Cast

## `world/Dialogue.fs` — the speaker is a string

```fsharp
// before (spec 038)
type Kind =
    | PortraitDialogue of Roster.Character * Emotion
    | Dialogue
    | Thought

// after
type Kind =
    | PortraitDialogue of speaker: string * Emotion   // the actor's `Name`
    | Dialogue
    | Thought
```

`world/Dialogue.fs` no longer names `Roster`; `Emotion`, `emotionName`, `emotions` and `Dialogue` are unchanged.
The module's header comment (which said it is compiled after `cast/Roster.fs`) is updated.

## `narrative/Roster.fs` — the union, relocated

Moved verbatim from `cast/Roster.fs` (module name `Roster`, `Character`, `characterName`, `characters`), with the
header comment rewritten to say it is the narrative engine's vocabulary. No type or function change; the narrative
files and `test/RosterTests.fs` compile unchanged.

## `world/Assets.fs` — the actor name index

```fsharp
type AssetManager =
    { Device : Gpu.Device
      Root : string
      Actors : Dictionary<AssetId, ActorAsset.ActorAsset>
      // NEW: the actor asset each declared name resolves to, built once at scan. A speaker string
      // (`Dialogue.Kind.PortraitDialogue`) resolves through this on the per-frame draw path, so no
      // enumeration runs per frame. The lowest `Id` wins a shared name (the by-name label rule).
      ActorsByName : Dictionary<string, ActorAsset.ActorAsset>
      Props : Dictionary<AssetId, EnvironmentAsset.EnvironmentAsset>
      // ... unchanged
```

Built by a private `indexByName (actors: Dictionary<AssetId, ActorAsset.ActorAsset>)` that iterates the actors
sorted by `Id` and keeps the first entry per `Name`; called from `loadActors` (production) and from
`TestSupport.manager` (tests). New read helper:

```fsharp
/// The discovered actor names, sorted and de-duplicated — what a speaker chooser offers.
let actorNames (manager: AssetManager) : string list
```

## `world/DialogueRender.fs` — resolution and the miss set

```fsharp
// before (spec 038)
let tryPortrait (assets) (cast: Casting.Cast) (character: Roster.Character) (emotion: Emotion) : PortraitResolution
let missReason (assets) (cast) (ui) (viewportW) (viewportH) (dialogue) : MissReason
let draw (assets) (cast) (viewportW) (viewportH) (ui) (dialogue) : unit

// after
let tryPortrait (assets: Assets.AssetManager) (speaker: string) (emotion: Dialogue.Emotion) : PortraitResolution
let missReason (assets) (ui) (viewportW) (viewportH) (dialogue) : MissReason
let draw (assets) (viewportW) (viewportH) (ui) (dialogue) : unit
```

Resolution table:

| Condition | Result |
|-----------|--------|
| `assets.ActorsByName` contains `speaker` | `Resolved (portraitOf emotion asset.Portraits)` when it declares portraits, else `Missed NoPortraits` |
| otherwise | `Missed NoAsset` |

`MissReason`:

```fsharp
type MissReason =
    | NotMissed
    | NoAsset            // was Unbound / AssetNotLoaded — no discovered asset of that name
    | NoPortraits
    | TooTallForViewport
```

`portraitOf` (the one expression→field map), the layout arithmetic, and every drawing rule are unchanged.

## `state/SharedState.fs`

```fsharp
// before
{ mutable Gpu : Gpu.Gpu
  Assets : Assets.AssetManager
  Cast : Casting.Cast            // removed
  mutable Advents : Set<Advent> }

// after
{ mutable Gpu : Gpu.Gpu
  Assets : Assets.AssetManager
  mutable Advents : Set<Advent> }
```

`SharedState.create` no longer builds a `Casting.Cast`.

## Editor — where the speaker control lives

| Surface | Control | Source |
|---------|---------|--------|
| cue `Exposit` (portrait) | **speaker combo** | discovered actor names — `CueTree.drawEditor`, fed `Assets.actorNames` by `CueEditor` |
| Dialogue panel | **speaker combo** | discovered actor names — `DialoguePanel.draw`, from `shared.Assets` |
| both | **expression combo** | `Dialogue.emotions` (unchanged) |

`CueTree.drawEditor` gains a `names: string list` parameter. A newly-chosen portrait defaults to the first
discovered name (or `""`), replacing `List.head Roster.characters`. The Cast panel is deleted.

## `world/Cue.fs` — the actor `…State` cases carry the handle

```fsharp
// before (spec 073): the run form re-resolved the actor by name every tick
| BlendState of actor: string * morph: bool * fromName: string * toName: string
              * initialFrom: float32 * initialTo: float32 * target: float32 * duration: float * endsAt: float
| MoveActorState of actor: string * points: Vector3[] * speed: float * duration: float * endsAt: float
                   * initialRotation: Quaternion * finalRotation: Quaternion * blend: float
                   * movingClip: string * idleClip: string
| RotateActorState of actor: string * initialRotation: Quaternion * targetRotation: Quaternion
                    * duration: float * endsAt: float

// after: each carries the resolved slot, captured once at begin
| BlendState of actor: string * place: Place * handle: Handle * morph: bool * fromName: string
              * toName: string * initialFrom: float32 * initialTo: float32 * target: float32
              * duration: float * endsAt: float
| MoveActorState of actor: string * place: Place * handle: Handle * points: Vector3[] * speed: float
                   * duration: float * endsAt: float * initialRotation: Quaternion * finalRotation: Quaternion
                   * blend: float * movingClip: string * idleClip: string
| RotateActorState of actor: string * place: Place * handle: Handle * initialRotation: Quaternion
                    * targetRotation: Quaternion * duration: float * endsAt: float
```

`Handle` is `core/SlotMap.fs`'s `{ Index : uint32; Generation : uint32 }`, compiled below `world/Cue.fs`; `Place`
is already named by the module. The actor `string` stays (the row label and diagnostics). The authored ops are
unchanged — they still name the actor by `string` (FR-019).

## `world/CueEngine.fs` — the step

```text
beginMove/Rotate/Blend:  (place, zone, ref) = tryResolveActor state actor      // once
                         …State (actor, place, handleOf ref, …)

stepMove (actor, place, handle, …):
    if not (state.Zones.ContainsKey place) then true          // place gone -> end
    let zone = state.Zones.[place]                            // indexer, not TryGetValue (no tuple alloc)
    if not (zone.Actors.IsAlive handle) then true             // slot dead/recycled -> end
    let actor = &zone.Actors.Get handle                       // O(1)
    if actor.State.Base.Name <> <stored name> then true       // re-check identity -> end
    …existing step…
```

`stepMove`/`stepRotate` also keep the `place <> state.ActiveZone` end (a local compare now, no scan); `stepBlend`
does not. The one-shot ops (`RemoveActor`, `EnablePlayer`/`DisablePlayer`, `begin*`) keep `tryResolveActor`.

## Validation rules

- **Exact, ordinal, deterministic**: the speaker resolves by `Name` equality; the lowest `Id` wins a shared name.
- **A miss is a value**: no discovered asset, or none declaring portraits, leaves the line undrawn and is reported
  in the panel — never raised (`specs/038` FR-006).
- **No binding, no look, no roster in the game**: `world/Dialogue.fs` and `world/DialogueRender.fs` name no
  `Roster`/`Casting` value.
- **Allocation-free draw**: the resolve is a contains + indexer on the scan-time index (FR-011).
- **No self-rescan on the hot path**: an in-flight actor op resolves its actor in O(1) from the carried `Handle`,
  ends on an absent place / dead slot / changed name, and never switches to another actor of the same name
  (FR-014–FR-017).
- **Engine unchanged**: the relocated `Roster` and the narrative suites are untouched (SC-003).
