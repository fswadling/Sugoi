# Feature Specification: Project Structure Consolidation

**Feature Branch**: `094-structure-consolidation` (none — applied retrospectively on the working tree)

**Created**: 2026-10-16

**Status**: Implemented (retrospective — specified after the change landed; see Assumptions)

**Input**: User description: "Consolidate the project structure so physical folders match the compile-time layers. Move state/History.fs up beside the rest of state/ in the .fsproj compile order (its highest dependency is GameplayState; the 'second deliberate inversion' was vestigial). Move world/ScenarioFork.fs and world/CueEngine.fs into state/ since their effects land on the gameplay state. Move screen/DebugOverlay.fs into render/ (ImGui diagnostics chrome, not a screen). Group world/ into world/assets/ (AssetLoad, Assets, ActorAsset, EnvironmentAsset) and world/render/ (ActorRender, EnvironmentRender, EnvironmentBatch, DialogueRender, PromptRender) subfolders. Rename module History -> Undo (state/Undo.fs) and root module Engine -> Shell (Shell.fs) to remove collisions with Narrative.History/Narrative.Engine. Rename nested module Entity.Actor -> Entity.ActorEntity to stop it shadowing the global Actor module in the 32 files that open Entity. The narrative/ folder is explicitly out of scope (vestigial). Update docs/PROJECT_STRUCTURE.md and stale path references across docs/ and code comments. Build and the 666-test suite must stay green."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One clean compile-order sweep (Priority: P1)

A maintainer reading `fsharp-game.fsproj` top to bottom sees each cluster's files
in one contiguous block, in dependency order, with no cluster re-entered later.
Previously `state/` appeared in two places: `state/History.fs` was listed after
`world/ScenarioFork.fs` and `world/CueEngine.fs` under a stale "second deliberate
ordering inversion" comment, even though its highest dependency is
`state/GameplayState.fs`.

**Why this priority**: The compile list is the project's declared dependency map
(PROJECT_STRUCTURE.md principle 3). A cluster split across it makes the declared
map disagree with the real one for no reason, and every future insertion has to
re-derive which block is authoritative.

**Independent Test**: Move the one `<Compile Include="state/History.fs" />` line
up to immediately after `state/GameplayState.fs` and build: it compiles with zero
other changes, proving nothing between the two positions referenced it.

**Acceptance Scenarios**:

1. **Given** the compile list before the change, **When** `state/History.fs` is
   moved to directly after `state/GameplayState.fs`, **Then** the project builds
   unmodified and all `state/` files form one contiguous block.
2. **Given** the new order, **When** the fsproj comments are read, **Then** they
   describe the current reason for each placement (no "deliberate inversion"
   remains for the undo history).

---

### User Story 2 - Folders match compile layers (Priority: P2)

A maintainer can infer a file's position in the dependency order from its folder:
every file compiled after `state/` lives in a cluster at or above `state/`.
Previously two files physically in `world/` compiled after `state/` (the one
genuine inversion), the debug overlay lived with the screens though nothing
screen-like referenced it, and `world/` had grown to 22 flat files mixing domain
data, asset management, and domain rendering.

**Why this priority**: The folder tree is how a newcomer navigates; the compile
list is how the compiler navigates. When they disagree, both lie.

**Independent Test**: Each move is a path change in the `.fsproj` with no code
edits; the build stays green after each one. The world subfolders change no
compile order at all.

**Acceptance Scenarios**:

1. **Given** `world/ScenarioFork.fs` and `world/CueEngine.fs` compiled after
   `state/`, **When** they move to `state/`, **Then** no `world/` file is
   compiled after any `state/` file, and the build is green.
2. **Given** `screen/DebugOverlay.fs` is referenced only by the app shell,
   **When** it moves to `render/DebugOverlay.fs` (compiled in the `render/`
   block, needing only `Diagnostics` and `ImGui`), **Then** the build is green.
3. **Given** 22 flat files in `world/`, **When** the asset records/manager and
   the domain renderers/views are grouped into `world/assets/` and
   `world/render/`, **Then** the compile order is unchanged and the build is
   green.

---

### User Story 3 - One name, one module (Priority: P3)

A maintainer grepping for a module name finds exactly one module that can mean
it. Previously `History` existed as both `Narrative.History` (event log) and the
global undo-history module; `Engine` as both `Narrative.Engine` and the root run
loop; and the nested `Entity.Actor` companion module shadowed the global `Actor`
module in every file that `open Entity` (32 files).

**Why this priority**: These compiled safely (the Narrative modules are
namespaced), so the cost is reader confusion and a latent wrong-module trap,
not breakage — real, but below the structural lies of P1/P2.

