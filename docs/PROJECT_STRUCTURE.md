# Project Structure

This document defines the target directory layout for `fsharp-game`. It adapts
`ocaml-game`'s structure (`ocaml-game/docs/PROJECT_STRUCTURE.md`) to F#, whose
build model differs from OCaml/dune in one important way: **files are compiled
in the order they are listed in the `.fsproj`, not discovered by directory.**

The structure is **emergent**, not a big-bang rewrite: new modules are created
directly in their final location, and existing modules move only when next
touched. odin-game's flat `src/` (~40 files, tests mixed in) was a build-system
artifact; F# has no such constraint, so we cluster by domain.

## Principles

1. **Organise by domain cluster, one level deep.** Four stable clusters —
   `core`, `render`, `screen`, `editor` — plus a top-level `Engine.fs` loop and a
   thin `Program.fs` entry point.
2. **Flat module namespace.** One top-level `module X` per file, no namespace.
   `core/SlotMap.fs` declares `module SlotMap`, exactly as if it lived at the
   root. (Rejects `namespace FSharpGame.Core` to avoid churning references.)
3. **Compile order encodes dependencies.** The `.fsproj` `<Compile Include>`
   list is ordered `core → render → screen → editor → Engine → Program`, and
   within each cluster files are ordered by dependency. A file may only
   reference symbols defined in earlier files.
4. **Data beside code, at the root.** `assets/` and `shaders/` hold files the
   engine reads; they are not F# modules.
5. **Tests colocate by discipline.** One flat `test/<Module>Tests.fs` per module
   (mirroring the cluster layout is a future option if `test/` grows).

## The F# mechanism

F# has no dune/`include_subdirs`. The single source of truth for ordering is the
`<Compile Include="...">` list in `fsharp-game.fsproj`. Adding a module means
inserting its file at the correct point in that list, not creating a directory.

The `.fsproj` will grow to something like:

```xml
<ItemGroup>
  <!-- core (structural, no dependencies above it) -->
  <Compile Include="core/SlotMap.fs" />
  <Compile Include="core/Entity.fs" />
  <Compile Include="core/Zone.fs" />
  ...
  <!-- render (consumes core) -->
  <Compile Include="render/Gpu.fs" />
  <Compile Include="render/Mesh.fs" />
  <Compile Include="render/Camera.fs" />
  ...
  <!-- screen (consumes core + render) -->
  <Compile Include="screen/Screen.fs" />
  <Compile Include="screen/ScreenManager.fs" />
  ...
  <!-- editor (consumes everything) -->
  <Compile Include="editor/Editor.fs" />
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
├── Engine.fs                   # top-level run loop (extracted from Program.fs)
├── core/                       # structural core: data + systems
│   ├── SlotMap.fs              #   generational slot map
│   ├── Entity.fs               #   entity kinds, Entity_Ref, transform, base
│   ├── Zone.fs                 #   zone + Environment_Entity + props
│   ├── ZoneMap.fs              #   runtime zone container
│   ├── Iqm.fs                  #   IQM binary parser (actors)
│   ├── Gltf.fs                 #   scoped glTF parser (environments/props)
│   ├── Assets.fs               #   asset manager + manifests
│   ├── Actor.fs                #   IQM model + morphs
│   ├── Advent.fs               #   adventure flag set
│   ├── Cast.fs                 #   name → asset id map
│   ├── Cue.fs                  #   Cue_Op (template) + CueState (runtime)
│   ├── Scenario.fs             #   scenario library
│   ├── ZoneRegistry.fs         #   editor zone templates
│   ├── Systems.fs              #   named play-mode systems
│   └── Project.fs              #   DTO persistence (project.json)
├── render/                     # GPU / visual modules (consume core)
│   ├── Gpu.fs                  #   SDL_gpu device/swapchain/pass wrapper
│   ├── Mesh.fs                 #   GPU buffers + draw
│   ├── Camera.fs               #   editor / follow camera
│   ├── ImGui.fs                #   cimgui `ig*` binding (moved from root)
│   ├── ImGuiSdl3.fs            #   SDL3 + SDL_gpu backend shim (moved from root)
│   ├── DebugDraw.fs            #   line-segment debug rendering
│   ├── Scene.fs                #   per-screen 3D scene
│   ├── ActorRender.fs          #   skinned actor rendering
│   ├── EnvironmentRender.fs
│   ├── Water.fs
│   ├── Ocean.fs
│   ├── Grass.fs
│   ├── Terrain.fs
│   ├── TerrainSplat.fs
│   ├── Skydome.fs
│   ├── Shadow.fs
│   ├── SceneTarget.fs          #   offscreen FBO
│   └── Exr.fs                  #   OpenEXR support
├── screen/                     # screens + shared state (consume core + render)
│   ├── Screen.fs               #   kind/transition/input types
│   ├── ScreenManager.fs
│   ├── SharedState.fs
│   ├── MainMenuScreen.fs
│   ├── GameplayScreen.fs
│   ├── GameplayState.fs
│   └── SettingsScreen.fs
├── editor/                     # editor (consumes everything)
│   ├── Editor.fs               #   orchestration: docking, shortcuts
│   ├── EditorState.fs          #   selection + transient UI state
│   ├── Outliner.fs
│   ├── Inspector.fs            #   single editing surface for the selection
│   ├── Viewport.fs             #   3D viewport + gizmos
│   ├── CastPanel.fs
│   ├── AdventPanel.fs
│   ├── AssetBrowser.fs
│   ├── CueEditor.fs
│   ├── CueTree.fs              #   shared recursive cue-tree renderer
│   ├── ScenarioEditor.fs
│   ├── ZoneEditor.fs
│   ├── Command.fs              #   undoable editor commands
│   └── Raycast.fs              #   screen-ray picking math
├── physics/                    # Box3D (NuGet `Box3D`, not vendored C)
│   └── Physics.fs              #   Box3D-backed world + a thin F# adapter
├── native/                     # native libs (ImGui + cimgui + backends)
├── test/                       # one <Module>Tests.fs per module (flat)
├── assets/                     # data: glTF, IQM, textures, manifests
├── shaders/                    # data: SPIR-V / GLSL
├── docs/
└── specs/
```

