# Project Structure

This document defines the target directory layout for `Sugoi`. It adapts
`ocaml-game`'s structure (`ocaml-game/docs/PROJECT_STRUCTURE.md`) to F#, whose
build model differs from OCaml/dune in one important way: **files are compiled
in the order they are listed in the `.fsproj`, not discovered by directory.**

The structure is **emergent**, not a big-bang rewrite: new modules are created
directly in their final location, and existing modules move only when next
touched. odin-game's flat `src/` (~40 files, tests mixed in) was a build-system
artifact; F# has no such constraint, so we cluster by domain.

Every existing module already lives in its cluster. In the layout below, `(planned)`
marks a file that is not created yet — the marker is dropped when the file lands.

## Principles

1. **Organise by domain cluster.** Eight stable clusters — `core`, `physics`,
   `render`, `audio`, `world`, `state`, `editor`, `screen` — plus a top-level
   `Shell.fs` loop and a thin `Program.fs` entry point. A cluster may add a grouping
   subfolder when it outgrows a flat list (`world/assets/`, `world/render/`); the
   compile list, not the directory, decides order.
2. **Flat module namespace.** One top-level `module X` per file, no namespace.
   `core/SlotMap.fs` declares `module SlotMap`, exactly as if it lived at the
   root. (Rejects `namespace Sugoi.Core` to avoid churning references.)
3. **Compile order encodes dependencies.** The `.fsproj` `<Compile Include>`
   list is ordered `core → physics → render → audio → world → state → editor →
   screen → Shell → Program`, and within each cluster files are ordered by
   dependency. A file may only reference symbols defined in earlier files.
4. **Data beside code, at the root.** `assets/` and `shaders/` hold files the
   engine reads; they are not F# modules.
5. **Tests colocate by discipline.** One flat `test/<Module>Tests.fs` per module
   (mirroring the cluster layout is a future option if `test/` grows).

## The F# mechanism

F# has no dune/`include_subdirs`. The single source of truth for ordering is the
`<Compile Include="...">` list in `Sugoi.fsproj`. Adding a module means
inserting its file at the correct point in that list, not creating a directory.

The `.fsproj` compile list is ordered like this (abridged):

