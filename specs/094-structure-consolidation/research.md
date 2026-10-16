# Research: Project Structure Consolidation

**Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

No open unknowns existed: the work was analysis-led, and the analysis is
reproducible (the scan script is in `quickstart.md`). This file records what the
scan found and the decision each finding produced.

## Method

A one-off Python script parsed `fsharp-game.fsproj` for the compile order, took
each file's first top-level `module` as its identity, and found cross-file
references (`Module.` not preceded by `.`/word-char, comments stripped). From
that: order-violation check, cluster-boundary check, and a topological level for
every file (the shallowest position it could hold).

## Findings

1. **The documented layering was already almost perfectly held.** The only
   upward cluster edges were the two documented "deliberate inversions":
   `world/ScenarioFork.fs` and `world/CueEngine.fs` referencing `state/`
   (`SharedState`/`GameplayState`). `core`, `physics`, `render`, `audio` never
   reach up; no game file references `Narrative` at all (only tests do).
2. **`state/History.fs` did not need its inversion.** Its references top out at
   `GameplayState`/`Command`/`SharedState` — it never names `ScenarioFork` or
   `CueEngine`. The in-file comment only justified sitting after
   `GameplayState.fs`; the placement after `world/CueEngine.fs` was a vestige.
3. **`world/` had 22 flat files spanning topological levels 0–13** across four
   concerns: domain data, asset management, domain rendering, runtime engines.
4. **Module-name collisions were compile-safe but reader-hostile.** The
   Narrative modules are namespaced (`Narrative.History`, `Narrative.Engine`),
   so nothing merged — but a grep for `History.`/`Engine.` hits both sides.
   `Entity.Actor` (nested companion module) shadowed the global `Actor` in the
   32 files that `open Entity`; its only call sites were two in `world/Zone.fs`.
5. **`screen/DebugOverlay.fs` was referenced only by the app shell** and needed
   only `Diagnostics` + `ImGui` (topological level 1).

## Decisions

### D1: Reunite `state/` in the compile list

- **Decision**: List `state/History.fs` immediately after `state/GameplayState.fs`.
- **Rationale**: Finding 2 — the move compiles with zero other changes, which
  proves the claim.
- **Alternatives considered**: Leave the split and fix only the comment —
  rejected: the declared map would still disagree with the real one.

### D2: Re-home the genuine inversion into `state/`

- **Decision**: Move `world/ScenarioFork.fs` and `world/CueEngine.fs` to
  `state/`, listed after `state/Undo.fs`.
- **Rationale**: Their effects land on the gameplay state; `state/` becomes
  "the gameplay state and the engines that drive it", and folder = compile
  layer with zero inversions.
- **Alternatives considered**: (a) A new `runtime/` cluster between `state/`
  and `editor/` — rejected: a whole folder for two files, against Minimalism.
  (b) Invert the dependency (ops act through callbacks/an interface) —
  rejected: real complexity for no behavioral need. (c) Accept and document —
  rejected: keeps folder ≠ layer, the exact confusion that prompted the work.

### D3: `DebugOverlay` → `render/`

- **Decision**: Move to `render/DebugOverlay.fs`, compiled at the end of the
  `render/` block.
- **Rationale**: Technology-only (Diagnostics + ImGui), drawn by the app shell
  over any screen; sits beside `DebugDraw`.
- **Alternatives considered**: `editor/` — rejected: it is runtime diagnostics
  chrome shown while playing, not an editing tool.

### D4: Group `world/` into `assets/` and `render/` subfolders

- **Decision**: `world/assets/` = AssetLoad, Assets, ActorAsset,
  EnvironmentAsset; `world/render/` = ActorRender, EnvironmentRender,
  EnvironmentBatch, DialogueRender, PromptRender. Domain data and `Systems`
  stay at the root. Compile order unchanged.
- **Rationale**: 22 flat files mixed three concerns; subfolders cost nothing
  (F# compiles by list, not by directory); `narrative/Units/` sets the
  precedent.
- **Alternatives considered**: Split `world/` into several top-level clusters —
  rejected: its files interleave in the compile order across the sub-concerns
  (e.g. `Advent` sits between the renderers and the asset substrate), so
  separate clusters would declare an order the dependencies do not have.

### D5: `History` → `Undo`

- **Decision**: Rename the module and file to `state/Undo.fs`; update ~120 call
  sites across `editor/` and `test/`; update the `"History.%s"` report prefix.
- **Rationale**: One referent for `History.` in game code (Finding 4); the file
  documents itself as "the one undo history", so `Undo.commit`/`Undo.undoHistory`
  read naturally.
- **Alternatives considered**: Rename `Narrative.History` — rejected:
  `narrative/` is out of scope. Leave it — rejected: grep-hostile forever.

### D6: Root `Engine` → `Shell`

- **Decision**: Rename the module and file to `Shell.fs`; `Program.fs` calls
  `Shell.run`.
- **Rationale**: Removes the second collision (Finding 4); the file's own
  header already calls itself "App shell".
- **Alternatives considered**: `GameLoop` — rejected: it also owns SDL bring-up
  and teardown, not just the loop. Leave it — rejected: same as D5.

### D7: `Entity.Actor` → `Entity.ActorEntity`; keep `Entity.Prop`

- **Decision**: Rename the nested companion module; update the two call sites
  in `world/Zone.fs`.
- **Rationale**: Ends the shadowing of the global `Actor` module in the 32
  files that `open Entity` (Finding 4). `Entity.Prop` has no global namesake —
  no collision to fix.
- **Alternatives considered**: Rename the global `Actor` module — rejected:
  more call sites, and the `ActorRender`/`ActorAsset` naming family fits the
  global one. Rename `Prop` too for symmetry — rejected: Minimalism.

### D8: Sweep docs and comments; leave `specs/` historical

- **Decision**: Update `docs/PROJECT_STRUCTURE.md` structurally (it also had
  stale entries for the long-retired `ZoneRegistry.fs`/`ZoneRegistryPanel.fs`
  and was missing `Diagnostics`/`Lru`/`Maths`/`InstanceBuffer`); fix path
  references in the other `docs/` files, code comments and fsproj comments.
  Leave `specs/` untouched.
- **Rationale**: `docs/` are living references (AGENTS.md calls them the source
  of truth); `specs/` are dated historical records.
