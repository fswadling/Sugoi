# Sugoi

Sugoi is a data-oriented 3D narrative game and in-engine editor written in F#. It runs on
SDL3's GPU API with Dear ImGui for its tools, and Jolt Physics for the world. Its architecture
follows the `odin-game` design: generational slot maps, structs-of-arrays, and plain procedures
instead of an ECS.

The game is authored entirely inside the application: place zones, environments, props, actors,
waypoints, and triggers; wire triggers to cue scripts; assemble scenarios from the current scene;
and toggle between editing and playing with one key.

## What is in the box

- **Playable 3D world** — zones with a glTF environment, props, IQM actors with animation,
  morph targets, bone sway, and cel shading; Jolt-backed actors and props; a third-person
  player.
- **Narrative tooling** — dialogue with portrait expressions, adventure flags, waypoint
  chains, interactable triggers, an immutable cue language (sequence/parallel/fork), and
  named scenarios.
- **In-engine editor** — a dockable ImGui workspace with a viewport, outliner, inspector,
  asset browser, and dedicated panels for cues, dialogue, scenarios, audio, and runtime
  tuning. Every scene edit is an undoable step.
- **Automatic persistence** — the scenario library is written to `project.manifest` in the
  launch working directory as readable s-syntax, and external edits can be picked up live
  while editing.
- **Audio** — SDL_mixer-backed streaming music decks and a one-shot sound pool.

See `docs/ROADMAP.md` for implemented milestones and `docs/ARCHITECTURE.md` for the data model.

## Requirements

Sugoi currently targets **Linux x64** (the checked-in native libraries and build script are
Linux `.so` files).

- **.NET 10 SDK**
- **SDL3 runtime** (`libSDL3.so.0`) available to the dynamic linker. The `SDL3-CS` NuGet
  package supplies the managed bindings, not the native library.
- **A GPU/driver with a supported SDL_gpu backend** (Vulkan on Linux). The compiled SPIR-V
  shaders are committed, so no shader toolchain is needed to run.
- **`libcimgui.so`**, built locally with `native/build.sh` (not committed).

Building `libcimgui.so` additionally needs:

- `git` and network access (the script shallow-clones cimgui on first run)
- a C++17 compiler (`g++` or `clang++`)
- SDL3 development headers (e.g. `/usr/include/SDL3/SDL.h`)

The SDL3_image, SDL3_ttf, and SDL3_mixer native libraries (plus `libjbig.so.0`) are vendored
in `native/` and copied next to the app at build time. The default text font is committed at
`assets/fonts/DejaVuSans.ttf`.

## Build

From the repository root:

```sh
# One-time: build the native Dear ImGui + SDL3/SDL_gpu backend library.
./native/build.sh

# Build the F# application.
dotnet build
```

## Run

```sh
dotnet run
```

Run from the repository root. The application resolves `assets/`, `shaders/`, and
`project.manifest` relative to the current working directory.

The main menu offers **Play** (enter the editor workspace) and **Quit**.

If audio hardware is unavailable, the game reports it and continues without audio. If the font
cannot be opened, text is disabled but the frame still renders.

## Controls

### Editor mode

The gameplay screen opens in editor mode; a backtick (`` ` ``) toggles between edit and play.

| Input | Action |
|---|---|
| Left-click | Select an entity (raycast); drag a gizmo handle to transform it |
| Right-drag | Orbit/look the camera |
| Right-drag + `W`/`A`/`S`/`D` | Fly the camera |
| Right-drag + `Q`/`E` | Fly down/up |
| `1` / `2` / `3` | Move / rotate / scale tool |
| `Q` | Toggle snapping |
| `Shift` + `W`/`A`/`S`/`D`/`Q`/`E` | Nudge the selection (camera-relative) |
| `F` | Focus the camera on the selection |
| `Del` | Delete the selection |
| `Ctrl+D` | Duplicate the selection |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `Ctrl+Shift+C` | Clear the world (one undoable step) |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Cycle focus through docked panels |
| `Escape` | Return to the main menu |
| Backtick (`` ` ``) | Enter play mode |

There is no scroll zoom for the editor camera; the wheel belongs to the panels, so the camera
is fly-only.

### Play mode

