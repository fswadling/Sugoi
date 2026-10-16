# Contract: The Project File — the Scenario Library on Disk

What the game writes and reads (`specs/095`). Supersedes the persistence deferral `specs/083` recorded (its FR-016,
"the library MUST NOT be persisted") and the JSON `Project_DTO` / `project_save` / `project_load` sketch in
`docs/ARCHITECTURE.md`'s Persistence section and `docs/ROADMAP.md` phase 15.

## The file

- **Path**: `project.manifest`, at the working-directory root the game is launched from (beside `assets/`). One file
  holds the whole library; it is not under `assets/` and is not found by the asset scan.
- **Format**: Prime symbolic-expression syntax — records with named fields (`[<SymbolicExpansion>]`), `;` comments
  permitted. Human-readable and hand-editable. Not JSON, not binary.
- **Root**: `[<SymbolicExpansion>] type Project = { Version : int; Scenarios : EntryDto array }`, where
  `EntryDto = { Name : string; Contents : ContentsDto option }`.
- **Version**: `Version = 1` (the current literal). An unrecognised value is a damaged file (below).

## What it holds

- For each scenario, in **authored order**: its name, and either `[None]` (named but not registered) or `[Some …]`
  its recorded contents — each zone's **staging** (environment template, light, sky, placed props) and **narrative
  entities** (actors, waypoints with chains, triggers with their authored cues), plus the scenario's **cue**.
- Field shapes that carry meaning across a restart:
  - `Next [None]` is a waypoint **chain end**; `[Some n]` continues at the zone's waypoint `n`.
  - `Contents [None]` is a scenario **named but not registered**.
  - A cue's run-form ops (`…State`) are **not** part of the contract — a scenario cue is authored (research D10).

## Write contract

- **When**: whenever the library **value** changes — an add, clear, register or delete, and an undo or redo of any of
  them — and **once per change**. No save command, no time-based debounce, no timer. (Detected by comparing the
  current library instance to the last written one; the library is immutable, so a change is always a new instance.)
- **How**: encode the whole library, write to `project.manifest.tmp`, then rename it over `project.manifest`
  (atomic). An interrupted write leaves the previous good file readable.
- **On failure**: a write that fails (a read-only directory, a full disk, a failed rename) is **reported** and the
  write is **skipped**, leaving the previous good file intact; the next change retries, and a write failure is never
  fatal. A library that cannot be encoded (it holds a run-form cue) is reported and skipped too — but that path is
  defensive: a run-form cue cannot be **filed** in the first place (Register refuses it, spec FR-018).

## Read contract

- **When**: once, at startup, before the first frame (`ScreenManager.init`). The load is not a step on the undo
  history.
- **File absent**: the library starts **empty**; no report; nothing is written until the first change. The
  pre-feature first-run behaviour.
- **File damaged** (unreadable, unparseable, or an unrecognised version): **reported** to the error surface, the
  library starts **empty**, and the file is **left on disk unchanged** (never overwritten by the load). It is
  replaced only by the author's next real change.
- **On success**: installs the library into `state.Scenarios`, and nothing else — the live scene, active place,
  camera, running cue, advents and mix are untouched.

## Postconditions

- **Round-trip**: a library the author can build reads back **equal, scenario for scenario**, in the same order,
  including a named-but-unregistered scenario, a chain-ending waypoint and an empty cue (spec FR-003, FR-011, SC-004).
- **Change-following**: every change, including an undo that restores a prior library, is reflected on disk, so a
  reload after any of them yields the library as it stood (spec FR-005, SC-002).
- **Quiescent at rest**: an unchanged library produces **0** writes (spec FR-015, SC-003); a missing file produces
  **0** errors and **0** writes until the first change (spec FR-008, SC-005).
- **Non-destructive**: a damaged file is never silently overwritten, and an interrupted write never corrupts the
  previous file (spec FR-006, FR-009, SC-005, SC-006).

## Explicitly unchanged / out of scope

- **The live scene** — the working `Zones`, the active place, the camera, the running cue, the advents and the mix
  are not persisted (spec FR-013, Assumptions).
- **The retired cast and zone-registry** — not persisted and not reintroduced (spec FR-014).
- **The one history** — saving and loading are not steps; the undo history is unaffected (spec FR-012).
- **Asset manifests** — untouched; the project file is not an asset.