```xml
<ItemGroup>
  <!-- core (structural, no dependencies above it) -->
  <Compile Include="core/SlotMap.fs" />
  <Compile Include="core/Iqm.fs" />
  <Compile Include="core/Gltf.fs" />
  <Compile Include="core/Input.fs" />
  <!-- physics -->
  <Compile Include="physics/Physics.fs" />
  <!-- render (consumes core) -->
  <Compile Include="render/Gpu.fs" />
  <Compile Include="render/Mesh.fs" />
  <Compile Include="render/Camera.fs" />
  ...
  <!-- audio (device/CPU, no GPU): consumed by world/, so it sits below it -->
  <Compile Include="audio/AudioAsset.fs" />
  <Compile Include="audio/Audio.fs" />
  <!-- Music is below Audio (its deck handles) and above world/ (state/ holds the mix) (spec 087) -->
  <Compile Include="audio/Music.fs" />
  <!-- Sound is below Music (its track handles) and above world/ (state/ holds the pool) (spec 089) -->
  <Compile Include="audio/Sound.fs" />
  <!-- world (the game domain: entities, zones, assets) -->
  <!-- the cue is listed before Entity.fs: a trigger owns a cue (spec 058) -->
  <Compile Include="world/Cue.fs" />
  <Compile Include="world/Entity.fs" />
  <Compile Include="world/ZoneTemplate.fs" />
  <Compile Include="world/Zone.fs" />
  <Compile Include="world/ScenarioZone.fs" />
  <Compile Include="world/ScenarioRegistry.fs" />
  <!-- Project.fs files the scenario library as s-syntax; it names every world/ type above it and nothing below (spec 095) -->
  <Compile Include="world/Project.fs" />
  <Compile Include="world/Systems.fs" />
  <!-- state (app/screen state, the command model, and the engines over the gameplay state) -->
  <Compile Include="state/Screen.fs" />
  <Compile Include="state/Command.fs" />
  ...
  <Compile Include="state/GameplayState.fs" />
  <!-- Undo is listed right after the gameplay state: a zone step adds, removes or switches
       a zone, so the runners need it — and nothing below it (spec 048) -->
  <Compile Include="state/Undo.fs" />
  <!-- ScenarioFork and CueEngine live in state/ because their effects land on the
       gameplay state (specs 078, 046) -->
  <Compile Include="state/ScenarioFork.fs" />
  <Compile Include="state/CueEngine.fs" />
  <!-- editor (consumes everything below) -->
  <!-- CueTree/CueEditor are listed before Inspector.fs: the Inspector draws a trigger's cue inline (spec 058) -->
  <Compile Include="editor/CueTree.fs" />
  <Compile Include="editor/CueEditor.fs" />
  <Compile Include="editor/Inspector.fs" />
  <!-- MiscPanel is listed before Editor.fs: Editor.draw submits it (spec 059) -->
  <Compile Include="editor/MiscPanel.fs" />
  <!-- SongPanel is listed before Editor.fs: Editor.draw submits it (spec 087) -->
  <Compile Include="editor/SongPanel.fs" />
  <!-- SoundPanel is listed after SongPanel and before Editor.fs: Editor.draw submits it (spec 089) -->
  <Compile Include="editor/SoundPanel.fs" />
  <Compile Include="editor/Editor.fs" />
  ...
  <!-- screen (composed last; consumes editor) -->
  <Compile Include="screen/ScreenManager.fs" />
  ...
  <!-- top level -->
  <Compile Include="Shell.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

## Target layout

```text
Sugoi/
├── Program.fs                  # entry point: [<EntryPoint>] main → Shell.run
├── Shell.fs                    # app shell: SDL3 + ImGui bring-up, the frame loop, teardown
├── core/                       # dependency-free structural utilities
│   ├── SlotMap.fs              #   generational slot map
│   ├── Diagnostics.fs          #   one-line reporting + the error tally (spec 081)
│   ├── Lru.fs                  #   a bounded least-recently-used cache
│   ├── Iqm.fs                  #   IQM binary parser (actors)
│   ├── Gltf.fs                 #   scoped glTF parser (environments/props)
│   ├── Input.fs                #   keyboard/mouse snapshot
│   └── Maths.fs                #   spline geometry: curves through points, turns through rotations (spec 054)
├── physics/                    # Jolt Physics (NuGet `JoltPhysicsSharp`)
│   └── Physics.fs              #   Jolt-backed world + a thin F# adapter
├── render/                     # generic GPU / visual modules
│   ├── Gpu.fs                  #   SDL_gpu device/swapchain/pass wrapper
│   ├── Texture.fs              #   texture / material loading
│   ├── Mesh.fs                 #   GPU buffers + draw
│   ├── StaticMesh.fs           #   unskinned geometry buffers + draw
│   ├── InstanceBuffer.fs       #   instance-data buffer for batched draws
│   ├── ImGui.fs                #   cimgui `ig*` binding
│   ├── ImGuiSdl3.fs            #   SDL3 + SDL_gpu backend shim
│   ├── Camera.fs               #   editor / follow camera
│   ├── DebugDraw.fs            #   line-segment debug rendering
│   ├── Ui.fs                   #   the game's own screen-space 2D layer (dialogue panel)
│   ├── DebugOverlay.fs         #   the on-screen debug readout: fps + the error tally (spec 081)
│   ├── Scene.fs                #   per-screen 3D scene (planned)
│   ├── Water.fs                #   (planned)
│   ├── Ocean.fs                #   (planned)
│   ├── Grass.fs                #   (planned)
│   ├── Terrain.fs              #   (planned)
│   ├── TerrainSplat.fs         #   (planned)
│   ├── Skydome.fs              #   the zone's sky: a camera-centred dome drawn behind the scene (spec 092)
│   ├── Shadow.fs               #   (planned)
│   ├── SceneTarget.fs          #   offscreen FBO (planned)
│   └── Exr.fs                  #   OpenEXR support (planned)
├── audio/                      # device/CPU audio, no GPU: the mixer seam, the audio asset model + the mix
│   ├── AudioAsset.fs           #   the audio asset: class (music/sound), residency, sound handle (spec 086)
│   ├── Audio.fs                #   the mixer context: load a sound / probe a track, the deck ops + its lifecycle (spec 086/087)
│   ├── Music.fs                #   the mix model (rows/weights/loop/master) + its pure edits + the deck rack (spec 087)
│   └── Sound.fs                #   the one-shot voice pool (fixed tracks reused round-robin) + play/stop/release (spec 089)
├── world/                      # the game domain: entities, zones, assets, and their renderers
│   ├── Entity.fs               #   entity kinds, EntityRef, transform, base
│   ├── ZoneTemplate.fs         #   a registered zone's authored record
│   ├── Zone.fs                 #   zone + an Environment owning its parts map; owns its batch; the authored
│   │                           #     contents a delete carries, `ZoneContents` (spec 048)
│   ├── ScenarioZone.fs         #   a zone as a scenario holds it: staging + narrative entities, ref-free, chains by index (specs 070, 079)
│   ├── ScenarioRegistry.fs     #   the scenario library: an ordered map keyed by name (spec 070/083)
│   ├── Dialogue.fs             #   the dialogue state: Kind (speaker string) and Emotion, runtime screen state
│   ├── Actor.fs                #   IQM model + morphs
│   ├── Sway.fs                 #   bone-chain sway: a Verlet rope anchored to the animated pose (spec 091)
│   ├── Cue.fs                  #   the cue: one type, the vocabulary, its run and forkInto (spec 046/058)
│   ├── Systems.fs              #   named play-mode systems
│   ├── assets/                 #   the asset records, the async load substrate, and the manager
│   │   ├── AssetLoad.fs        #     the decoded payload, the bounded recency queue, the worker pool (spec 053)
│   │   ├── Assets.fs           #     asset manager + manifests
│   │   ├── ActorAsset.fs       #     actor asset record
│   │   └── EnvironmentAsset.fs #     static-model asset record
│   ├── render/                 #   the domain renderers and the 2D views
│   │   ├── ActorRender.fs      #     skinned actor rendering
│   │   ├── EnvironmentRender.fs#     static environment + prop rendering
│   │   ├── EnvironmentBatch.fs #     per-zone static-scene instancing batch
│   │   ├── DialogueRender.fs   #     the dialogue view: speaker name -> actor asset -> portrait, + the panel layout
│   │   └── PromptRender.fs     #     the interact prompt view + its layout (pure)
│   └── Project.fs              #   the project file: the scenario library as s-syntax (`project.manifest`) at the launch working directory (spec 095)
├── state/                      # app/screen state, the editor-command model, and the engines over the gameplay state
│   ├── Screen.fs               #   kind/transition/input types
│   ├── SharedState.fs          #   engine/any-screen state: GPU context, assets, advents
│   ├── Command.fs              #   undoable editor commands (+ the UndoStack)
│   ├── EditorState.fs          #   selection + transient UI state
│   ├── GameplayState.fs        #   the gameplay world: zones + the scenario library + physics, and the screen's view
│   ├── Undo.fs                 #   the one undo history: apply/undo/commit + the steppers (spec 048)
│   ├── ScenarioFork.fs         #   the scenario materialiser both entry points call (spec 078)
│   └── CueEngine.fs            #   the cue engine: advance / tick (spec 046)
├── screen/                     # screens (composed last; consume editor)
│   ├── ScreenManager.fs
│   ├── MainMenuScreen.fs
│   ├── GameplayScreen.fs
│   └── SettingsScreen.fs       #   (planned)
├── editor/                     # editor (consumes everything below)
│   ├── Editor.fs               #   orchestration: docking, shortcuts
│   ├── Spawning.fs             #   single entity-creation surface (undo-paired)
│   ├── Selection.fs            #   selection edits (delete / duplicate)
│   ├── Raycast.fs              #   screen-ray picking math
│   ├── Viewport.fs             #   3D viewport + gizmos
│   ├── Outliner.fs
│   ├── Inspector.fs            #   single editing surface for the selection
│   ├── AssetBrowser.fs
│   ├── ScenarioPanel.fs        #   the scenarios: a name field + Add, and register/clear/fork/delete per row (spec 070/083)
│   ├── DialoguePanel.fs        #   the dialogue authoring surface (direct write)
│   ├── AdventPanel.fs          #   the milestone list editor (spec 055/082), docked right: a name + Add, and a Remove per row; one Advent step each
│   ├── MiscPanel.fs            #   the Miscellaneous panel (spec 059), docked right: game-wide runtime tuning (the fade level)
│   ├── SongPanel.fs            #   the Songs panel (spec 087), docked bottom: add music assets to a global mix, a row each with transport/scrub/weight/loop over one master; a SetMix step each
│   ├── SoundPanel.fs           #   the Sounds panel (spec 089), docked bottom: the discovered sounds, a Play/Stop row each + Stop all; a runtime audition surface (no history)
│   ├── CueEditor.fs            #   the cue panel (spec 047) + the inline trigger-cue editor (spec 058); a SetCue/TriggerSet step on the one history
│   └── CueTree.fs              #   shared recursive cue-tree control (spec 047)
├── native/                     # native libs (ImGui + cimgui + backends)
├── test/                       # one <Module>Tests.fs per module (flat)
├── assets/                     # data: glTF, IQM, textures, manifests
├── shaders/                    # data: SPIR-V / GLSL
├── docs/
└── specs/
```

## Clusters

- **`core/`** — dependency-free structural utilities: the slot map, the IQM/glTF
  parsers, and the input snapshot. Everything may depend on it, and it depends on
  nothing above it. This is the "structural core" from `docs/ARCHITECTURE.md`.
- **`physics/`** — the Jolt-backed world wrapped behind a thin F# adapter over
  the `JoltPhysicsSharp` NuGet package. The per-platform native `joltc` library
  (`libjoltc.so`/`libjoltc.dylib`/`joltc.dll`) ships in the `JoltPhysics.Native`
  package and is copied into `runtimes/<rid>/native/` automatically.
- **`render/`** — generic GPU/visual modules (device, buffers, camera, ImGui,
  debug-draw, the debug overlay). Consumes `core/`.
- **`audio/`** — device/CPU audio, no GPU: the `Audio.Context` mixer seam (bring up SDL audio +
  SDL_mixer, load a sound into memory, probe a track, and the playback deck ops — open/play/seek/gain/loop),
  the `AudioAsset` model (class, residency, `SoundHandle`), `Music` (the authored mix + its pure edits
  and the runtime deck rack), and `Sound` (the one-shot voice pool + its play/stop/release ops). Consumes
  `core/` and SDL3; consumed by `world/` (`Assets.scan`) and `state/` (the mix and the pool on
  `SharedState`). Compiled after `render/` and before `world/` (spec 086/087/089).
- **`world/`** — the game domain: entities, zones, the cue, the scenario library
  and the play-mode systems at the root; `world/assets/` the asset records, the
  async load substrate and the manager; `world/render/` the domain renderers and
  the 2D views. Consumes `core/`, `physics/`, `render/`, and `audio/`.
- **`state/`** — app/screen state, the undoable editor-command model, the one
  undo history (`Undo`), and the engines whose effects land on the gameplay
  state (`ScenarioFork`, `CueEngine`). Consumes `world/`.
- **`editor/`** — editor tools. Consumes everything below it.
- **`screen/`** — the screen manager and screens, composed last so a screen can
  drive the editor. Consumes `editor/`.
- **`Shell.fs`** — the app shell: SDL3 + ImGui bring-up, the top-level `run`
  loop, and teardown; composes the rest.
- **`Program.fs`** — `[<EntryPoint>] main` only, delegates to `Shell.run`.

Dependency direction is strictly downward, matching the compile order:
`Program`/`Shell` → `screen` → `editor` → `state` → `world` → `render` →
`physics` → `core` → `SlotMap`. `audio/` sits beside `render/` (both consumed by `world/`):
`world` → `audio` → `core`.

## Naming

- **Files and modules are PascalCase** (`SlotMap.fs` → `module SlotMap`),
  following F# convention. (ocaml-game used `snake_case` files / `PascalCase`
  modules; F# uses PascalCase for both.)
- Each file declares exactly one top-level `module`, matching its filename.
- No namespaces — the flat module namespace matches ocaml-game's `wrapped false`.

## Migration status

The flat layout has fully migrated. Only `Shell.fs` (the app shell and run loop,
split out of `Program.fs`) and `Program.fs` (the thin `[<EntryPoint>]`) remain at
the root; `ImGui.fs`/`ImGuiSdl3.fs` live in `render/`; the game-domain modules
(entities, zones, the cue and the systems, with `assets/` and `render/`
subfolders) live in `world/`; and the app/editor state (`Screen`, `SharedState`,
`Command`, `EditorState`, `GameplayState`) plus its engines (`Undo`,
`ScenarioFork`, `CueEngine`) live in `state/`.

Future moves are still applied **lazily** — when a module is next touched, not as
a single restructuring commit.

## Applying it incrementally

- **New module?** Create it directly in its target location and insert it at the
  correct point in the `.fsproj` compile list.
- **Touching an existing module that needs to move?** Move it as part of that
  change.
- **Landing a phase?** Its files go into the cluster shown above, dropping the
  `(planned)` marker from the layout as each is created.
