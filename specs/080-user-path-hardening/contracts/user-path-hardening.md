# Contract: Graceful Failure on User-Facing Paths

**Feature**: `specs/080-user-path-hardening` | **Date**: 2026-10-12

The project exposes no external API; its "interface" is the behaviour an author or player observes for a
given action. This file fixes that contract: for each action, what is guaranteed, what a diagnostic says,
and how it is observed headlessly. It is the reference `/speckit-tasks` and the tests must satisfy.

## Global guarantee

- **G1.** No action listed here terminates the process, whatever the state of the scene, cue or assets.
- **G2.** When an action performs no effect, exactly one diagnostic line is written naming the operation
  and the reason (the existing `eprintfn` convention). The world is otherwise unchanged.
- **G3.** A report is emitted once per event, never per frame.
- **G4.** Fail-fast is preserved for states only a programming error can produce (stale slot handles,
  double-free, `Occupy` on a taken slot, broken internal invariants). Those still raise.

## Per-action contract

| Ref | Action | Guarantee | Diagnostic (shape) | Observed by |
|-----|--------|-----------|--------------------|-------------|
| US1 | Play with a player actor whose asset lacks the standing/moving clip | The actor is rendered in a defined pose; the run continues | `animation: no clip '<name>' in asset '<id>'; using <fallback>` | a headless actor-renderer test, plus the quickstart Play scenario |
| US2a | Run a set-camera op naming an absent place | The op performs nothing; the cue continues | `cue: SetCamera(<place>/<waypoint>) performs nothing: place '<p>' is not present` | `CueTests` (headless `GameplayState`) |
| US2b | Run a set-camera op naming a waypoint that does not resolve / is not alive | as US2a | `… no waypoint named '<w>' in '<p>'` / `… waypoint '<w>' is not alive` | `CueTests` |
| US2c | Run a fly-camera op naming a path of fewer than two live waypoints | as US2a | `… path '<p>' has fewer than two waypoints` | `CueTests` |
| US2d | Run a fly-camera op with speed ≤ 0 | as US2a | `… speed must be positive` | `CueTests` |
| US2e | Run a cue that is a bare scenario-fork op | The scenario is forked; the cue completes | (none — it succeeds) | `CueTests` |
| US3 | Select/edit/add-to/remove an `If` branch in the cue editor | The edit is applied; the node is rebuilt | (none — it succeeds) | `CuePanelTests` |
| US4a | Undo a step whose place a play session removed | The step is skipped and reported; the run continues | `History.apply/undo: skipping <step>: the place '<p>' is not present` | `GameplayStateTests` |
| US4b | Redo the same | as US4a | as US4a | `GameplayStateTests` |
| US4c | Undo/redo a switch to a removed place | as US4a | as US4a | `GameplayStateTests` |
| US5 | Create an entity when the kind's map is at capacity | No entity is created; a reason is reported | `editor: cannot add <kind> to '<place>': at capacity (<cap>)` | `SpawningTests` |
| US6 | Register a scenario while a waypoint's successor is deleted | The link is captured as absent; the run continues | (none — it succeeds) | `ScenarioTests`/`ScenarioForkTests` |
| US7a | Load an unreadable/malformed/unsupported model asset | The asset is skipped and reported; the scan continues | `asset '<id>': <message>` | `AssetsTests` / `AssetLoadTests` |
| US7b | Parse a model with a cyclic/oversized node graph | Reported as an error, not an unrecoverable failure | `asset '<id>': <message>` | `AssetsTests` |
| US7c | A missing font | Reported; the run continues with text disabled | `ui: failed to open the font '<path>': <sdl error>; text disabled` | quickstart (font removed) |
| US8 | An unexpected failure on the frame path | Reported with the failing phase; controlled teardown | `frame failed in phase '<phase>': <exception>` | quickstart (forced failure) |

## Non-goals (explicitly out of contract)

- Window-resize font/render-target failures, long-text and UI batch-capacity limits, environment-residency
  races, stale physics handles, and native-interop access violations.
- Cue undoability — whether a cue's runtime effects become history steps.
