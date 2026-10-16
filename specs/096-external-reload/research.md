# Research: Live Reload of the Project File

## D1 — A content poll, not `FileSystemWatcher`

**Decision**: detect external changes by periodically **reading the file and comparing its text** to the text last
written or read. No `FileSystemWatcher`, no inotify/FSEvents/ReadDirectoryChangesW.

**Rationale**: three properties make the poll strictly better here.

1. **Portability.** It is plain `System.IO`; `FileSystemWatcher` is *also* cross-platform in the BCL, but it fronts
   three different OS mechanisms with three sets of quirks. A content read has none.
2. **Every save style is detected uniformly.** Measured on this machine (a temp dir, a directory-bound watcher):
   an in-place write fires `Changed`; a **temp-file-then-rename** ("atomic save") fires **`Renamed`** — and a watcher
   bound to the *file* rather than the directory goes deaf on a rename, because the inode is replaced. The poll does
   not care how the bytes landed. (If a watcher were used, it must watch the **directory** and match the name, and
   must debounce the 2+ events each write produces.)
3. **No debounce.** The watcher's event storms (two events per write, above) require coalescing; the poll has one
   read per interval by construction.

**Alternatives considered**: `FileSystemWatcher` (rejected — more moving parts, per-OS quirks, event storms, and the
file-vs-directory trap); a hybrid (watcher as trigger, content-compare as the decision) — viable and low-latency, but
unjustified complexity when ~1 s is ample for "an agent edited the file".

## D2 — The feedback loop is closed by comparing to the last-written text

**Decision**: keep `lastText : string` — the file content the game last wrote or read. An external change is
`readText projectPath <> lastText`. Both `load` and `persist` set `lastText`; the comparison is therefore `false` for
the game's own writes.

**Rationale**: the game **writes this file** (`specs/095`), so a naive poll would see its own saves and reload them
(a pointless churn, and a way to clobber in-flight state). A content comparison closes the loop in one line and also
makes a no-op external rewrite (identical bytes) a no-op. Decoding and comparing the *library* would not work: Prime's
`OMap` equality is reference-based (`specs/095` research D5), so two equal libraries compare unequal. Timestamps are
also unusable — some filesystems have 1-second granularity, so a same-second rewrite of the same length would be
missed; the content compare has no such blind spot.

**Alternatives considered**: compare modification times (rejected — coarse granularity); compare decoded libraries
(rejected — `OMap` reference equality); a "we are writing now" boolean window (rejected — racy across frames, whereas
the text compare is exact).

## D3 — `writeAtomic` reports success, so `lastText` reflects the file, not the intent

**Decision**: factor the write into `writeAtomic : string -> string -> bool` (which does the encode-free `.tmp` +
rename and reports a failure). `saveTo path registry = writeAtomic path (encode registry) |> ignore`; `persist`
encodes, calls `writeAtomic projectPath`, and sets `lastText` **only when the write succeeded**.

**Rationale**: if a failed write set `lastText` to the text it *intended*, the next poll would read the unchanged
on-disk content, see it differ from `lastText`, and "reload" the **old** library over the in-memory one — reverting the
author's change. Setting `lastText` only on success keeps it a faithful model of what is on disk. (`lastWritten` is
still updated regardless, as `specs/095` does, to avoid a per-frame retry storm.)

**Alternatives considered**: read the file back after writing (extra I/O for the same answer); set `lastText` only in
`persist` and not in `writeAtomic` (that is what this is — `writeAtomic` just reports; `persist` decides).

## D4 — Runtime damage/deletion keeps the library; startup damage empties it