## Clusters

- **`core/`** — the data model and systems. Everything depends on it, and it
  depends on nothing above it. This is the "structural core" from
  `docs/ARCHITECTURE.md`.
- **`render/`** — GPU/visual modules. Consumes `core/`.
- **`screen/`** — the screen manager and screens. Consumes `core/` and
  `render/`.
- **`editor/`** — editor tools. Consumes everything.
- **`Engine.fs`** — the top-level `run` loop; composes the rest.
- **`Program.fs`** — `[<EntryPoint>] main` only, delegates to `Engine.run`.
- **`physics/`** — the Box3D-backed world wrapped behind a thin F# adapter over
  the `Box3D` (Happypig375) NuGet package. No vendored C source (the package
  ships the native lib), unlike ocaml-game which vendored it.

Dependency direction is strictly downward: `Program`/`editor` → `screen` →
`render` → `core` → `SlotMap`.

## Naming

- **Files and modules are PascalCase** (`SlotMap.fs` → `module SlotMap`),
  following F# convention. (ocaml-game used `snake_case` files / `PascalCase`
  modules; F# uses PascalCase for both.)
- Each file declares exactly one top-level `module`, matching its filename.
- No namespaces — the flat module namespace matches ocaml-game's `wrapped false`.

## Existing files → target

| Current path | Target path | Notes |
|---|---|---|
| `Program.fs` | `Program.fs` + new `Engine.fs` | split: `Engine.fs` gets the loop; `Program.fs` is a thin `main` |
| `ImGui.fs` | `render/ImGui.fs` | move (module name unchanged: `ImGui`) |
| `ImGuiSdl3.fs` | `render/ImGuiSdl3.fs` | move (module name unchanged) |
| `native/` | `native/` | unchanged |

Moves are applied **lazily** — when a module is next touched, not as a single
restructuring commit.

## Applying it incrementally

- **New module?** Create it directly in its target location and insert it at the
  correct point in the `.fsproj` compile list.
- **Touching an existing module that needs to move?** Move it as part of that
  change.
- **Landing a phase?** Its files go into the cluster shown above (e.g. phase 1's
  `Iqm.fs` → `core/`, `Gpu.fs`/`Camera.fs`/`Mesh.fs` → `render/`, and the
  `Engine.fs`/`Program.fs` split happens here).

Phase 1 (animated character on screen) is the first structural milestone: it
lands `core/Iqm.fs`, `render/{Gpu,Camera,Mesh}.fs`, splits `Engine.fs` out of
`Program.fs`, and moves `ImGui.fs`/`ImGuiSdl3.fs` into `render/`.