| Input | Action |
|---|---|
| `W` / `S` | Walk forward / backward |
| `A` / `D` | Turn left / right |
| `E` | Interact with the trigger the player is inside |
| `Space` | Advance the shown dialogue line |
| Backtick (`` ` ``) | Return to editor mode |

Escape returns to the menu only from the editor, so press the backtick key first when playing.

## Authoring workflow

1. **Open the Outliner** to manage zones and entities. Add a zone by typing a name and
   pressing **Add**; click a zone to make it active. The active zone starts empty.
2. **Open the Asset Browser** to populate the active zone. Click an actor or prop to spawn it
   at the selected spawn anchor; click an environment to stage it in the zone's single
   environment slot.
3. **Select an actor and press "Set as Player"** in the Inspector to make her the playable
   character; her physics mode, mass, friction, restitution, and gravity factor are edited in
   the same panel.
4. **Add waypoints and triggers** from the Outliner, then edit their chains, volumes, prompts,
   and trigger cues in the Inspector.
5. **Author narrative content** in the side panels: a dialogue line in **Dialogue**, story
   flags in **Advent**, and a cue tree in **Cue**.
6. **Toggle play mode** with the backtick key and walk around the scene. Cue scripts advance on play
   time and freeze while editing.
7. **File the scene as a scenario** in the **Scenario** panel: name it, press **Add**, then
   **Register** to capture every present zone and the current cue. **Fork** later materialises
   it back into the scene as one undoable step; **Clear** empties it; **Delete** removes it.

### The workspace

| Panel | Dock | Purpose |
|---|---|---|
| **Viewport** | center | The 3D scene, selection, gizmos, and waypoint/trigger overlays |
| **Outliner** | left | Zones; the active zone's actors, props, environment parts, waypoints, and triggers |
| **Scenario** | left | Name, register, fork, clear, and delete scenarios |
| **Inspector** | right | The selected item's properties; with nothing selected, the active zone's environment, sun, and skydome |
| **Cue** | right | The screen's cue tree, editable ahead of the executing step |
| **Advent** | right | Named adventure flags (story milestones) |
| **Miscellaneous** | right | Screen fade level and live reload |
| **Asset Browser** | bottom | Actors, environments, and props, with a shared spawn anchor |
| **Dialogue** | bottom | The dialogue line currently put on screen |
| **Songs** | bottom | The global music bed: per-track transport, scrub, weight, and loop |
| **Sounds** | bottom | Audition the discovered sound effects |

Actors, props, environments, audio, and dialogue portraits are discovered from s-syntax
manifests under `assets/`. The manifest formats are documented in `assets/README.md`.

## Persistence and live reload

The authored scenario library is stored as s-syntax in `project.manifest` in the launch
working directory. It is read once at startup and written automatically whenever the library
changes (including undo/redo). Writes are atomic (`.tmp` + rename), and a damaged or missing
file leaves the running library alone.

With **Live reload** enabled in the **Miscellaneous** panel (the default), an external edit to
`project.manifest` is picked up while editing without restarting. Play mode performs no file
I/O.

## Project layout

```text
core/       Slot map, IQM/glTF parsers, maths, input, diagnostics, LRU cache
physics/    Jolt Physics world and F# adapter
render/     SDL_gpu device, meshes, textures, camera, ImGui, 2D UI, skydome, debug draw
audio/      Mixer seam, audio assets, music mix/decks, one-shot sound pool
world/      Entities, zones, actors, assets and loading, renderers, cue, scenarios, persistence
state/      Screen state, commands, the one undo history, scenario fork, cue engine
editor/     Editor orchestration and every dockable panel
screen/     Main menu and gameplay screens, screen manager
assets/     Sample content: Akane, the ArtRoom environment, props, music, sounds, font
shaders/    GLSL sources and committed SPIR-V
native/     Dear ImGui C shim + vendored SDL3_* libraries
docs/       Architecture, roadmap, editor design, rendering and performance notes
specs/      Spec-driven development feature records
test/       xUnit test suite
```

New asset payloads are filtered by `assets/.gitignore`; the committed sample payloads stay
tracked. `assets/README.md` explains manifests and the portrait contract.

## Development

```sh
# Build
dotnet build

# Run the test suite
dotnet test test/Sugoi.Tests.fsproj
```

This project uses Spec Kit ("speckit"). Features are specified under `specs/` and move through
specify → plan → tasks → implement → converge. The guiding documents are:

- `docs/ARCHITECTURE.md` — target data model and port decisions
- `docs/PROJECT_STRUCTURE.md` — directory layout and compile order
- `docs/ROADMAP.md` — phased plan and current state
- `docs/EDITOR_DESIGN.md` — workspace, interaction model, and undo model

Before restructuring the project or adding modules, check the compile order in
`Sugoi.fsproj`: F# compiles files in list order, and a file may only reference symbols
defined above it.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `DllNotFoundException: cimgui` | Build the native shim with `./native/build.sh` |
| `SDL_Init failed` / no window | Install the SDL3 runtime and check that a display/GPU driver is available |
| No assets appear | Run from the repository root so `assets/` resolves |
| No audio | Expected on machines without an audio device; the game continues |
| Text does not render | Verify `assets/fonts/DejaVuSans.ttf` exists; the rest of the frame still works |