**Decision**: factor out `readText : string -> string option` (absent → `None`),
`tryDecode : string -> Result<Registry, string>` (illegal → `Error`, carrying the failure's message),
`writeAtomic : string -> string -> bool`, and `decodeStartup : string -> string -> Registry` (the startup policy).
`loadFrom` (startup) uses them with the `specs/095` policy — damaged → **empty** library. `pollExternal` uses them with
the opposite policy — damaged or absent → **keep the running library**, report, and retry next interval.

The decode returns a `Result` rather than an `option` so the failure's message survives into the report: `specs/095`
reported `ex.Message`, and dropping it would lose the parse detail that tells the author *why* the file is bad.

**Rationale**: at startup there is nothing to preserve, so an empty library is the honest result of a damaged file.
At runtime there is a live library the author is working with, and emptying it because an agent wrote half a file
would be destructive; the poll therefore installs nothing and retries — which also **self-heals** a partial write:
once the writer finishes, the text differs from the (unchanged) baseline and decodes.

**Alternatives considered**: one shared policy (wrong for one of the two cases); clear the library on deletion
(rejected — a deleted file must not wipe the working set).

## D5 — The reload is not an undo step, and touches nothing but the library

**Decision**: installing a reloaded library sets `state.Scenarios` and nothing else; no `SetScenario` step is pushed.

**Rationale**: it mirrors the startup load — an arrival of authored state, not an author action in this session — so
putting it on the history would conflate two notions. The reload likewise does not touch the live scene, the active
place, the camera or the running cue, because the file holds only the scenario library (`specs/095`).

**Alternatives considered**: push a `SetScenario` step so `Ctrl+Z` reverts an import (workable, but it complicates the
history for little gain and diverges from the startup load).

## D6 — The toggle lives on `EditorState`; the gate is a pure predicate at the call site

**Decision**: `EditorState.LiveReload : bool` (default `true`), drawn as a checkbox by `editor/MiscPanel.fs`.
`world/Project.fs` exposes a pure gate, `pollEnabled (editing: bool) (liveReload: bool) : bool = editing && liveReload`,
and `screen/GameplayScreen.fs` calls `if Project.pollEnabled state.Editing state.Editor.LiveReload then (match
Project.pollExternal frame.Dt with …)`. `Project` itself does not read `EditorState`.

**Rationale**: `world/Project.fs` is compiled **before** `state/` (`EditorState`), so `Project` cannot read
`EditorState`; passing the two flags into a **pure** predicate keeps the dependency one-directional, keeps
`pollExternal` a pure function of the file, and makes the **gate itself a headlessly testable unit** — spec FR-011
lists "the toggle suspends" among the tests, and a gate buried in the frame loop could not be asserted. The toggle is
an ordinary transient editor boolean (the `Snap`/`Tool` pattern) — not persisted, not a step (`specs/095`/`specs/059`
precedent). It suspends reloading only; `persist` is unaffected.

**Alternatives considered**: pass the flag into `pollExternal` (parameterises the mechanism with a policy, and hides
the gate where it cannot be tested); gate inline in `GameplayScreen` (untestable — the reason for the predicate); a new
panel (unnecessary — the Miscellaneous panel already exists for exactly this).

## D7 — The interval gate is a frame-time accumulator in the store

**Decision**: the store gains `accumulator : float` and `pollSeconds = 0.25` (a plain private constant); `pollExternal
dt` adds `dt`, and returns `None` immediately until the interval has elapsed, then resets it and does the read.

**Rationale**: a per-frame read of a file that can reach hundreds of KB would be tens of MB/s; the accumulator makes
it one read per 250 ms, off the hot path. The accumulator also makes the gate **testable** — a test supplies the `dt`
values and asserts that a second immediate call does not re-read.

**Alternatives considered**: a frame counter (couples the interval to the frame rate); a `Stopwatch` (real time, and
harder to test deterministically).

## D8 — Drained at the end of `GameplayScreen.update`

**Decision**: the poll and the install happen at the end of `screen/GameplayScreen.fs`'s `update`, beside the existing
`Project.persist` call, and **only while the author is editing**.

**Rationale**: the library is the gameplay screen's, and the same place already owns the write; a reload that lands
just before the next frame's write keeps the two in step. Gating on editing keeps **actual play's hot path clear of
file I/O** — the file can reach hundreds of KB, and play must not read it — so an external change made while playing
(or on the main menu) is picked up on the first edit-mode frame, when the author returns to the editor.

**Alternatives considered**: drain in `ScreenManager.update` (runs on every screen, but the library is gameplay-only —
it would reload state nothing is looking at).

## D9 — Verification is headless, over a temp directory

**Decision**: `test/ProjectTests.fs` gains a shared helper that creates a temp directory, sets it as the current
directory (the suite is serialized — `xunit.runner.json` sets `parallelizeTestCollections: false`), runs the body, and
restores and deletes it. The reload tests are: an external rewrite yields `Some` equal to the new library; an identical
rewrite yields `None`; a write by the game itself (then a poll) yields `None`; a malformed external write yields
`None`, is reported, and leaves the file and the library alone, while a subsequent valid write yields `Some`; a
deletion yields `None`; and the interval gate suppresses a second immediate read.

**Rationale**: `pollExternal` is `System.IO` over a path, so it is assertable with no device (the `specs/095`
precedent). The gate test is possible only because the interval is driven by an explicit `dt`.

**Alternatives considered**: none — headless verification is the project's rule (constitution N-V).

## D10 — Docs are updated

**Decision**: note live reload in the Persistence section of `docs/ARCHITECTURE.md` (the file is now polled at runtime
as well as read at startup and written on change) and in the Miscellaneous-panel description of
`docs/EDITOR_DESIGN.md` (the Live reload toggle).

**Rationale**: both docs describe the project file and the panel, and both would otherwise be silent about a
behaviour an author or an agent-operator will observe.
