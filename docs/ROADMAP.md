# fsharp-game — Roadmap

**Status**: Draft (2026-08-27).

This roadmap is derived from **odin-game's git history** (the "what" and the
proven build order) and **ocaml-game's editor design** (the "how"), adapted to
F# + SDL3/SDL_gpu + ImGui + Jolt Physics. It deliberately departs from ocaml-game's
foundation-first milestone order in favour of keeping something runnable at
every step. It puts the **app shell (screens) first**, then the **"walk around an
environment with physics"** and **PBR** milestones early, before any
content-authoring (cues/scenarios).

## Vision

A narrative, story-driven 3D game built data-oriented: playable **zones**
(terrain, water, actors, props, waypoints, triggers), scripted **cues**
(Sequence/Parallel/Fork sequences), **scenarios** assembled from zones + cast
members, **adventure flags** for story state, physics (capsule actors +
box/BVH props), and a full **in-engine editor** to author all of it.

## Principles

- **Something runnable at every step.** Each phase ends with a visible result
  you can look at or play; it is never a pure abstraction.
- **Screens first.** Stand up the app shell (screen manager + main menu +
  gameplay screen, UI-only) before any 3D — it's cheap and de-risks the structure.
- **Get a character on screen as the first 3D result**, not "math + camera +
  triangle" in the abstract.
- **Get to "walk around an environment with physics" early** (phase 7). A
  playable character in a physical world is the backbone everything else hangs
  off.
- **Get PBR in early** (phase 5) — establish the real visual baseline *before*
  the cue/scenario content-authoring, so the editor and authored content are
  built against the actual look, not placeholder shading.
- **Bring the editor in early** — right after the entity store.
- **Physics early** — colliders + raycast picking are part of the core loop.
- **Refactor after it works.** The slot map, zones, immutable cue, and systems
  extraction in odin-game were *refactors* on top of a working game, not
  prerequisites. We port the *final* shape directly, but still build it
  incrementally.
- **Port the final architecture, not the intermediate states** — see
  `docs/ARCHITECTURE.md` for the target data model.

## Current state

- Phase 1 (screens + main menu) is complete: `Engine.fs` run loop, `screen/`
  screen manager + main menu + gameplay placeholder, and an on-screen frame rate
  counter (`screen/DebugOverlay.fs`).
- SDL3 window + SDL_gpu device + swapchain loop with a software 60 FPS cap.
- Dear ImGui wired through the SDL_gpu backend, docking-enabled
  (`render/ImGui.fs`, `render/ImGuiSdl3.fs`, `native/`).
- Jolt Physics (JoltPhysicsSharp) wired in: `physics/Physics.fs` (world, box/capsule
  bodies, raycast), colliders for actors/props, and accurate collider picking.
- Target data architecture documented in `docs/ARCHITECTURE.md`.

## The phases

Each phase is a coherent, committable increment. "Result" is what you can see or
play at the end of the phase. Phases 1–7 form the **playable-and-visible** spine
(shell → editor → character → world → PBR → physics → walking); phases 8+ are
content-authoring and polish on top.

