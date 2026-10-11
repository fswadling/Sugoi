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
   `core`, `physics`, `render`, `world`, `state`, `screen`, `editor` — plus the
   `narrative` engine below them, a top-level `Engine.fs` loop and a thin `Program.fs`
   entry point.
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
  <!-- narrative (first: the roster sits below the engine, and the engine cannot reference the game) -->
  <Compile Include="narrative/Roster.fs" />
  <Compile Include="narrative/Vocabulary.fs" />
  ...
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
  <!-- the cue is listed before Entity.fs: a trigger owns a cue (spec 058) -->
  <Compile Include="world/Cue.fs" />
  <Compile Include="world/Entity.fs" />
  <Compile Include="world/ZoneTemplate.fs" />
  <Compile Include="world/Zone.fs" />
  <Compile Include="world/ZoneRegistry.fs" />
  ...
  <!-- state (app + editor-command state) -->
  <Compile Include="state/Screen.fs" />
  <Compile Include="state/Command.fs" />
  ...
  <!-- editor (consumes everything below) -->
  <!-- CueTree/CueEditor are listed before Inspector.fs: the Inspector draws a trigger's cue inline (spec 058) -->
  <Compile Include="editor/CueTree.fs" />
  <Compile Include="editor/CueEditor.fs" />
  <Compile Include="editor/Inspector.fs" />
  <!-- MiscPanel is listed before Editor.fs: Editor.draw submits it (spec 059) -->
  <Compile Include="editor/MiscPanel.fs" />
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
├── narrative/                  # the narrative engine: events → facts → rules
│   ├── Roster.fs               #   the closed roster, characterName, characters (asset-free; spec 074)
│   ├── Vocabulary.fs           #   FactKind and the closed Event / Fact / Trait vocabulary
│   ├── Canonical.fs            #   canonical text: construct name and argument text (rendering only)
│   ├── History.fs              #   committed events, game time
│   ├── Facts.fs                #   the ordered fact map, reasons, fact sources
│   ├── Rules.fs                #   projections and derivations, and their builders
│   ├── Derive.fs               #   the fixpoint, its pass restriction and its bound
│   ├── Engine.fs               #   the public surface: commit, facts, reasons
│   ├── Harness.fs              #   scenario, run, report, assertions
│   └── Units/                  #   narrative units: one module of rules each
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
│   ├── Ui.fs                   #   the game's own screen-space 2D layer (dialogue panel)
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
│   ├── EnvironmentBatch.fs     #   per-zone static-scene instancing batch
│   ├── ZoneTemplate.fs         #   a registered zone's authored record
│   ├── Zone.fs                 #   zone + an Environment owning its parts map; owns its batch; the authored
│   │                           #     contents a delete carries, `ZoneContents` (spec 048)
│   ├── ZoneRegistry.fs         #   the game's places, and the zone registered at each
│   ├── Scenario.fs             #   the game's scenarios, a closed union (spec 070)
│   ├── ScenarioZone.fs         #   a zone as a scenario holds it: ref-free, arrays, indices (spec 070)
│   ├── ScenarioRegistry.fs     #   the contents captured under each scenario (spec 070)
│   ├── Assets.fs               #   asset manager + manifests
│   ├── Actor.fs                #   IQM model + morphs
│   ├── ActorAsset.fs           #   actor asset record
│   ├── ActorRender.fs          #   skinned actor rendering
│   ├── EnvironmentAsset.fs     #   static-model asset record
│   ├── EnvironmentRender.fs    #   static environment + prop rendering
│   ├── Advent.fs               #   the milestone union and its flag set (spec 055)
│   ├── Dialogue.fs             #   the dialogue state: Kind (speaker string) and Emotion, runtime screen state
│   ├── DialogueRender.fs       #   the dialogue view: speaker name -> actor asset -> portrait, + the panel layout
│   ├── PromptRender.fs         #   the interact prompt view + its layout (pure)
│   ├── Cue.fs                  #   the cue: one type, the vocabulary, its run and forkInto (spec 046/058)
│   ├── CueEngine.fs            #   advance / tick (spec 046; compiled after state/)
│   ├── Scenario.fs             #   the game's scenarios, a closed union (spec 070)
│   ├── Systems.fs              #   named play-mode systems
│   └── Project.fs              #   DTO persistence (project.json) (planned)
├── state/                      # app/screen + editor-command state
│   ├── Screen.fs               #   kind/transition/input types
│   ├── SharedState.fs          #   engine/any-screen state: GPU context, assets, advents
│   ├── Command.fs              #   undoable editor commands
│   ├── EditorState.fs          #   selection + transient UI state
│   ├── GameplayState.fs        #   the gameplay world: zones + zone registry + physics, and the screen's view
│   └── History.fs              #   the one undo history: apply/undo/commit + the steppers (spec 048)
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
│   ├── ZoneRegistryPanel.fs    #   the places tree: register a zone against one, instantiate one
│   ├── ScenarioPanel.fs        #   the scenarios: register the current scene under one, clear it (spec 070)
│   ├── DialoguePanel.fs        #   the dialogue authoring surface (direct write)
│   ├── AdventPanel.fs          #   the milestone checklist (spec 055), docked right; a toggle is an Advent step
│   ├── MiscPanel.fs            #   the Miscellaneous panel (spec 059), docked right: game-wide runtime tuning (the fade level)
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

- **`narrative/`** — the narrative engine: immutable committed events projected into
  facts, facts combined by rules, every conclusion carrying the rule and premises behind
  it. It is compiled **first, before `core/SlotMap.fs`**, and depends on no game
  module, which is how "the engine cannot reference the game" is a compile error rather than a
  convention (`docs/ARCHITECTURE.md`, `specs/025-narrative-core`). Its `Roster.fs` holds the closed
  `Character` roster (`specs/074-retire-cast` moved it in from the old `cast/` cluster). Nothing consumes
  it yet: the engine is complete and tested headlessly, and the integration that makes the game feed
  it committed events is separate work.
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
`physics` → `core` → `SlotMap`. One cluster sits below that chain: `narrative/`
(below `core/`, and consumed by no other cluster yet), which holds the closed `Character` roster
and consumes nothing.

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
