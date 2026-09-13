# Architecture

This is the reference for `fsharp-game`'s data architecture, derived from
`odin-game`. The goal is to reproduce odin-game's data-oriented design in F#,
leveraging F#'s structs and the GC rather than fighting them.

## Guiding principles

- Data-driven and cache-conscious: keep related runtime data in contiguous,
  fixed-size arrays of structs.
- No ECS framework — plain structs, slot maps, and procedural systems.
- Generational slot maps for O(1) add/get/free with stale-reference detection.
- Hot-path code is imperative; functional style is welcome off the hot path.
- Separate runtime state from authoring/template state and from the wire
  format.

## Core container: generational slot map

The foundational structure (odin-game `src/slot_map.odin`):

- A slot holds `{ entity, generation, alive, next_free }`.
- A slot map is a **fixed-size array of slots** plus `count` and a `free_head`
  free-list.
- Add/get/free are O(1); deletion reuses indices without index fix-ups, and a
  generation counter detects stale handles.
- Slots are exposed as a read-only span (`Slots`) so systems scan directly, and
  writes go through `Get` (by `Handle`: index + generation). Undo restores through
  `Occupy`, which re-occupies a deleted entity's recorded (free) slot and restores
  its generation; a slot unexpectedly taken fails loudly rather than relocating.

```text
for i in 0..<N:
    if slots[i].alive: ...
```

This is the core locality win: per-zone entity storage is inline, not
heap-allocated per entity.

## Entity model

- `Entity_Kind` discriminates `Actor | Environment | Prop | Part | Waypoint | Trigger`.
- `Entity_Ref { kind, index, generation, epoch }` is a lightweight handle (not a
  pointer). Deleting bumps the slot generation, so a stale ref fails lookup
  instead of aliasing a reused slot. `epoch` scopes a Part ref to the environment
  that produced it (0 for other kinds): each environment's parts map starts its
  generations at 0, so without it a part ref could resolve against a same-index
  part in a different environment. Absence is `EntityRef option` in the F# port,
  not Odin's `NO_ENTITY` sentinel.
- `Entity` is the shared base: `name`, `transform`, `enabled`, `visible`,
  `selectable`, `draggable`.
- Specialized entities compose the base (Odin uses `using entity: Entity`):
  - `Actor_Entity` — renderer pointer, physics, debug flag.
  - `Environment_Entity` — its own `parts` slot map (the imported glTF parts),
    mesh indices, renderer, splat/grass pointers.
  - `Prop_Entity` — a placed prop: collision data, render-transform cache, water
    params. Environment parts reuse the same shape.
  - `Waypoint`, `Trigger` — standalone (no `Entity` base).
- A part is addressed by `EntityKind.Part` into the current environment's own
  parts map, scoped by the ref's `epoch`, so environment churn never touches the
  zone's placed props/actors and a stale part ref can't alias another
  environment's part.

## Zone = world container

`Zone` owns one playable area: `actors`, `environment` (which owns its own
`parts`), `waypoints`, `triggers`, plus inline global settings (wind, lighting,
skydome, ocean). Zones are heap-allocated; their entity collections are
fixed-size inline arrays.

Two zone containers exist on purpose:

