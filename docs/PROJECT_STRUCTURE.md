# Project Structure

This document defines the target directory layout for `fsharp-game`. It adapts
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

1. **Organise by domain cluster, one level deep.** Seven stable clusters —
   `core`, `physics`, `render`, `world`, `state`, `screen`, `editor` — plus a
   top-level `Engine.fs` loop and a thin `Program.fs` entry point.
2. **Flat module namespace.** One top-level `module X` per file, no namespace.
   `core/SlotMap.fs` declares `module SlotMap`, exactly as if it lived at the
   root. (Rejects `namespace FSharpGame.Core` to avoid churning references.)
3. **Compile order encodes dependencies.** The `.fsproj` `<Compile Include>`
   list is ordered `core → physics → render → world → state → editor → screen →
   Engine → Program`, and within each cluster files are ordered by dependency. A
   file may only reference symbols defined in earlier files.
4. **Data beside code, at the root.** `assets/` and `shaders/` hold files the
   engine reads; they are not F# modules.
5. **Tests colocate by discipline.** One flat `test/<Module>Tests.fs` per module
   (mirroring the cluster layout is a future option if `test/` grows).

## The F# mechanism

F# has no dune/`include_subdirs`. The single source of truth for ordering is the
`<Compile Include="...">` list in `fsharp-game.fsproj`. Adding a module means
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
  <!-- world (the game domain: entities, zones, assets) -->
  <Compile Include="world/Entity.fs" />
  <Compile Include="world/Zone.fs" />
  ...
  <!-- state (app + editor-command state) -->
  <Compile Include="state/Screen.fs" />
  <Compile Include="state/Command.fs" />
  ...
  <!-- editor (consumes everything below) -->
  <Compile Include="editor/Editor.fs" />
  ...
  <!-- screen (composed last; consumes editor) -->
  <Compile Include="screen/ScreenManager.fs" />
  ...
  <!-- top level -->
  <Compile Include="Engine.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

## Target layout

