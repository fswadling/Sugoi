# Research: The Cast — a Roster Hoisted from the Engine and Bound to Actor Assets

Decisions derived from the feature spec, the design discussion that produced it, and the code as
it stands (`narrative/Vocabulary.fs`, `world/Assets.fs`, `state/SharedState.fs`,
`editor/{Editor,AssetBrowser}.fs`, `render/ImGui.fs`, `native/cimgui_sdl3.cpp`).

## Decision 1 — The roster is hoisted to a new layer beneath the engine, not into the game

- **Decision**: `Character`, `characterName` and `characters` move out of
  `narrative/Vocabulary.fs` into a new first-compiled cluster, `cast/Roster.fs`. In
  `fsharp-game.fsproj` the compile list becomes `cast/` → `narrative/` → `core/` → … → `world/`.
- **Rationale**: FR-001 and FR-004 together. The engine must consume one declaration, and it
  must not become able to reference the game — so the roster cannot be in the engine (that is
  the duplication) and cannot be in `world/` (the engine would have to be compiled after the
  game to see it, at which point it could reference the assets, GPU, and entity modules too).
  A layer beneath the engine is the only position satisfying both, and it is a small one: one
  file, asset-free, consumed by both sides.
- **Alternatives considered**:
  - *Declare the roster in `world/` and move `narrative/` after it.* Fewer moving parts, and
    permitted — nothing consumes the engine yet — but it converts the property
    `docs/ARCHITECTURE.md` records ("the engine cannot reference the game" as a compile error
    rather than a convention) into a convention. The engine's whole reason for being listed
    first is that it is deliberately usable with no graphical interface, and that would become
    maintained by discipline.
  - *Declare the roster in `core/`.* `core/` is compiled *after* `narrative/` today, so it is
    the same problem as `world/`; and `core/` is structural engine infrastructure (the slot map,
    the IQM and glTF parsers), not story content.
  - *Make the engine generic over a character type.* The purest way to let the roster live
    anywhere, since the engine would then depend on nothing. Rejected: it undoes the closure
    `specs/025-narrative-core` chose deliberately (research D20, D28), and `Canonical` would
    need a name function threaded through it where today it is a match. The payoff —
    reusability across stories — is something nothing in this project asks for.
  - *Declare the roster in `world/` and re-export it into `narrative/` by a type alias.* Zero
    churn in the engine's sources, but the engine would still own a name for content it does
    not own, and nothing would enumerate the sites that named it. See Decision 8.

## Decision 2 — Identity and appearance are two types, and the roster case stays fieldless

- **Decision**: `Character` is fieldless and stays so. The look vocabulary is a second, separate
  closed union in `world/Casting.fs`. A slot pairs them.
- **Rationale**: the engine's constructs are parameterised by the character
  (`HasTrait of Character * Trait`, `Friends of Character * Character`). If a roster case
  carried its variant — `Akane of AkaneLook` — then naming Akane would require choosing a look,
  so `HasTrait(Akane SchoolUniform, Shy)` and `HasTrait(Akane Casual, Shy)` would be facts about
  two different participants. Change her clothes and every fact about her becomes a fact about
  someone else. The separation is what keeps "who" independent of "which model", and it is why
  the request's literal shape (a roster whose members carry variant options) is not the shape
  built.
- **Alternatives considered**: payload-carrying roster cases, with a total map back to a
  payload-free identity. Rejected — the map would need a second fieldless roster or names parsed
  back into characters, which is precisely the duplication and the string-keyed identity the
  single declaration exists to prevent.

## Decision 3 — The look vocabulary declares itself once, by enumeration and by match

- **Decision**: `Casting` defines `[<RequireQualifiedAccess>] type Look = SchoolUniform | Casual`
  with `lookName : Look -> string` as a match over the cases and
  `allLooks : Look list` enumerated from the type by reflection into a module-initialised value.
