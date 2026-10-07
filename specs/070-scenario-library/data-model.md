# Phase 1 — Data Model: Scenario Library

Everything below is authoring/editor data (heap, off the hot path) except where noted; nothing here runs per frame.

## Scenario (vocabulary)

`world/Scenario.fs` — the declared set. Pure, dependency-free, compiled before `world/Cue.fs`.

| Field | Type | Notes |
|-------|------|-------|
| case | `Intro` (union case) | The declared scenarios. Opening with one; adding one is a case here. |

| Function | Signature | Rule |
|----------|-----------|------|
| `scenarioName` | `Scenario -> string` | The case name, matched over the cases (as `Place.placeName` / `Advent.name`). |
| `all` | `Scenario list` | Every case in declaration order, derived via reflection (as `Place.all`). |

**Identity & uniqueness**: a scenario is a value of a closed union; two scenarios are equal iff the same case.
Nothing identifies a scenario by a string.

**Relationship**: a scenario is the key of a `Registry` entry; it names no zone, no asset and no device.

## ScenarioZone (the ref-free authored zone)

`world/ScenarioZone.fs` — a zone as a **scenario** holds it. Deliberately **not** `ZoneContents`: that type
exists to reverse a delete (it keeps each entity's `EntityRef` and the live `Environment`), none of which a
scenario can use. `ScenarioZone` is the de-ref'd authored record, arrays throughout.

| Field | Type | Notes |
|-------|------|-------|
| `Environment` | `ScenarioEnvironment option` | The environment as authored — asset, base, orientation, parts — not the live `Environment` (no epoch, no parts slot map). |
| `Light` | `Texture.LightSettings` | The zone's light, valid with or without an environment. |
| `Actors` | `ActorState array` | Authored actor states, in slot order. |
| `Props` | `PropState array` | Placed props (authored). |
| `Waypoints` | `ScenarioWaypoint array` | Chains link by **index into this array**, not by `EntityRef`. |
| `Triggers` | `TriggerState array` | Authored trigger states (the cue is an immutable value). |
| `Player` | `int option` | The designated actor as an **index into `Actors`**, not a slot handle. |

```fsharp
type ScenarioEnvironment =
    { AssetId : string; Base : Entity; Orientation : Quaternion; Parts : PropState array }

type ScenarioWaypoint =
    { Base : Entity; Next : int option }
```

| Function | Signature | Rule |
|----------|-----------|------|
| `capture` | `Zone.Zone -> ScenarioZone` | Pure and device-free. De-refs every entity (drops the `(ref, state)` pairing), reads the environment as authored, and resolves each waypoint's `Next` to an index (two passes) and the player handle to an actor index. |

**Why its own type (FR-012).** The only references are the four surfaces `ZoneContents` must keep: the
`(EntityRef * State)` pairs, the live `Environment` (its epoch), `Waypoint.Next`, and `Player`. A scenario must
carry none of them — a fork instantiates into fresh zones — so each is re-expressed (arrays, an authored
environment, an index, an index). `docs/ZONE_MODEL_DECISION.md` records why the delete's snapshot is not the
scenario's shape.

**Loading is deferred.** `capture` only; the inverse (`occupy`, the fork) is the later increment, and would need
the renderer/physics paths a headless test cannot exercise.

## ScenarioContents (the captured value)

`world/ScenarioRegistry.fs` — one scenario's captured state. Immutable, so an undo entry can hold it.

| Field | Type | Notes |
|-------|------|-------|
| `Zones` | `Map<Place, ScenarioZone>` | Every present zone, keyed by the place it sits at. Each value is the ref-free record `ScenarioZone.capture` answers. |
| `Active` | `Place` | The place active at capture (clarified: part of the state). |
| `Cue` | `Cue.Cue` | The screen's authored cue. A cue is an immutable value, so this is a reference. |

**Validation**: `Zones` is non-empty in any capture (the active place is always present). Each `ScenarioZone`
carries its environment/light, parts, props, actors, waypoints and triggers, and its player designation — all
ref-free.

**No identity is stored**: nothing in a `ScenarioZone` names a slot in the zone it was captured from, so the
deferred fork can materialise it in a fresh zone without discarding anything (`spec.md` FR-012, assumption
"A scenario zone is its own type").

**Actor tags are derived, not stored.** For its row's tag list the panel reads the cast characters
(`ActorState.Cast`) across every `ScenarioZone.Actors` and shows them sorted (FR-013) — the shape the Outliner's
zone rows use. Nothing extra is captured for them.

## Registry (the sparse store)

| Field | Type | Notes |
|-------|------|-------|
| `Entries` | `Dictionary<Scenario, ScenarioContents>` | Sparse: a key present means the scenario holds contents; absent means it does not. |

| Function | Signature | Rule |
|----------|-----------|------|
| `create` | `unit -> Registry` | An empty library. |
| `tryContents` | `Scenario -> Registry -> ScenarioContents option` | The panel's question. An expected absence, so `option` (as `ZoneRegistry.tryTemplate`). |
| `setContents` | `Scenario -> ScenarioContents option -> Registry -> unit` | The one write: `Some` files an entry, `None` removes it. The only place the option rule lives; the state-level runner calls it with `Next` (apply) and `Previous` (undo). |
| `capture` | `Dictionary<Place, Zone.Zone> -> Place -> Cue.Cue -> ScenarioContents` | Pure, device-free (spec FR-009): maps each live zone through `ScenarioZone.capture` into the immutable `Map`, and pairs in the active place and cue. |

## The step (a command payload)

`state/Command.fs` / `state/History.fs` — the registration as one undoable step, state-level (not a zone edit).

| Case | Payload | Apply | Undo |
|------|---------|-------|------|
| `SetScenario` | `Name : Scenario * Previous : ScenarioContents option * Next : ScenarioContents option` | `setContents Name Next` | `setContents Name Previous` |

**State transitions** (a scenario's registration state, driven by the one history):

```
unregistered --Register--> registered --Register(overwrite)--> registered
     ^                          |                                    |
     |                          | Clear / undo of first Register     | undo of overwrite
     +--------------------------+------------------------------------+--> previous contents
```

- **Register over unregistered**: `Previous = None`, `Next = Some captured`. Undo → `None` (unregistered).
- **Register over registered**: `Previous = Some old`, `Next = Some captured`. Undo → `Some old`.
- **Clear**: `Previous = Some old`, `Next = None`. Undo → `Some old`.
- **Redo** re-applies `Next` in every case.

**Invariants**:

1. A step changes only `Registry.Entries`; it touches no zone, no entity, no camera, no selection (spec FR-008,
   SC-005).
2. Every step is one entry on `EditorState.Undo`; the panel keeps no history of its own.
3. The step is refused by the **zone-level** `Command.apply`/`undo` (like `SetCue`/`Advent`) — reaching one there
   is a broken invariant, not a value to ignore (`Command.fs`'s state-level `invalidOp` group).
4. Play's `History.pruneCueSteps` removes `SetCue` steps only; a `SetScenario` step survives Play, and the cue it
   holds is data inside the step, not a step of its own.