```text
fsharp-game/
├── Program.fs                  # entry point: [<EntryPoint>] main → Engine.run
├── Engine.fs                   # top-level run loop (split out of Program.fs)
├── core/                       # dependency-free structural utilities
│   ├── SlotMap.fs              #   generational slot map
│   ├── Iqm.fs                  #   IQM binary parser (actors)
│   ├── Gltf.fs                 #   scoped glTF parser (environments/props)
│   └── Input.fs                #   keyboard/mouse snapshot
├── physics/                    # Jolt Physics (NuGet `JoltPhysicsSharp`)
│   └── Physics.fs              #   Jolt-backed world + a thin F# adapter
├── render/                     # generic GPU / visual modules
│   ├── Gpu.fs                  #   SDL_gpu device/swapchain/pass wrapper
│   ├── Texture.fs              #   texture / material loading
│   ├── Mesh.fs                 #   GPU buffers + draw
│   ├── StaticMesh.fs           #   unskinned geometry buffers + draw
│   ├── ImGui.fs                #   cimgui `ig*` binding
│   ├── ImGuiSdl3.fs            #   SDL3 + SDL_gpu backend shim
│   ├── Camera.fs               #   editor / follow camera
│   ├── DebugDraw.fs            #   line-segment debug rendering
│   ├── Scene.fs                #   per-screen 3D scene (planned)
│   ├── Water.fs                #   (planned)
│   ├── Ocean.fs                #   (planned)
│   ├── Grass.fs                #   (planned)
│   ├── Terrain.fs              #   (planned)
│   ├── TerrainSplat.fs         #   (planned)
│   ├── Skydome.fs              #   (planned)
│   ├── Shadow.fs               #   (planned)
│   ├── SceneTarget.fs          #   offscreen FBO (planned)
│   └── Exr.fs                  #   OpenEXR support (planned)
├── world/                      # the game domain: entities, zones, assets
│   ├── Entity.fs               #   entity kinds, EntityRef, transform, base
│   ├── Zone.fs                 #   zone + an Environment owning its parts map
│   ├── ZoneMap.fs              #   runtime zone container (planned)
│   ├── ZoneRegistry.fs         #   editor zone templates (planned)
│   ├── Assets.fs               #   asset manager + manifests
│   ├── Actor.fs                #   IQM model + morphs
│   ├── ActorAsset.fs           #   actor asset record
│   ├── ActorRender.fs          #   skinned actor rendering
│   ├── EnvironmentAsset.fs     #   static-model asset record
│   ├── EnvironmentRender.fs    #   static environment + prop rendering
│   ├── Advent.fs               #   adventure flag set (planned)
│   ├── Cast.fs                 #   name → asset id map (planned)
│   ├── Cue.fs                  #   Cue_Op (template) + CueState (runtime) (planned)
│   ├── Scenario.fs             #   scenario library (planned)
│   ├── Systems.fs              #   named play-mode systems (planned)
│   └── Project.fs              #   DTO persistence (project.json) (planned)
├── state/                      # app/screen + editor-command state
│   ├── Screen.fs               #   kind/transition/input types
│   ├── SharedState.fs
│   ├── Command.fs              #   undoable editor commands
│   ├── EditorState.fs          #   selection + transient UI state
│   └── GameplayState.fs
├── screen/                     # screens (composed last; consume editor)
│   ├── ScreenManager.fs
│   ├── MainMenuScreen.fs
│   ├── GameplayScreen.fs
│   ├── DebugOverlay.fs
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
│   ├── CastPanel.fs            #   (planned)
│   ├── AdventPanel.fs          #   (planned)
│   ├── CueEditor.fs            #   (planned)
│   ├── CueTree.fs              #   shared recursive cue-tree renderer (planned)
│   ├── ScenarioEditor.fs       #   (planned)
│   └── ZoneEditor.fs           #   (planned)
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
  debug-draw). Consumes `core/`.
- **`world/`** — the game domain: entities, zones, assets, and their domain
  renderers. Consumes `core/`, `physics/`, and `render/`.
- **`state/`** — app/screen state and the undoable editor-command model. Consumes
  `world/`.
- **`editor/`** — editor tools. Consumes everything below it.
- **`screen/`** — the screen manager and screens, composed last so a screen can
  drive the editor. Consumes `editor/`.
- **`Engine.fs`** — the top-level `run` loop; composes the rest.
- **`Program.fs`** — `[<EntryPoint>] main` only, delegates to `Engine.run`.

Dependency direction is strictly downward, matching the compile order:
`Program`/`Engine` → `screen` → `editor` → `state` → `world` → `render` →
`physics` → `core` → `SlotMap`.

## Naming

- **Files and modules are PascalCase** (`SlotMap.fs` → `module SlotMap`),
  following F# convention. (ocaml-game used `snake_case` files / `PascalCase`
  modules; F# uses PascalCase for both.)
- Each file declares exactly one top-level `module`, matching its filename.
- No namespaces — the flat module namespace matches ocaml-game's `wrapped false`.

## Migration status

The flat layout has fully migrated. Only `Engine.fs` (the run loop, split out of
`Program.fs`) and `Program.fs` (the thin `[<EntryPoint>]`) remain at the root;
`ImGui.fs`/`ImGuiSdl3.fs` live in `render/`; the game-domain modules (entities,
zones, assets, and their renderers) live in `world/`; and the app/editor state
(`Screen`, `SharedState`, `Command`, `EditorState`, `GameplayState`) lives in
`state/`.

Future moves are still applied **lazily** — when a module is next touched, not as
a single restructuring commit.

## Applying it incrementally

- **New module?** Create it directly in its target location and insert it at the
  correct point in the `.fsproj` compile list.
- **Touching an existing module that needs to move?** Move it as part of that
  change.
- **Landing a phase?** Its files go into the cluster shown above, dropping the
  `(planned)` marker from the layout as each is created.