- `Zone_Map` — runtime. `Slot_Map(^Zone)` + an `active` index ("which zones
  exist, which is active").
- `Zone_Registry` — editor templates. Entries carry `environment_asset_id`,
  `settings`, `prop_template`, splat maps, and `template_saved` for
  authoring/persistence.

In the F# port the runtime container is folded into the gameplay screen: zones live
in a `Dictionary<string, Zone>` keyed by the zone's unique name, with `ActiveZone`
naming the active one (zones are addressed by identity and never bulk-iterated, so a
slot map buys nothing). Only the active zone's bodies are attached to the one shared
physics world: a switch detaches the outgoing zone with `BodyInterface.RemoveBody`
and re-attaches the incoming one with `AddBody`, then re-optimises the broadphase, so
inactive zones neither collide nor integrate without rebuilding any shape or body.
Deleting a zone destroys its bodies and releases its GPU resources. The player
designation and the undo history are held per zone.

## Systems

Systems are plain procedures that take `zone`/`screen`/`shared` + `dt` and
mutate data in place. `play_mode_systems_update` chains them in a fixed order:

```text
player_input → gravity → physics → animation → sway → trigger →
interaction → exposition → camera_follow → collider sync → clock → cue
```

Iteration is direct scan loops over the fixed arrays — no component registry or
archetypes.

## State management

- `Shared_State` holds cross-screen singletons: `cues`, `assets`,
  `scenario_library`, `selected_scenario_index`, `advents`, `cast_members`,
  `zone_registry`.
- `Screen_Manager` owns `Shared_State` plus one stateful struct per screen,
  with init/activate/deactivate/update/draw per screen.
- `Gameplay_Screen` holds per-gameplay hot state (`zones`, `player_locomotion`,
  `camera_follow`, `mode`, `clock`, `camera`, `interactable_trigger`, `fade`)
  and cold state (`exposition`, `editor`). Hot/cold fields are grouped.

## Persistence

Uses a DTO pattern: `Project_DTO`/`Project_Zone`/`Project_Prop`/
`Project_Scenario` structs with JSON tags, separate from the runtime model.
`project_save`/`project_load` convert between them; saves are atomic
(`.tmp` + rename). A missing file is not an error. Runtime/player save state is
out of scope — only cast, zone-registry templates, and the scenario library are
persisted.

## Assets

`Asset_Manager` holds `actors`, `environments`, `props` (dynamic arrays) plus an
LRU model cache and async loader. Assets are scanned from disk at startup and
parsed from s-syntax manifests, keyed by `id`.

## Scenario & Cue (template/runtime split)

- `Scenario_Library` → `Scenario` = fixed zones of authoring data plus a
  `cue_root`. Scenarios are immutable at runtime (heap maps, off hot path).
- `Cue` = `{ template_root, state_root, state_map }`.
- `Cue_Op` is a tagged union of op types (leaf data + recursive control flow
  via pointers: Sequence/Parallel/Fork/If).
- `CueState` is a *separate* union holding only runtime progress for the
  stateful ops. Template and running state are kept apart.

```mermaid
graph TD
    SM[Screen_Manager] --> SS[Shared_State]
    SM --> GS[Gameplay_Screen]
    SS --> AM[Asset_Manager]
    SS --> SL[Scenario_Library]
    SS --> ZR[Zone_Registry]
    SS --> C[Cue]
    SS --> ADV[Advent_Set]
    SS --> CAST[Cast_Members]

    GS --> ZONES["zones: Dictionary(string, Zone)"]
    GS --> ACTIVE["active zone name"]
    ZONES --> Z[Zone]

    Z --> SA["actors: Slot_Map(Actor_Entity)"]
    Z --> ENV["environment: Environment_Entity"]
    Z --> SW["waypoints: Slot_Map(Waypoint)"]
    Z --> ST["triggers: Slot_Map(Trigger)"]

    ENV --> SP["parts: Slot_Map(Prop_Entity)"]

    C --> COP["Cue_Op (template union)"]
    C --> CST["CueState (runtime union)"]
    C --> CSM["state_map: Slot_Map(CueState)"]
```

## F# port decisions

- **Slot map** → a generic sealed class holding a fixed `Slot` array plus the
  free-list head and count, mutated in place. It is deliberately *not* an
  immutable struct: copies would desync from the shared storage. `Slot`/`Handle`
  stay `[<Struct>]`, `Slots` is a read-only span, and `Get` returns a `byref` into
  the payload.
- **`using entity: Entity`** → no struct inheritance in F#; model as a nested
  `Entity` field inside each specialized record.
- **Inline `[N]u8` strings → `string`.** The runtime `Entity.name` in odin-game
  is already a heap `string`; the fixed `[N]u8` fields appear only in
  `Waypoint`, `Trigger`, `Zone`, cue/scenario templates, and the `Project_*`
  DTOs — i.e. editor/authoring/serialization data, not the per-frame hot path.
  So the port uses `string` everywhere and drops the fixed-size byte arrays.
- **DTO serialization** → `System.Text.Json` (with converters/attributes) or
  `FSharp.Data`. Keep the DTO-vs-runtime-model separation.
- **Systems** → top-level `unit`-returning functions mutating
  `Zone`/`GameplayScreen`/`SharedState`, called with `do`. This matches the
  AGENTS.md guidance.
- **Zone containers** → the runtime container is a name-keyed
  `Dictionary<string, Zone>` on `GameplayState` with an `ActiveZone` name (not a
  `Zone_Map` slot map: zones are addressed by identity and never bulk-iterated).
  `Zone_Registry` (authoring templates) stays a distinct structure.
- **Jolt ownership** → `PhysicsSystem` holds only *raw pointers* to the object-layer
  pair filter, broad-phase interface, and object-vs-broadphase filter, so the `World`
  must keep their managed wrappers (and its `PhysicsSystemSettings`) alive as fields.
  If the GC finalises them, native collision silently stops and dynamic bodies fall
  through all geometry. Shapes are different: a body ref-counts its shape natively, so
  a shape wrapper may be collected once the body exists.

The locality to preserve is the slot maps and fixed-size struct arrays — not
inlined strings.