| # | Phase | Result | Reference |
|---|-------|--------|-----------|
| **1** | **Screens + main menu** | screen manager, main menu ↔ gameplay (UI-only shell) | `odin-game/src/{screen,main_menu_screen,gameplay_screen}.odin`; `ocaml-game` M4 |
| **2a** | **Entity store** | generational slot map + entity model, verified by tests | `odin-game/src/{slot_map,entity}.odin`; `ocaml-game/lib/core/{slot_map,entity}.ml` |
| **2b** | **Editor workspace shell** | docking, viewport, outliner, inspector, asset browser (empty scene) | `odin-game/src/editor*.odin`; `ocaml-game` M5 |
| **2c** | **Selection + transform** | raycast select, move/rotate/scale gizmos, spawn/delete/duplicate | `odin-game/src/editor*.odin`; `ocaml-game` M5 |
| **2d** | **Command/undo** | undo/redo for every editor mutation | `ocaml-game` D-011; `state/Command.fs` |
| **3** | **Animated character on screen** | an animated actor in a 3D viewport | `odin-game` IQM loading; `ocaml-game/lib/core/{iqm,actor,assets}.ml` |
| **4** | **Environment + props** | a static environment + prop assets you can see | `odin-game/src/{environment,environment_gltf,environment_render}.odin`; `ocaml-game/lib/core/gltf.ml` |
| **5** | **PBR + lighting** | metallic-roughness shading (GGX), materials, sun/directional light | `odin-game/src/environment_render.odin`, `docs/PBR_PLAN.md`; `ocaml-game` M13 |
| **6** | **Physics + picking** | capsule character + box/BVH prop colliders, raycast click-select | `odin-game/src/physics.odin`; `ocaml-game` M6 (Box3D + raycast) |
| **7** | **Walk around** | player locomotion + camera follow; walk the character around the environment with physics | `odin-game` `player_input`/`camera_follow`/`gravity` systems; `ocaml-game` M11.1–M11.2 |
| **8** | **Zones** | multiple zones: add, select, delete, switch | `odin-game/src/{zone_map,zone_registry}.odin` (runtime part); `specs/024` |
| **9** | **Cue engine + editor** | author and run a scripted sequence | `odin-game/src/{cue,cue_editor}.odin`; `ocaml-game/lib/core/cue.ml`, `docs/CUE_DESIGN.md` |
| **10** | **Story primitives** | waypoints, triggers, advent, cast | `odin-game/src/{cast,advent}.odin` + zone waypoints/triggers; `ocaml-game` M7 |
| **11** | **Scenario editor** | assemble a scenario from zones + cast + cues | `odin-game/src/{scenario,scenario_editor}.odin`; `ocaml-game` M9 |
| **12** | **Zone registry + editor** | author zone templates/settings | `odin-game/src/{zone_registry,zone_editor}.odin`; `ocaml-game` M10; `specs/034-zone-registry` |
| **13** | **Systems + fixed timestep + immutable cue** (refactor) | named systems, live-edit-safe cues | `odin-game` systems-extraction commits; `ocaml-game` M11 / D-010 |
| **14** | **Advanced rendering** | shadows → water → grass → sky → ocean → sway → cel-shading | `odin-game/src/{shadow,water,grass,skydome,ocean,sway}.odin`; one spec each |
| **15** | **Persistence + perf** | save/load `project.json`, async loading, LRU cache | `odin-game/src/project.odin`; `ocaml-game` M12/M14 |

The phases are **dependency-ordered**, but several are intentionally parallel
after the core (e.g. 10/11 vs 12, and the sub-features of 14).

## Phase details

Each phase lists the concrete **deliverables** (modules + behaviour) and the
**"done when"** acceptance criteria. Phase 1 is complete.

### 1 · Screens + main menu ✅

**Goal**: the app shell — screen manager, main menu, and a UI-only gameplay
screen on the existing ImGui + SDL_gpu loop.

**Deliverables**
- `state/Screen.fs` — `ScreenKind` + `ScreenTransition`.
- `screen/ScreenManager.fs` — owns the active screen, dispatches update/draw,
  applies the transition at frame end.
- `screen/MainMenuScreen.fs` (Play/Quit) and `screen/GameplayScreen.fs`
  (UI-only placeholder).
- `state/SharedState.fs` / `state/GameplayState.fs` — empty state seams for
  later phases.
- `Engine.fs` (run loop) split out of `Program.fs`, which becomes a thin entry
  point. Move `ImGui.fs`/`ImGuiSdl3.fs` into `render/`.

**Done when**: launch lands on the menu; Play switches to the gameplay
placeholder; Quit (or window close) exits cleanly. *(complete — `specs/001`)*

### 2a · Entity store

**Goal**: the data core — generational slot map + entity model, the foundation
every later phase builds on (no visible result yet; verified by tests).

**Deliverables**
- `core/SlotMap.fs` — generational slot map: a sealed class over a fixed slot
  array (`{ value, generation, alive, next_free }`), free-list, generation
  counter; O(1) add/get/free with stale-reference detection, mutated in place.
  A read-only `Slots` span for scans, a `byref` `Get` (by `Handle`: index +
  generation), and a
  generation-restoring `Occupy` for undo (re-occupy the deleted entity's recorded
  slot, which is always free; a taken slot raises).
