# Implementation Plan: Live Reload of the Project File

**Branch**: `096-external-reload` | **Date**: 2026-10-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/096-external-reload/spec.md`

## Summary

Pick up an external edit to `project.manifest` without a restart. `world/Project.fs` gains a **content poll** — a
`pollExternal (dt: float) : Registry option` that, once per interval, reads the file and compares its text to the text
last written or read. If the text differs and decodes, the reloaded library is returned for installation; if it
differs and does *not* decode (a partial or damaged write), it is reported and nothing is installed; if it is
identical, nothing happens (which is how the game's own auto-save avoids reloading itself). `screen/GameplayScreen.fs`
drains the poll at the end of the frame while the author is editing, gated by a **Live reload** toggle on `EditorState` surfaced in
`editor/MiscPanel.fs`.

The comparison is by **content**, not timestamp, so it is correct on coarse-timestamp filesystems and detects every
save style uniformly — including a temp-file-then-rename ("atomic save"), verified to raise `Renamed` (which a
file-bound `FileSystemWatcher` misses). The poll is plain `System.IO`, so the feature is platform-agnostic by
construction and needs no per-OS event API and no debounce.

A runtime damaged/deleted file **keeps the running library** (and retries) — deliberately unlike the startup load,
which yields an empty library, because at runtime there is a live library to preserve. The reload is **not** an undo
step and changes nothing but the scenario library.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `System.IO` (`File`), `core/Diagnostics.fs` (the report surface), the existing
`world/Project.fs` store; `editor/MiscPanel.fs` and `state/EditorState.fs` for the toggle; `screen/GameplayScreen.fs`
for the drain. No new package, no OS-specific API.

**Storage**: the `project.manifest` of `specs/095` — read (polled) and compared, never rewritten by this feature.

**Testing**: xUnit, headless (`dotnet test`). `test/ProjectTests.fs` gains the reload tests over a temp directory
(external change installed; identical rewrite ignored; the game's own write ignored; damaged/partial reported with
nothing installed; deletion ignored; the interval gate does not re-read on a second immediate call). No device.

**Target Platform**: Linux desktop (the mechanism is portable; nothing here is Linux-specific)

**Project Type**: Desktop game (data-oriented engine + editor, narrative engine in-process)

**Performance Goals**: off the play path. One file read and one string compare **per ~250 ms while the author is
editing** (an interval accumulator gates it) — never during play and never per frame; the poll allocates only when the
file actually changed.

**Constraints**: warning-free build; green headless suite; no device opened; detection by content (not timestamp); the
game's own writes never trigger a reload; nothing but the scenario library is touched; the toggle affects reloading
only, never saving; the poll runs only while editing, so play performs no file I/O.

**Scale/Scope**: game source — 1 module edited (`world/Project.fs`: `readText`/`tryDecode`/`writeAtomic` factored out,
`pollExternal` + the store's `lastText`/accumulator added), 3 edited (`state/EditorState.fs` one field,
`editor/MiscPanel.fs` one checkbox, `screen/GameplayScreen.fs` the drain); tests — 1 edited (`test/ProjectTests.fs`);
docs — `ARCHITECTURE.md`, `EDITOR_DESIGN.md`. No file added or deleted.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. One new operation on an existing module, one editor field and one checkbox, and one line
  at the frame drain. It adds no new module, no new file, no new dependency and no new state owner; the toggle reuses
  the existing Miscellaneous panel rather than adding a surface.
- **II. Fail Loudly** — PASS. A partial or damaged external write is **reported** (`Diagnostics.reportf`) and nothing
  is installed. The non-fatal cases — an absent file, or a deletion — are the *expected* states of a file the author
  or an agent may remove, and they leave the live library untouched rather than raising.
- **III. Spec-First** — PASS. This feature is `specs/096-external-reload`, extending `specs/095`'s project file.
- **IV. Data-Oriented Architecture** — PASS. The reload yields the same plain library value; it adds a `string` and a
  `float` to the existing store and nothing to the data model.
- **V. Minimise GC Pressure** — PASS. The poll runs once per second, off the hot path, and allocates only when the
  file changed; the per-frame cost is a float addition.
- **Narrative subsystem (N-I…N-V)** — N/A. The narrative engine holds no scenario library; the change does not reach
  it.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/096-external-reload/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/
│   └── requirements.md
├── contracts/
│   └── live-reload.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Project.fs              # factor out `readText`/`tryDecode`/`writeAtomic`; add `pollExternal (dt)`, the pure gate
                              #   `pollEnabled (editing) (liveReload)`, and the store's `lastText` + interval
                              #   accumulator; `load`/`persist` maintain `lastText`
state/EditorState.fs          # NEW field `LiveReload : bool` (default true), beside the other editor toggles
editor/MiscPanel.fs           # a "Live reload" checkbox writing `state.Editor.LiveReload` (no step, no Command)
screen/GameplayScreen.fs      # end of `update`: `if Project.pollEnabled state.Editing state.Editor.LiveReload then` — bind the poll then `match` it: `Some registry -> state.Scenarios <- registry`, `| _ -> ()` (a wildcard, since `open Screen` shadows the option's `None`)
test/ProjectTests.fs          # the reload tests, over a shared temp-directory helper (chdir); add the interval gate
docs/ARCHITECTURE.md          # the Persistence section: note the runtime content poll / live reload
docs/EDITOR_DESIGN.md         # the Miscellaneous panel: the Live reload toggle
```

**Structure Decision**: single project, F#'s file-order model. Everything lives in `world/Project.fs` because that
module already owns the project file and its store (`lastWritten`), and `pollExternal` needs nothing from `state/` —
it is gated at the call site, so it never has to know about `EditorState`. The toggle is an ordinary editor boolean on
`EditorState` (the `Snap`/`Tool` pattern) surfaced by the existing Miscellaneous panel; the drain is one line beside
the existing `persist` call. Nothing changes shape and no new module or package is introduced.

## Complexity Tracking

> No Constitution Check violations.
