# Phase 0 Research: Adventure Flags and the Advent Panel

## Decision 1 — A milestone is a closed union, and the flags are an ordered set of them

- **Decision**: `world/Advent.fs` declares `type Advent = OpeningComplete | MetAiko | MetAkane |
  MetShinji | MetYuki` and the two helpers `all` and `name`. The flags themselves are a plain
  `Set<Advent>`, manipulated with the standard `Set` functions.
- **Rationale**: a milestone is an identifier, not free text. A union makes a misspelling impossible,
  makes a match on a milestone exhaustive, and keeps identity off string comparison — the exact
  reasoning `cast/Roster.fs` gives for its cast, and what `Nu`'s `My Game 2` does (`type Advent =
  OpeningComplete | …`). `FSharpType.GetUnionCases` enumerates the cases so `all` and the panel need
  no second list, and `name` is an explicit match so adding a case fails the build until it is named.
  F#'s `Set` orders union values by case tag — declaration order — so iterating the flags is
  deterministic (the panel's rows and the suite's assertions rely on it), and a change produces a new
  set, which makes an edit a before/after snapshot for free (D7).
- **Alternatives considered**: a `string` set, as `odin-game`/`ocaml-game` have — rejected: a
  milestone misspelling would be silently a different flag, and nothing about which flags exist would
  be checked by the compiler (this is the revision the feature was asked for); a `map[[64]u8]` fixed
  buffer, `odin-game`'s allocator-free C shape — same rejection, and a foreign artefact in F#; a
  record of one `bool` per milestone — rejected: a set is the membership question a branch asks, and
  `Set<Advent>` states "has happened" directly; a parameterised case (`Met of Character`) — rejected
  for now as it makes `all` enumerate a product with the roster for no gain at this size.

## Decision 2 — The flag set lives on `SharedState`, not `GameplayState`

- **Decision**: `mutable Advents : Set<Advent>` is a field of `SharedState`, initialised `Set.empty`
  in `SharedState.create`. `mutable`, because the state is a reference type screens share and the one
  history writes the field in place.
- **Rationale**: a flag is project state that belongs to no zone and must survive a zone switch — the
  same reason the cast binding (`specs/032`, D5) and the zone registry (`specs/034`, D11) live there.
  `docs/ARCHITECTURE.md` already lists `advents` among `Shared_State`'s singletons. A future trigger
  and the cue engine's advent ops will read it from the shared state.
- **Alternatives considered**: `GameplayState` — rejected, a zone switch is not a story reset; a
  module-level singleton — rejected, the project keeps state on the state records.

## Decision 3 — An edit is a state-level step on the one history

- **Decision**: `Command.Advent of Previous : Set<Advent> * Next : Set<Advent>`, handled by
  `state/History.fs` (`apply` writes `shared.Advents <- next`, `undo` writes `<- previous`). The
  zone-level `Command.apply`/`undo` **refuse** it (it joins `ZoneMove`/`ZoneAdd`/`ZoneDelete`/
  `SetCue` in the `invalidOp` group), because it is not a zone's.
- **Rationale**: `docs/EDITOR_DESIGN.md` D-011 names the advent flags as a surface owed a before/after
  snapshot on a stack. `specs/048` already made the one history state-level, so the flag's step needs
  **no second stack**; it is another arm on the same timeline, and a step that names no zone is what
  keeps it reachable after the developer has moved zones (`specs/048` research D2).
- **Alternatives considered**: a direct write, like the cast binding (D-022) — rejected: the design
  names the flags as *editable* surface owed an undo; a per-project second stack — rejected: it would
  duplicate routing and pruning the one history already does.

## Decision 4 — The panel is the "Advent" tab on the right, and the dock line is native

- **Decision**: `editor/AdventPanel.fs` draws an ImGui window named `"Advent"`, docked to the **right**
  node beside `"Inspector"` and `"Cue"`. Because the layout is built in C++
  (`cimgui_setup_dock_layout`), `native/cimgui_sdl3.cpp` gains `ImGui::DockBuilderDockWindow("Advent",
  right);` and `libcimgui.so` is rebuilt with `native/build.sh`.
- **Rationale**: the panel edits a set of story facts, not a place in the scene, so it belongs with
  the editing surfaces in the right column (`docs/EDITOR_DESIGN.md`'s Workspace table, revised). Until
  the native rebuild lands the window simply floats, because layout persistence is deliberately
  disabled (`docs/RENDERING_NOTES.md`) — the same tolerant fallback every earlier panel documents.
- **Alternatives considered**: the left column, as the first cut had it and as the Workspace table
  originally said — rejected on review: the Advent is not a place/navigation surface like Cast or the
  Zone Registry, so the right column is the honest home.

## Decision 5 — The Inspector is the selected tab when the editor opens

- **Decision**: `editor/Editor.fs` submits `Inspector.draw` **last** among the docked panels.
- **Rationale**: when a dock node's tab bar is first built, ImGui selects the last window submitted to
  that node (`tab_bar->Tabs.back()`, `imgui.cpp`), so the tab that shares the right node and is drawn
  last is the one on top. The Inspector shares that node with the Cue and the Advent, so submitting it
  after both is what makes it the selected tab at startup, with no focus call and no state. (This is
  the same mechanism that made the Cue, previously the last-drawn panel, the startup tab.)
- **Alternatives considered**: `SetWindowFocus("Inspector")` after a frame — rejected: it needs a
  second flag to avoid firing before the windows exist and only reaches the same result a frame later;
  a native `SelectedTabId` poke — rejected as fragile against the tab-id derivation.

## Decision 6 — The panel is a checklist, one box per case

- **Decision**: the panel draws `Advent.all`, one `Checkbox` per case, seeded with `Set.contains`; a
  toggle calls `commit` with `Set.add`/`Set.remove`.
- **Rationale**: the milestone vocabulary is closed, so an author chooses from it rather than typing
  it — the checklist shows *which flags exist* as well as which are set, which a text field cannot.
  This removes the string version's blank-name and duplicate-name cases entirely. The interaction
  matches the reference panels' list of flags (the checkbox *is* the flag's state), minus the Add
  field the string shape needed.
- **Alternatives considered**: a text field + Add, as the first cut and as `ocaml-game`/`odin-game`
  have — rejected: it exists only because those references key on strings; with a closed union there
  is nothing to type.

## Decision 7 — The command names the whole set, before and after

- **Decision**: `Command.Advent` carries the two set values, not a milestone plus a verb.
  `History.commit` applies the `Next` set and pushes the step; undo writes `Previous`.
- **Rationale**: the set is small and immutable, so a whole-set snapshot is simpler than
  `(milestone, set/cleared)` and is exactly the "before/after snapshot of the immutable set" D-011
  prescribes. Undo is total: an add and a remove are the same shape, so a step can never be applied
  backwards by mistake.
- **Alternatives considered**: `AdventSet of Advent` / `AdventClear of Advent` — rejected: two cases
  where one snapshot suffices, each having to re-derive the prior set on undo.