- `world/Entity.fs` — `Entity_Kind` (`Actor|Environment|Prop|Part|Waypoint|Trigger`),
  `Entity_Ref { kind, index, generation, epoch }` (absence via `option`), base
  `Entity` (`name`, `transform`, `enabled`, `visible`, `selectable`, `draggable`),
  and the specialized entities (each embeds a nested `Entity`; no struct
  inheritance in F#).

**Done when**: `test/SlotMapTests.fs` proves add/get/free, free-list reuse, and
stale-handle rejection.

### 2b · Editor workspace shell ✅

**Goal**: the editor window layout with an (empty) navigable 3D viewport and a
populated asset browser.

**Deliverables**
- `world/Assets.fs` — asset manager: scan `actors/`/`environments/`/`props/` + s-syntax
  manifests, keyed by `id`.
- `render/DebugDraw.fs` — line/wireframe primitive (grid; later gizmos/overlays).
- `render/Camera.fs` — editor camera (pitch/yaw/pan/zoom).
- Docking workspace: `editor/Editor.fs` (docking + menu bar + shortcuts),
  `state/EditorState.fs`, `editor/Viewport.fs`, `editor/Outliner.fs`,
  `editor/Inspector.fs`, `editor/AssetBrowser.fs`.
- Backtick (`` ` ``) play/edit toggle, hiding/showing the workspace while the
  viewport grid stays visible.

**Done when**: the editor opens with dockable Viewport (grid + orbit camera),
Outliner, Inspector, and Asset Browser panels, and the browser lists discovered
assets. *(complete — `specs/003`)*

### 2c · Selection + transform

**Goal**: interact with entities in the viewport.

**Deliverables**
- `editor/Raycast.fs` — minimal screen-ray → sphere/box selection.
- Selection + focus (`F`), delete (`Del`), duplicate (`Ctrl+D`).
- Translate/rotate/scale gizmos (axis picking reuses the raycast).
- Spawn from the Asset Browser into the active zone.

**Done when**: you can spawn an entity, select it, and move/rotate/scale it with
gizmos (rendered as debug-draw markers).

### 2d · Command/undo

**Goal**: make every editor mutation undoable/redoable.

**Deliverables**
- `state/Command.fs` — value-storing commands (`apply`/`undo`), not closures;
  `EntityRef`-based entity commands survive deletion.
- Undo/redo stack + `Ctrl+Z` / `Ctrl+Shift+Z`.
- Continuous-edit coalescing (a gizmo/slider drag = one undo step).

**Done when**: spawn/delete/duplicate/transform are all undoable and redoable,
and a gizmo drag coalesces into a single undo step.

### 3 · Animated character on screen

**Goal**: the first real 3D result — an animated actor in a 3D viewport. This is
the hard plumbing step (SDL_gpu has no `LoadModel`/`DrawMesh` helper).

**Deliverables**
- `render/Gpu.fs` — device/swapchain/pass wrapper; vertex+index buffers, one
  graphics pipeline, one shader.
- `render/Mesh.fs` — GPU buffers + draw path.
- `core/Iqm.fs` — IQM binary parser emitting flat struct-of-arrays
  (`System.IO.BinaryReader`, little-endian, `float32` data).
- `world/Actor.fs` — IQM model + animation slots + CPU skinning.
- `world/ActorRender.fs` — skinned actor rendering.

**Done when**: an animated character (e.g. Akane) is visible and animating in
the viewport via the asset manifest.

### 4 · Environment + props

**Goal**: static world geometry, reusing the phase-3 mesh-draw path.

**Deliverables**
- `core/Gltf.fs` — scoped glTF parser (environments/props).
- `world/Zone.fs` — the world container: `actors`, `environment` (owning its own
  `parts` slot map), `waypoints`, `triggers`, plus inline global settings.
- `world/Entity.fs` — `Environment_Entity` (owning its parts map) / `Prop_Entity`
  / `Part`.
- `world/EnvironmentRender.fs` — static environment + prop rendering.

**Done when**: a static environment and prop assets are visible in the viewport.

### 5 · PBR + lighting

**Goal**: establish the real visual baseline before any content authoring.

**Deliverables**
- Upgrade the phase-3 pipeline/shader to metallic-roughness GGX.
- glTF material mapping (metallic-roughness) onto SDL_gpu pipelines.
- Directional (sun) light parameters + the SPIR-V/GLSL shader.

**Done when**: environment/character render with PBR materials under a
directional light.

### 6 · Physics + picking

**Goal**: physical world plus accurate selection.

**Deliverables**
- `physics/Physics.fs` — Jolt adapter: world, capsule character body, box prop
  colliders, and `PhysicsSystem.Update` (step).
- Collider↔render transform sync.
- Accurate raycast picking (screen-ray → collider/BVH), replacing the phase-2
  minimal raycast.

**Done when**: character/props have colliders and click-select hits physical
shapes.

### 7 · Walk around ✅

**Goal**: the playable backbone — walk the character around an environment with
physics.

**Deliverables**
- `world/Systems.fs` — player systems gated on a bound player: `playerInput`
  (tank controls), `cameraFollow`, `animate`, plus `syncActors`, the non-gated
  body→entity sync for every actor.
- Dynamic, rotation-locked actor capsules — gravity and collision come from the
  physics step, and the actor's existing body is the player body.
- A deliberate *Set as Player* editor action; no enter/leave-play callbacks.
- The fixed-order play-mode chain: `player_input → physics → sync → camera_follow`
  (grows in phase 13).

**Done when**: you can walk the character around the environment with collisions
and a following camera. *(complete — `specs/016`)*

### 8 · Zones ✅

**Goal**: multiple zones with one active; add, select, delete, and switch.

**Deliverables**
- A `Dictionary<Place, Zone>` on `GameplayState` plus `ActiveZone : Place` (a zone's identity is
  its declared `Place`, a closed union in `world/Place.fs`; no `world/ZoneMap.fs` — a dictionary
  suffices for identity-addressed zones). A place is either **present** (it has a zone) or
  **absent**; the active place is always present.
- Zone switch path: detach the outgoing zone's bodies and re-attach the incoming
  zone's (`RemoveBody`/`AddBody` + broadphase optimize) in the one shared world.
- Per-zone player designation and undo history, plus add/select/delete in the
  Outliner's Zones section.
- Deleting a zone releases its bodies and GPU resources, unloading its environment unless another
  present place shares it; the active place cannot be removed.

**Done when**: multiple zones exist and you can add, select, delete, and switch
between them at runtime. *(complete — `specs/024`, and place-keyed identity is `specs/061-zones-as-places`)*
Cue-driven zone ops (`AddZone`/`RemoveZone`/`PreloadZone`) are `specs/062-zone-cue-ops`.

### 9 · Cue engine + editor

**Goal**: author and run a scripted sequence.

**Deliverables** *(in two features: the engine — `specs/046` — then the editor)*
- `world/Cue.fs` — one type that is the vocabulary **and** the run (`Sequence`/
  `Parallel`/`Fork`, the leaf ops, and the terminal `Fin`); `world/CueEngine.fs` —
  `advance` / `tick` / `forkInto`. No `CueState` union and no `state_map`: measured and
  retired (`docs/CUE_MODEL_DECISION.md`). *(complete — `specs/046`)*
- Runtime cue execution in the play chain, on play time. *(complete — `specs/046`)*
- `editor/CueEditor.fs` + `editor/CueTree.fs` — recursive tree-list editor (not a
  node graph), one shared control, a row per container, and read-only for what is
  executing. An edit is a `SetCue` step on the one undo history (spec 049), so the
  panel keeps no history of its own and Play prunes the cue's steps. *(complete —
  `specs/047`, cue history `specs/049`)*

**Done when**: you can author and run a scripted sequence in the cue editor.
*(Complete: the engine is `specs/046`, the editor is `specs/047`.)*

### 10 · Story primitives

**Goal**: waypoints, triggers, adventure flags, and cast.

**Deliverables**
- `Waypoint` / `Trigger` entities in `world/Entity.fs` (or `world/Zone.fs`).
- `world/Advent.fs` — named flag set *(a string set since `specs/082-advent-strings`)*; `editor/AdventPanel.fs` — the docked **Advent** panel; and the
  cue ops that read and write the flags (`AddAdvent`, `RemoveAdvent`, `Await`, `If`).
  *(complete — `specs/055-advent-flags` for the flags and the panel, `specs/056-advent-cue-ops` for the
  cue ops)*
- cast — retired (`specs/074-retire-cast`): the look vocabulary, the slot → actor asset binding, the Cast
  panel and the roster all left the game. The closed `Character` union is now the narrative engine's
  (`narrative/Roster.fs`), and a portrait line's speaker — like a cue's actor — is a plain **string**,
  resolved to her actor asset by name (a name is a label, so duplicates resolve to the first match). The
  binding had been delivered first by `specs/032-cast-binding` (roster in `cast/Roster.fs`, binding in
  `world/Casting.fs`, panel in `editor/CastPanel.fs`); the **runtime** half moved to addressing **by name**
  first (`specs/072-actor-name-addressing`, `specs/073-cue-actor-by-name`): an actor carries no role, and a
  cue names her by a plain string — her `Name` — in an explicit place (`specs/075-cue-zone-selector`), resolved
  within that place's zone; `specs/063-cast-index`'s role and runtime index were withdrawn. The actor lifecycle cue ops — `AddActor`
  (a pure spawn that names a discovered actor asset plus a typed name) and `RemoveActor` — landed in
  `specs/064-actor-cue-ops` and were re-based by
  `specs/072`/`specs/073`, and the rest of
  odin's actor family in `specs/066-actor-motion-cue-ops`: the animation/morph blends (`Animate`/`CrossFade`/
  `Morph`/`CrossMorph`, renamed `CrossFadeAnimation`/`CrossFadeMorph` by `specs/088-music-cues` for parity with
  the music cross-fade), the movement ops (`MoveActor`/`RotateActor`, physics-driven and active-zone only) and
  the player toggles (`Enable`/`DisablePlayer`, the runtime form of the editor's "Set as Player").
  The trigger toggles (`Enable`/`DisableTrigger`) and `Print` landed in `specs/067-print-trigger-cue-ops`.
  Only odin's scenario ops (`Inline`/`Fork`/`ClearScenario`) and the camera-follow/interactions toggles remain.
- `world/Cue.fs` + `world/CueEngine.fs` — the **music cue ops** (`specs/088-music-cues`): a cue sets up the
  palette (`AddMusic`/`RemoveMusic`), transports it (`PlayMusic`/`StopMusic`, and whole-bed `PlayBed`/`StopBed`)
  and shapes it (`SetMusicLevel`, `SetMusicMaster`, `CrossFadeMusic`) against the mix `specs/087-song-mixing`
  authored. Every music op is a runtime effect that never lands on the one history (the `AddAdvent` pattern),
  and the engine composes `audio/Music.fs` as-is. One-shot sounds, start/loop points, ducking and
  spatialisation remain the deferred audio families.
- Trigger→cue wiring inline in the Inspector: a trigger owns an authored cue, drawn and edited inline, and
  forked into the screen's running cue when the trigger fires on entry (or on the interact key, when
  interactable). *(complete — `specs/058-trigger-cue`)*

**Done when**: you can place waypoints/triggers, wire triggers to cues, set
adventure flags, and manage the cast. *(Complete: `specs/026-waypoints`–`specs/029-triggers`, the cast and
the adventure flags, and trigger→cue wiring is `specs/058-trigger-cue`. The cast itself was later retired —
`specs/074-retire-cast`.)*

### 11 · Scenario editor

**Goal**: assemble scenarios from zones + cast + cues.

**Deliverables**
- `world/ScenarioRegistry.fs` — the scenario library: an ordered map (Prime's `OMap`) keyed by scenario **name**, from
  name to its captured contents (a scenario = every present zone, each a `ScenarioZone`, + the cue; immutable at
  runtime). `world/ScenarioZone.fs` — a zone as a scenario holds it (its **staging** — environment, light and placed
  props — plus its narrative entities as arrays of authored actor/waypoint/trigger states, with waypoint chains by
  index). `world/Scenario.fs`, the closed vocabulary union, is **retired**. *(the library and its panel are
  `specs/070-scenario-library`; string-keyed since `specs/083-scenario-strings`; the staging was restored to the
  scenario by `specs/079-scenario-owned-zones`)*
- `editor/ScenarioPanel.fs` — the scenarios, one row each, named by hand: a text field + **Add**, and each row's
  **Register** (file the current scene), **Clear**, **Fork** (run it) and **Delete**, all steps on the one undo
  history. *(complete — `specs/070-scenario-library`, `specs/078-fork-scenario`, `specs/083-scenario-strings`)*
- `world/ScenarioFork.fs` — the materialiser both entry points call (the `ForkScenario` cue op and the panel's Fork
  control): it ensures each zone by name (creating and staging an absent one, restaging a present one), appends its
  entities, and returns the commands that reverse it. *(`specs/078-fork-scenario`, `specs/079-scenario-owned-zones`)*

**Done when**: you can assemble and run a scenario. *(Both are in: assembling is the library, the panel and
registration (`specs/070-scenario-library`); **running** one is the Fork control and the `ForkScenario` cue op,
which materialise the scenario and fork its cue beside the running one (`specs/078-fork-scenario`). Odin's other
scenario cue ops `InlineScenario`/`ClearScenario` and the camera-follow/interaction toggles remain the deferred cue
families.)*

### 12 · Zone registry + editor — retired (`specs/079-scenario-owned-zones`)

**Goal**: author zone templates/settings.

**Deliverables**
- `world/Place.fs` — the game's places: a closed union whose cases carry their own variant sets
  (`ArtRoom of Period`, `Island`), so a zone that is not declared here cannot exist. `world/ZoneRegistry.fs`
  is the sparse map from a place to the zone registered there. `world/ZoneTemplate.fs` holds one
  entry's record: the environment asset, the environment's settings, and its parts as the author
  left them.
- `editor/ZoneRegistryPanel.fs` — the places as a tree, with a register control and an instantiate
  control per place.

**Done when**: you can author zone templates/settings in the editor. *(complete —
`specs/034-zone-registry`, and an entry's placed props are `specs/035-zone-template-props`. The places
vocabulary was hoisted to `world/Place.fs` by `specs/061-zones-as-places`. Waypoints,
triggers and splat maps are the deferred siblings of an entry's parts — read
[`ZONE_MODEL_DECISION.md`](./ZONE_MODEL_DECISION.md) before growing the entry, since it decides how the
entry's shape relates to a zone's snapshot; persisting the registry is
phase 15.)*

### 13 · Systems + fixed timestep + immutable cue (refactor)

**Goal**: deterministic stepping and a clean systems seam — a refactor on top of
the working game, not a rewrite.

**Deliverables**
- `world/Systems.fs` — named play-mode systems chained in the fixed order.
- Fixed-timestep loop (accumulator + fixed `dt`), decoupling simulation from
  render (replaces the software 60 FPS cap).
- Formalise the immutable-cue-template guarantee from phase 9.

**Done when**: systems are named/chained, simulation steps at a fixed `dt`, and
cues stay live-edit-safe.

### 14 · Advanced rendering

**Goal**: visual polish — each sub-feature is its own runnable increment/spec.

**Deliverables** (in order): shadows → water → grass → sky → ocean → sway →
cel-shading.
- `render/Shadow.fs`, `render/Water.fs`, `render/Grass.fs`, `render/Skydome.fs`,
  `render/Ocean.fs`, `render/Terrain.fs`, `render/TerrainSplat.fs`,
  `render/SceneTarget.fs` (offscreen FBO), `render/Exr.fs` (OpenEXR).

**Done when**: each sub-feature lands as a visible, committable increment.

> **Shadows (first deliverable)** were prototyped as spec 043 and then **reverted**; the spec directory was
> deleted. See [`SHADOWS_NOTES.md`](./SHADOWS_NOTES.md) for the design summary and the traps to fix on a
> retry (notably: the asset `Bounds` are local-space, so the light frustum must be fitted to the casters'
> *world* bounds).

### 15 · Persistence + perf

**Goal**: save/load and performance.

**Deliverables**
- `world/Project.fs` — `Project_DTO`/`Project_Zone`/`Project_Prop`/
  `Project_Scenario` DTOs (`System.Text.Json`), separate from the runtime model.
- Atomic save/load (`project.json`, `.tmp` + rename); missing file is not an error.
- Async asset loading + LRU model cache.

**Done when**: cast, zone-registry templates, and the scenario library round-trip
through `project.json`; assets load async with an LRU cache.

## Sequencing

```mermaid
graph TD
    P1[1 screens] --> P2a[2a entity store]
    P2a --> P2b[2b editor workspace]
    P2b --> P2c[2c selection + transform]
    P2c --> P2d[2d command/undo]
    P2d --> P3[3 character]
    P3 --> P4[4 environment + props]
    P4 --> P5[5 PBR + lighting]
    P5 --> P6[6 physics + picking]
    P6 --> P7[7 walk around]
    P7 --> P8[8 zones]
    P8 --> P9[9 cue engine]
    P8 --> P10[10 story primitives]
    P9 --> P10
    P10 --> P11[11 scenario editor]
    P8 --> P12[12 zone registry]
    P11 --> P13[13 systems + fixed step]
    P12 --> P13
    P13 --> P14[14 advanced rendering]
    P13 --> P15[15 persistence + perf]
```

The **playable-and-visible spine** is
`1 → 2a → 2b → 2c → 2d → 3 → 4 → 5 → 6 → 7` (screens → entity store → editor
workspace → selection/transform → command/undo → character → environment/props →
PBR → physics → walk around). After that, the content-authoring path is
`8 → 9 → 10 → 11 → 12 → 13`, and `14`/`15` are polish.

## F#-specific notes

- **Phase 3 is the "hard" plumbing step.** odin-game had raylib's
  `LoadModel`/`DrawMesh` to make "character on screen" trivial; SDL_gpu has no
  such helper, so phase 3 must build a minimal mesh-draw path (vertex/index
  buffers + one graphics pipeline + one shader) and a model loader. Phase 4
  reuses that path for static geometry; phase 5 upgrades its shader to PBR.
- **Model loading** — port `ocaml-game/lib/core/iqm.ml` (actors) and a scoped
  glTF parser (environments/props). The IQM parser already emits flat
  struct-of-arrays, which feeds SDL_gpu buffers directly. Use `System.IO.BinaryReader`
  for little-endian reads; keep the data as `float32`/`single`, not `float`.
- **Math** — use `System.Numerics` (`Vector3`/`Quaternion`/`Matrix4x4`) rather
  than hand-rolling. It ships in the BCL (no dependency), is SIMD-accelerated,
  and its types are value types (no GC). Watch the convention: `Matrix4x4` is
  row-major/row-vector, so keep it consistent with SDL_gpu (transpose as needed).
- **Asset manifests** — port `ocaml-game/lib/core/assets.ml` (scan
  `actors/`/`environments/`/`props/` + s-syntax manifests) as the asset manager
  (needed by phase 2b and phase 4).
- **Physics** — `JoltPhysicsSharp` behind `physics/Physics.fs`, starting with
  capsule bodies (character) + mesh shapes (props/environment), accurate raycast
  picking, and `PhysicsSystem.Update` (step).
- **PBR** — glTF materials (metallic-roughness) map directly onto SDL_gpu
  pipelines; no external shading library needed. Keep it minimal (GGX direct +
  directional light) rather than a full PBR framework.
- **Editor** — follow `ocaml-game/docs/EDITOR_DESIGN.md` and `DECISIONS.md`
  (one docking workspace, one Inspector, command/undo model, template/runtime
  cue split, "just enough" ImGui binding).

## References

- **odin-game** — `src/` (working implementation), `docs/*_PLAN.md`
  (per-feature design rationale, incl. `PBR_PLAN.md`).
- **ocaml-game** — `lib/core/` (slot map, entity, zone, cue, iqm, gltf, assets),
  `docs/{ROADMAP,DECISIONS,EDITOR_DESIGN,CUE_DESIGN,SYSTEMS_PHYSICS_DESIGN}.md`,
  `specs/` (27 completed specs as evidence).
- **fsharp-game** — `docs/ARCHITECTURE.md` (target data model),
  `docs/PROJECT_STRUCTURE.md` (layout), `docs/EDITOR_DESIGN.md` (editor),
  `AGENTS.md` (style), `ImGui.fs`/`ImGuiSdl3.fs`/`native/` (ImGui + SDL_gpu).

## Next step

**Phase 11 — scenarios own their zones**: a **scenario** is the primary unit of authoring. It is a plain **string
name** and it carries each of its zones' **staging** (environment, light and placed props) as well as their narrative
entities; a zone is addressed by a **name** (`specs/079-scenario-owned-zones`, `specs/083-scenario-strings`). The
**Scenario** panel names scenarios by hand (Add/Delete) and files the current scene, clears it and **forks** it —
ensuring each zone by name (creating and staging, or restaging a present one), appending its entities and forking its
cue — each a step on the one undo history (`specs/070`, `specs/078`, `specs/079`, `specs/083`). The Zone Registry and
the closed `Place` union are **retired**, and the scenario library is a string-keyed ordered map with no closed
vocabulary. What remains of the deferred cue families is odin's other scenario ops (`InlineScenario`/`ClearScenario`)
and the camera-follow / interaction toggles. (Phase 10 is complete; phase 12 is retired by 079.)