**Independent Test**: Each rename is a mechanical module/file rename plus call
sites; the compiler enumerates any missed reference as an error.

**Acceptance Scenarios**:

1. **Given** the undo history module `History`, **When** it is renamed to
   `Undo` (file `state/Undo.fs`, all editor/test call sites updated), **Then**
   `History.` in game code has one possible referent and the build is green.
2. **Given** the root run-loop module `Engine`, **When** it is renamed to
   `Shell` (file `Shell.fs`, `Program.fs` calls `Shell.run`), **Then** no
   global module shares a name with a `Narrative.*` module.
3. **Given** `Entity.Actor`, **When** it is renamed to `Entity.ActorEntity` and
   its two call sites in `world/Zone.fs` are updated, **Then** files that
   `open Entity` cannot silently resolve bare `Actor.` to the wrong module.
4. **Given** the rename, **When** `Entity.Prop` is reviewed, **Then** it stays:
   no global `Prop` module exists for it to collide with.

---

### Edge Cases

- A rename misses a call site: impossible to miss silently — F# compiles in
  list order, so a missed reference is a compile error, and both projects
  (game + tests) are built.
- A test file that `open Narrative` also writes `History.`: checked — no game
  or test file both opens `Narrative` and references the undo history, so the
  rename cannot hijack a `Narrative.History` reference.
- A comment or doc still points at an old path: swept (code comments, fsproj
  comments, `docs/`); `specs/` is a historical record and deliberately left.
- `narrative/` shares module names with the game (`History`, `Engine`): the
  game-side modules are the ones renamed; `narrative/` is untouched.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every cluster's files MUST form one contiguous block in the
  `.fsproj` compile list, ordered by dependency.
- **FR-002**: No file physically in `world/` MUST be compiled after any file in
  `state/`; the scenario fork and cue engine, whose effects land on the gameplay
  state, MUST live in `state/`.
- **FR-003**: The on-screen debug readout MUST live in `render/` with the other
  technology-only debug visuals, not in `screen/`.
- **FR-004**: `world/` MUST group its asset records, async load substrate and
  asset manager under `world/assets/`, and its domain renderers and 2D views
  under `world/render/`, leaving domain data and play-mode systems at the root.
- **FR-005**: The undo-history module MUST be named `Undo` and its file
  `state/Undo.fs`; the app-shell module MUST be named `Shell` and its file
  `Shell.fs`; no global module MUST share a name with a `Narrative.*` module.
- **FR-006**: The nested companion module for the actor entity MUST NOT share
  the global `Actor` module's name.
- **FR-007**: `narrative/` MUST NOT be modified (vestigial subsystem, retained
  as-is).
- **FR-008**: `docs/PROJECT_STRUCTURE.md` MUST describe the resulting layout,
  cluster definitions and compile-order principles; stale path references in
  code comments, fsproj comments and `docs/` MUST be updated.
- **FR-009**: The full test suite (666 tests) MUST pass and the build MUST
  produce zero warnings after the restructuring.

### Key Entities

- **Cluster**: A top-level folder naming a dependency layer (`core`, `physics`,
  `render`, `audio`, `world`, `state`, `editor`, `screen`); a file may only
  reference earlier files in the compile list.
- **Compile list**: The ordered `<Compile Include>` sequence in
  `fsharp-game.fsproj` — the single source of truth for dependency order.
- **Module**: One top-level `module X` per file, no namespace (the `Narrative`
  namespace excepted); a name collision between a global module and an opened
  module's nested module is a latent wrong-module trap.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of source files sit in the cluster block matching their
  compile-list position — verified by a dependency-graph scan showing zero
  upward cluster edges.
- **SC-002**: Zero global module names are declared by more than one file or
  collide with a `Narrative.*` module name.
- **SC-003**: `dotnet build` succeeds with 0 warnings, 0 errors; the full suite
  passes 666/666 tests with no test logic changed.
- **SC-004**: A maintainer can state what each folder is for from
  `docs/PROJECT_STRUCTURE.md` alone: every existing file appears in its layout
  tree (no stale or missing entries).

## Assumptions

- This spec is **retrospective**: the restructuring was analysed, agreed and
  implemented first; the spec/plan/tasks record it after the fact so the
  speckit trail is complete (a deliberate exception to Constitution III).
- No git branch was created; the work sits uncommitted on the working tree.
- `narrative/` is vestigial and out of scope; its namespace inconsistencies
  (global `Roster` vs `namespace Narrative`) are explicitly not addressed.
- `specs/` directories of past features are historical records and are not
  rewritten to the new paths; only `docs/` and code/fsproj comments are
  updated.
- `Entity.Prop` keeps its name: no global `Prop` module exists, so there is no
  collision to fix.
