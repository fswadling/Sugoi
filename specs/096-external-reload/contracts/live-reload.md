# Contract: Live Reload — the Project File Watched at Runtime

What the game does when `project.manifest` changes underneath it (`specs/096`). Extends the write/read contract of
`specs/095` (its [project-file](../095-scenario-persistence/contracts/project-file.md) contract) with a runtime poll.

## The watched artifact

- **The project file** — `project.manifest` at the launch working directory, the one artifact `specs/095` persists.
  Nothing else is polled, because nothing else is written.
- **What a change affects** — the **scenario library** only. The file holds nothing else, so a reload cannot change
  the live scene, the active place, the camera or the running cue.

## The poll

- **When**: once per interval (~250 ms), **only while the author is editing**, drained at the end of the gameplay
  screen's frame update. Not during play, and not per frame; the interval is gated by a frame-time accumulator.
- **Detection**: by **content**, `readText path <> lastText` — timestamps and lengths are never consulted. Every save
  style is therefore detected, including a temp-file-then-rename atomic save.
- **API surface**: `Project.pollEnabled (editing: bool) (liveReload: bool) : bool` — the pure gate the caller
  applies (the poll runs only while editing and only when the toggle is on); and
  `Project.pollExternal (dt: float) : Registry option` — `Some registry` for an external change to install; `None` for
  nothing-to-do.

## Outcomes

| Situation | Result |
|-----------|--------|
| External rewrite, decodes | `Some registry`; the caller installs it into `state.Scenarios` |
| Content identical to `lastText` (the game's own write, or a no-op rewrite) | `None` |
| External write mid-flight / undecodable | **reported**; `None`; the running library is **unchanged**; retried next interval |
| File absent / deleted externally | `None`; the running library is **unchanged** (never cleared) |
| Live reload **off**, or the author **not editing** (`pollEnabled` false) | the poll is not called; nothing is installed |

## Guarantees

- **The game never reloads its own writes** — `persist` maintains `lastText` on a successful write (SC-002).
- **A live library is never emptied by the file** — a damaged, partial, or deleted file preserves it (SC-003), unlike
  the startup load, which yields an empty library for a damaged file (`specs/095`).
- **Platform-agnostic** — a content read over `System.IO`; no per-OS event API (SC-004, FR-009).
- **No undo step, no side effects** — installing a reloaded library sets `state.Scenarios` and nothing else, and adds
  nothing to the history (SC-005).
- **Suspensible** — the Misc-panel **Live reload** toggle (default on) stops reloading without affecting saving
  (SC-006).

## Explicitly out of scope

- **Merging** — a game edit and an external edit collide as **last-writer-wins**, with no merge (spec Assumptions).
- **Sub-second latency** — the interval is fixed (~1 s) and not user-configurable; the toggle is the only control.
- **Watching anything but the project file** — assets, shaders and the like are not watched.
- **Persisting the toggle** — it is transient editor state, not written and not a step on the undo history.