- **Rationale**: the panel needs two different things from the vocabulary — every case, to draw
  the leaves, and a printable name, to label them. `allLooks` by reflection is the idiom
  `Vocabulary.characters` already uses for exactly this reason ("enumerated from the type rather
  than listed again, so a case cannot be left out of it"), so a case added to `Look` appears in
  the panel with no edit. `lookName` as a match follows the project's rendering rule for a
  fieldless union (research D30: matching measured ~0.3 us a value against ~17 us for `string`/
  `ToString`), so adding a case is also a compile error until it is named. Neither runs per
  frame: `allLooks` is a value, and the match is only called for the leaves drawn.
- **Alternatives considered**:
  - *A hand-listed `allLooks`.* One less reflection call, but a second place the cases appear,
    which is the "table to keep in step" the project refuses elsewhere. `lookName`'s match
    would force a new case to be *named* but not to be *listed*.
  - *Per-character variant types* (`AkaneLook`, `ShinjiLook`, a `Look` union of them). Rejected
    in the spec's Clarifications: it puts the roster's names in a second place for no check the
    binding does not already give, and whether a member has a look is a fact about the art, not
    a type.
  - *Rendering a look with `string`/`ToString`.* Rejected for the reason the vocabulary's own
    renderers are matches, and because the performance guard in the narrative suite exists to
    keep rendering off the reflection path.

## Decision 4 — The binding is sparse, and an entry's existence is the existence check

- **Decision**: `Cast` holds `Dictionary<Slot, string>` keyed by a `[<Struct>]` slot of a member
  and a look, whose value is an actor asset id. There is no availability declaration: a member
  has a look if and only if the map has an entry for that slot.
- **Rationale**: FR-006, and the spec's clarification that not every member has every variant is
  fine. A separate availability list would be a second source of truth for something the
  binding already states, and it would have to be kept in step with the art rather than with the
  code. The consequence is deliberate and visible: the panel offers the full member × look cross
  product, eight slots today, and an unassigned leaf reads as "no model for this look" rather
  than being absent (FR-010).
- **Alternatives considered**: a total `looks : Character -> Look list` availability match.
  This was the earlier recommendation and the user rejected it in favour of the map; it is
  recorded because it is the obvious alternative — it would make availability a compile-time
  obligation, but it duplicates what the binding says and would need editing whenever a model is
  added.

## Decision 5 — The binding lives on `SharedState` and is not undoable

- **Decision**: `SharedState` gains `Cast : Casting.Cast`, beside `Assets`. The panel mutates its
  entries in place. Nothing pushes it onto an `UndoStack`.
- **Rationale**: FR-011. The undo history is genuinely per zone — `GameplayState.UndoByZone`
  maps a zone name to its `UndoStack` and `enterZone` swaps the incoming zone's stack into
  `EditorState.Undo` on a switch, "so an edit's undo always applies to the zone it was recorded
  in". The cast binding outlives every zone, so a binding recorded in one zone's stack would be
  unreachable from another. The precedent is the player designation (`Zone.Player`, set from the
  Inspector) and the collider overlay (`Zone.setShowCollider`, whose own doc comment says a
  direct write is right because "the overlay is an editor visualisation preference, not a scene
  edit"); both are binding actions rather than undoable edits. `SharedState` is where
  cross-screen, session-scoped values live — the loaded assets are there for the same reason.
- **This contradicts two statements in `docs/EDITOR_DESIGN.md`, and that document is corrected
  by this feature rather than quietly departed from**: its Principle 4 ("Undo everywhere") and
  its non-entity-surfaces bullet, which names the cast among the surfaces that "store name-keyed
  before/after snapshots". D-022 records the exception and its reason, so the next reader meets
  the decision where the model is described instead of inferring it from the absence of a
  command.
- **Alternatives considered**: putting the binding on `EditorState` (transient UI state, and
  wrong — the binding is data the game will read, not UI state); making it undoable (rejected: no
  zone to scope the stack to, and undoing a model assignment is not a scene operation). The
  project-scoped stack such an edit would need is real work — a second stack and a routing rule
  for Ctrl+Z — but it is not owed to this binding: it is owed to the four *editable* non-entity
  surfaces (scenario, zone registry, advent flags, cue root), and an assignment made by picking a
  model from a list costs less to re-make than to undo. D-022 therefore excludes the binding from
  that group rather than deferring it into it.
- **The location is settled by this reasoning, not by command reachability.** A cast edit is not a
  command, so `Command` never needs to name the binding, which removes the only structural
  reason to care where it lives. `SharedState` is chosen because `docs/ARCHITECTURE.md` already
  lists `cast_members` there (and draws `SS --> CAST`), so the choice needs no amendment to that
  document — and it puts the binding beside the assets it refers to.

## Decision 6 — The panel reuses the collapsing header, and adds five bindings

- **Decision**: `editor/CastPanel.fs` draws each member as the already-bound
  `igCollapsingHeader_TreeNodeFlags` node (closed by default), and each look beneath it as a
  label plus an `igBeginCombo` menu of the discovered actor assets, with a `(none)` item that
  clears the slot. `render/ImGui.fs` gains `igPushID_Str`, `igPopID`, `igBeginCombo`,
  `igEndCombo`, `igSelectable_Bool`.
- **The panel assigns in place, which overrides `docs/EDITOR_DESIGN.md`'s interaction model, and
  that document records the override as D-022.** The doc's model is one selection → one
  Inspector, with the Cast panel a list of members whose selection the Inspector edits. That is
  rejected here because a *slot* is not a selectable item in a one-selection model (the
  Inspector would need a second notion of what is being edited) and because binding eight slots
  through a two-step round trip each is worse than doing it in the list, where a member's other
  looks are visible for comparison. The dock position was likewise specified by the doc — a tab
  in the **left** panel beside the Outliner — and this plan now follows it rather than inventing
  a bottom-node position.
- **Rationale**: the existing binding has no tree or combo widget, so the panel is only possible
  with additions — but most of what a tree view needs is already there. A collapsing header *is*
  a tree node visually and needs no `igTreePop`, which is why `igTreeNodeEx_Str`/`igTreePop` are
  not required. Every look label repeats under every member (`Casual` four times), so
  `igPushID_Str` per member scopes them; that is the idiom for repeated labels, and it is needed
  by this feature rather than added speculatively. All seven candidate exports were confirmed
  present in the shipped library with `nm -D native/libcimgui.so`, so the additions are
  P/Invokes only — no `.so` change is needed for the widgets themselves.
- **Alternatives considered**:
  - *`igTreeNodeEx_Str` and `igTreePop` for a stricter tree.* Available and verified, but it
    buys nothing the collapsing header does not do here, and the leaves are label-plus-combo rows
    rather than nodes.
  - *Unique `##`-suffixed labels instead of `igPushID_Str`.* One less binding pair, but it means
    building a unique label string per leaf per frame; the ID stack is the intended mechanism
    and allocates nothing itself.
  - *A separate clear button per row.* Unnecessary: clearing is one of the combo's items, and
    `smallButton` already exists if it is ever wanted.

## Decision 7 — The dock line is native, and the feature does not depend on it

- **Decision**: add `ImGui::DockBuilderDockWindow("Cast", left);` to
  `native/cimgui_sdl3.cpp`, so the panel is a tab beside the Outliner exactly as
  `docs/EDITOR_DESIGN.md`'s Workspace table already specifies ("Cast, Advent, Cue, Scenario, and
  Zone Registry dock as tabs in the **left** panel"), and rebuild `native/libcimgui.so` with
  `native/build.sh`. `Editor.panelNames` gains `"Cast"` so Ctrl+Tab cycles to it. The `left` node
  id is already computed by the shim for the Outliner, so following the document costs nothing
  extra.
- **Rationale**: the default layout is built deterministically in the shim on every run, because
  `cimgui_enable_docking` sets `io.IniFilename = nullptr` and disables layout persistence. A new
  window that the shim does not place therefore floats at a default position on every start.
  DockBuilder is `imgui_internal.h` and is not exposed by the vendored cimgui's `ig*` wrappers —
  which is exactly why the shim exists — so the line cannot be a P/Invoke.
- **Cost recorded honestly**: `native/cimgui` is not vendored in the working tree (it is empty;
  `build.sh` clones the `docking_inter` branch with submodules), so the rebuild needs network
  access and SDL3 development headers. The panel is complete and usable without it; only its
  dock position is worse. The dock line is therefore the last task and its absence is not a
  spec failure (FR-009 asks for the tree, not for a dock slot).

## Decision 8 — Deleting the members is what enumerates the sites

- **Decision**: `Character`, `characterName` and `characters` are **removed** from
  `Vocabulary`, and the files that named them gain `open Roster` (or read `Roster.characterName`).
  No alias or re-export is left behind in `narrative/`.
- **Rationale**: the same mechanism `specs/031-entity-authored-state` relied on — removing a
  member turns every reference into a compile error, so no site can be missed, and the count is
  reported by the compiler rather than estimated. An alias would let the engine's sources
  compile untouched, which is precisely what would hide the sites that named the roster and let
  the engine keep reading as though it owned it.
- **Scope of the churn**: the roster's own rendering is used only from `narrative/Canonical.fs`
  (four sites); the rest is `open Roster` in the files whose signatures name `Character`, plus
  the test references (`NarrativeVocabularyTests`, `NarrativeHarnessTests`,
  `NarrativePerformanceTests`). The compiler names all of them.

## Decision 9 — The binding's surface is a map and one lookup

- **Decision**: `Casting` exposes `Look`, `lookName`, `allLooks`, `Slot`, `Cast`, and
  `tryAsset`. It defines no `assign` or `clear`: the panel writes
  `shared.Cast.Bindings.[slot] <- id` and removes an entry directly, and `SharedState.create`
  constructs the record inline.
- **Rationale**: Minimalism, and the rule of three — each of those operations has exactly one
  caller. `tryAsset` is kept because it is the binding's *question* (FR-007, "including the
  absent case"), it converts `TryGetValue`'s tuple into the `option` the project prefers for an
  expected absence, and it is what the headless test asserts and what the later consumer calls.
- **Alternatives considered**: `Casting.create` (a one-line constructor with one call site);
  `assign`/`clear` wrappers (one call site each, and they would hide that the binding is a plain
  map). If a second caller appears — placement, or persistence — the wrapper earns its place
  then.

## Decision 10 — Resolving a stored id raises; offering an id cannot be wrong

- **Decision**: the panel's combo iterates `shared.Assets.Actors`, so only discovered assets can
  be assigned (FR-008) and no validation path is needed. When the panel needs an assigned asset's
  display name it reads `shared.Assets.Actors.[id].Name`, which raises for an id that names no
  loaded asset.
- **Rationale**: Fail Loudly II. Assets are scanned once at startup and the binding is built by
  selecting from that scan, so a dangling id cannot arise in this feature; if it ever does — a
  loaded binding written by a future persistence feature — it is a bug to report rather than a
  row to skip silently.
- **Alternatives considered**: a `TryGetValue` at the display site rendering the raw id.
  Rejected as a just-in-case fallback for a state this feature cannot produce.

## Decision 11 — Four documents state what this feature changes, and all four are corrected

- **Decision**: `docs/ARCHITECTURE.md` (the narrative section's roster bullet, and the paragraph
  explaining that `narrative/` is listed first), `docs/PROJECT_STRUCTURE.md` (the new `cast/`
  cluster, the abridged compile list, the cluster descriptions, and the dependency chain),
  `docs/ROADMAP.md` (phase 10's `world/Cast.fs` line) and `docs/EDITOR_DESIGN.md` (the Workspace
  table, the Cast panel's row and dock, Principle 4's undo scope, the non-entity-surfaces
  bullet, and a new D-022) are updated in the same change. `specs/025-narrative-core` gains an
  amendment recording that the roster moved and that `narrative/` is no longer compiled first,
  in the form that spec already uses for its two earlier amendments.
- **Rationale**: `narrative/` being first is stated as a deliberate decision with a rationale in
  two of them, and it stops being true here; `EDITOR_DESIGN.md` states the Cast panel's dock and
  interaction model, and this plan departs from one and follows the other. A document that
  contradicts the `.fsproj` or the code on the next read is worse than the churn of correcting
  it. 025 is amended rather than edited because its history is a record of what was decided then.

## No unresolved clarifications

Every choice is settled by the spec, by the design discussion, or by the library inspection
recorded in Decision 6. No `NEEDS CLARIFICATION` remains for Phase 1.
