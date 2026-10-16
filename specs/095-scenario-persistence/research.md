# Research: Automatic Persistence of the Scenario Library

## D1 — Prime's symbolic syntax, and what it does and does not round-trip

**Decision**: persist with Prime's symbolic-expression syntax — `SymbolicOperators.scstring` to write, `scvalue` to
read, `PrettyPrinter.prettyPrintValue` for a multi-line, comment-friendly rendering. Records carry
`[<SymbolicExpansion>]` (named fields). Nothing else is used: no JSON, no binary, no separate schema.

**Rationale**: it is the project's convention for human-authored data (`AGENTS.md`, every `assets/**/*.manifest`),
it maps F# records and unions directly, and it needs no DTO-shaped intermediate beyond the field flattening below.
Measured against the referenced **Prime 11.5.2**:

| Type | `scstring` → `scvalue` |
|------|------------------------|
| `string`/`int`/`float32`/`bool` | round-trips |
| `option`, list, array, `Map<_,_>` | round-trips |
| `FDeque<_>` (a cue's `rest`) | round-trips (as a list) |
| union types (the cue) | round-trips |
| records, incl. `[<SymbolicExpansion>]` named fields, nested / arrays / maps | round-trips |
| `OMap<_,_>` (the registry) | **unsupported** — `NullReferenceException` |
| `System.Numerics.Vector3` / `Quaternion` | **no** — written as an opaque `ToString` atom (`"<1, 2, 3>"`), unreadable |
| a custom `TypeConverter` for `Vector3` (`assignTypeConverter`) | **not honoured** by `SymbolicConverter` |

So the only two things the converter cannot carry are the ordered map and the `System.Numerics` value types; both are
handled in the record tree (D2). `prettyPrintValue value PrettyPrinter.defaultPrinter` was verified to produce
multi-line output that `scvalue` reads back.

**Alternatives considered**: JSON with a DTO (the reference lineage, and what `docs/ROADMAP.md` phase 15 currently
names) — rejected: it is not the project's convention, it cannot carry the cue's union or `FDeque` without bespoke
converters, and it needs a parallel schema; a custom `TypeConverter` for `Vector3` — rejected because Prime ignores
it; binary — rejected: the file is meant to be human-readable and hand-editable.

## D2 — A serializable record tree that mirrors the authored types

**Decision**: `world/Project.fs` defines records that mirror the authored types field for field, with two
flattenings: `Vector3`/`Quaternion` become three/four `float32` scalar fields, and the library's `OMap` becomes an
**ordered list** of `{ Name; Contents : ContentsDto option }` entries (rebuilt in order with `OMap.ofSeq1` on load).
The cue (`Cue.Cue`) and the per-zone `Map<string, ScenarioZone>` are used **directly** — they round-trip already.
Every record is `[<SymbolicExpansion>]`.

**Rationale**: the authored records cannot be annotated in place. They are hot-path `[<Struct>]`s with `mutable`
fields (e.g. `Entity`, `Transform`), they carry `Vector3`/`Quaternion` (which still would not encode even if
annotated), and their shape is the runtime model, not the file format — the architecture's whole reason for a DTO.
Mirroring at the boundary keeps the disk format free to move without touching the runtime types. Reusing the cue and
the `Map` directly keeps the tree as small as the converter's reach allows.

**Alternatives considered**: annotating the runtime records directly (couples the file format to the runtime model,
and does not solve `Vector3`); serialising `Vector3` as a string and parsing it back (a bespoke converter that Prime
does not honour on read, and it would be custom on one side); a per-scenario file tree — rejected (one ordered file
is what the spec asks, and it preserves the authored order in one place).

## D3 — The module is `world/Project.fs`, compiled after `world/ScenarioRegistry.fs`

**Decision**: the whole feature lives in one new module, `world/Project.fs`, inserted into the `.fsproj` list
directly after `world/ScenarioRegistry.fs`.

**Rationale**: the record tree names the entire authored tree — `Registry`, `ScenarioContents`, `ScenarioZone`,
`ZoneStaging`/`ZoneTemplate`, `Entity`/`ActorState`/`PropState`/`TriggerState`, `Cue`, and `Texture`/`Skydome`
settings — all of which are defined by that point, and none of which is defined earlier. It sits in `world/` (not
`state/`) because it needs **no** `GameplayState`: `persist` takes the library by value, so the module does not
depend on the state layer at all.

**Alternatives considered**: `state/Project.fs` (would need to sit *after* `GameplayState.fs` to see the state, but
then `ScreenManager`/`GameplayScreen` can still call it — the only gain would be reaching into `state.Scenarios`
itself, which the by-value entry points deliberately avoid); naming the module `ProjectFile` (the ROADMAP already
names `world/Project.fs`).

## D4 — One file, `project.manifest`, at the working-directory root

**Decision**: a single file named `project.manifest`, resolved relative to the current working directory (the same
base `SharedState.create` scans `"assets"` against). It is not under `assets/` and is not discovered by the asset
scan (which walks `actors/`/`props/`/`environments/`/`audio/`).

**Rationale**: the project file is project state, not an asset, and the author expects to find it beside the game
and the `assets` folder. The `.manifest` extension matches the project's s-syntax files; because it is not inside
`assets/`, no scan can mistake it for an asset.

**Alternatives considered**: `assets/project.manifest` (mixes state into the asset tree, and the scan would have to
learn to skip it); `.scm` (Prime's own convention, but unfamiliar here); a directory of per-scenario files (loses
the single ordered document the spec asks for).

## D5 — Change-driven writes: compare the library instance, once per frame

**Decision**: `Project.persist` writes only when the library passed to it is not the **same instance** as the one it
last wrote (`obj.ReferenceEquals`), and remembers the new instance after writing. It is called at the end of
`GameplayScreen.update`.

**Rationale**: the library is an immutable value and **every** change produces a new instance — the pure
`ScenarioRegistry` operations and the undo/redo snapshots all yield a new `OMap`. So one reference comparison
detects *all* changes, including an undo that restores a prior library (which is a different instance from the one
just written). This is the mechanism that makes persistence follow the **value** rather than a control, satisfying
FR-005 and the `specs/083` delete-then-undo case: had persistence hooked only the Register control, a scenario
restored by Ctrl+Z would be lost on the next launch. It is also why no timer and no dirty flag are needed: the
comparison is O(1) and a write happens exactly once per change. (Verified: creating two structurally equal `OMap`s
compares **unequal** by `=`, i.e. Prime's map equality is reference-based, so `ReferenceEquals` is the honest test —
and structural equality would have been wrong. `OMap.add` was verified to return a new instance.)

**Alternatives considered**: a time-based debounce (rejected by the author — the changes are discrete, not
continuous, so batching them is unjustified); a dirty flag set in the state-level runner (`state/Undo.fs`) — rejected
because it couples the undo runner to the persistence module and would fire in the headless suite, where a flag set
by `Undo.commit` would leak across tests as process-global state; hooking the Register control only — rejected
(misses Add/Clear/Delete and every undo/redo).

## D6 — Load at startup in `ScreenManager.init`; write in `GameplayScreen.update`

**Decision**: `screen/ScreenManager.fs`'s `init` calls `Project.load ()` once and assigns the result to
`gameplay.Scenarios` before the loop begins. `screen/GameplayScreen.fs`'s `update` calls
`Project.persist state.Scenarios` at the end of the frame.

**Rationale**: `GameplayState.create` is called by **every** headless fixture (`newState ()` in ~12 test files), so a
disk read there would make the entire suite depend on the working directory and on whatever `project.manifest`
happens to exist. `ScreenManager.init` and `GameplayScreen.update` are the production seams the suite never calls
(verified: no test names `ScreenManager`, `GameplayScreen` or `Editor.update`). Loading once at startup also gives
"present from the first frame" (FR-007), and writing at end of update means an edit made by `Editor.update` in the
same frame is written in that frame.

**Alternatives considered**: loading in `GameplayState.create` (breaks the headless suite); loading in
`GameplayScreen.activate` (runs each time the gameplay screen is re-entered — repeated IO and a re-load that could
clobber in-session edits); writing from `state/Undo.fs` (fires in tests, and couples disk IO to undo).

## D7 — Atomic writes (`.tmp` + rename)

**Decision**: `saveTo path registry` writes the encoded text to `path + ".tmp"`, then moves it over `path` with
overwrite (`File.Move(tmp, path, true)`). A failure encoding or writing is reported and the write is skipped, so the
previous good file is left intact.

**Rationale**: the rule `docs/ARCHITECTURE.md` already states for saves; it means a crash or power loss mid-write
cannot truncate the project file (FR-006, SC-006). The encode step is pure and runs before any file is touched, so a
cue that could not be encoded (D10) never produces a partial file.

**Alternatives considered**: `File.WriteAllText` straight to the target (a truncated file on interruption); writing
the temp in a system temp dir and moving across volumes (a cross-device rename can fail).

## D8 — A missing file is empty; a damaged file is reported and leaves the library empty

**Decision**: `loadFrom` returns the empty library when the file is **absent** — no report, no write. When the file
**exists but cannot be read or decoded**, or its **version is not recognised**, it reports via
`Diagnostics.reportf` and returns the empty library, leaving the file **untouched** on disk (it is rewritten only by
the author's next real change).

**Rationale**: a first run has no file, which is the expected path, not an error (FR-008). A damaged file is
user-reachable (hand-edited, truncated, newer version), so it is reported — visible in the debug overlay and the
console (`specs/081`) — and the session still starts, with an empty library, rather than crashing (FR-009). Leaving
the file in place means the author does not silently lose data they might want to salvage; because the library
starts empty and no write occurs until a change, startup never overwrites it.

**Alternatives considered**: treating a damaged file as a hard failure (fails the boot, contrary to the
survivability the spec asks for); silently emptying without a report (hides the problem — Fail Loudly); renaming the
damaged file to `.bak` (a file-system side effect at open time the author did not ask for; deferred, and easy to add
later).

## D9 — A format version in the file

**Decision**: the root record carries a `Version : int`, written as the current literal; on load, an unrecognised
version takes the damaged-file path (FR-010).

**Rationale**: the asset manifests already carry a version and check it (`world/assets/ActorAsset.fs`); the same
guard lets the format change without silently mis-reading an old file.

**Alternatives considered**: no version (an old file would be mis-parsed as the new shape).

## D10 — The cue is authored; its run forms do not encode

**Decision**: the persisted cue (`ScenarioContents.Cue` and each `TriggerState.Cue`) is the authored union and is
carried directly by the tree. A cue holding a run-form op (a `…State` case carrying a `Handle`, a `Vector3[]` or a
`Quaternion`) is **not** encodable; encoding such a library is reported and the write skipped (D7).

**Rationale**: a scenario's cue is authored data — it is captured while editing, and `Undo.pruneCueSteps` drops cue
steps on Play — so the normal case round-trips directly, with no bespoke cue DTO. The `…State` cases exist for the
*run* (spec 046) and carry a live slot handle and engine-space vectors that have no authored meaning, so refusing
them (rather than inventing an on-disk form for a running cue) is the Fail-Loudly choice; reporting and skipping the
write keeps the last good file. This is an edge, not the norm: the invariant is that a registered cue is authored.

**Alternatives considered**: a bespoke authored-cue DTO union mirroring only the non-`…State` cases — rejected as
large (the cue has ~50 cases) and duplicative, when the authored cue already round-trips; persisting run forms
faithfully — rejected (a `Handle` and run-space vectors are meaningless across a restart).

## D11 — Verification is headless

**Decision**: `test/ProjectTests.fs` asserts: (a) a library built through the real `capture` path round-trips
`encode |> decode` **equal, scenario for scenario**; (b) a named-but-unregistered scenario, a chain-ending waypoint
(`Next = None`), and an empty cue survive; (c) `loadFrom` on an absent path yields the empty library and writes
nothing; (d) `loadFrom` on a malformed file and on an unknown version reports, yields the empty library, and leaves
the file byte-for-byte unchanged; (e) `saveTo` is atomic (the temp file is gone and the target is readable); (f)
`persist` writes nothing when handed the same instance twice and writes when handed a new one. Temp files under
`Path.GetTempPath()`.

**Rationale**: the tree, the codec and the file handling are plain data and `System.IO`, so they are assertable with
no device (constitution N-V; `test/AssetsTests.fs` and `test/AudioTests.fs` are the precedent for temp-dir
fixtures).

**Alternatives considered**: none — headless verification is the project's rule.

## D12 — Docs are corrected

**Decision**: update the Persistence section of `docs/ARCHITECTURE.md` (it currently states "`Project_DTO`/… structs
with JSON tags" and a `project_save`/`project_load` pair) and `docs/ROADMAP.md` phase 15 (its `world/Project.fs` /
`System.Text.Json` / `project.json` deliverables), to state the s-syntax `project.manifest`, the library-only scope,
and the automatic write-on-change. `specs/083`'s FR-016 ("the library MUST NOT be persisted") is noted as superseded.

**Rationale**: the docs are the source of truth and currently describe the reference's JSON persistence and a
persisted cast and zone-registry, both since retired (`specs/074`, `specs/079`). Correcting them keeps the "phase 15"
description honest about what now exists.

## D13 — The mid-run cue is refused at Register, and the control says why

**Decision**: Register refuses to file a cue that holds a **run-form op** (a `…State` case), detected with the
predicate the editor already has — `CueTree.executing` (`editor/CueTree.fs`, spec 047). While that holds, the
**Register control is disabled** with a tooltip ("the cue is still running — stop play first"). The write path keeps
its defensive report-and-skip (D10) in case the invariant is ever violated.

**Rationale**: a `…State` op is run *progress*, not authoring — it carries a live `SlotMap.Handle` and engine-space
`Vector3`/`Quaternion`, which (a) have no meaning across a restart and (b) the serializer cannot round-trip (D1).
Filing one would break the round trip the feature exists for, and under a fail-at-save design (option B) a single
such scenario would **block persistence of the whole library**. Refusing at the point of capture keeps the library
always authored and always writable, and it **reuses a predicate the editor already relies on and asserts** rather
than inventing a second notion of "in progress". It is reachable: stopping play does **not** reset the live cue
(`editor/Editor.fs` prunes the history, not the cue), so "play partway → stop → Register" would otherwise capture a
mid-run cue. A disabled control with an explanation (rather than a silent refusal) is the pattern the panel already
uses for Add/Clear/Fork.

**Tooltip binding**: the pinned native library (Dear ImGui 1.92.9b, `native/libcimgui.so`) already exports
`igBeginTooltip`/`igEndTooltip`/`igSetTooltip`/`igBeginItemTooltip`/`igSetItemTooltip` and
`ImGuiHoveredFlags_AllowWhenDisabled` (`1 << 10`); `render/ImGui.fs` declares no tooltip call yet, so **one extern is
added**, bound like the existing variadic `igText` (a literal format string). The non-variadic
`igSetTooltip0`/`igSetItemTooltip0` declared in the vendored header are **not** exported by the compiled `.so`, so the
variadic form is used. `SetItemTooltip` is preferred over the explicit `IsItemHovered` + `SetTooltip` pair because its
default `ForTooltip` hover flags already allow a disabled item.

**Alternatives considered**: **fail at save** and **drop at save** (options B/C) — leave the problem to the write,
and B blocks every other change; **snapshot and restore the cue around Play** (option D) — fixes the root cause but is
a play-mode change beyond persistence; a **silent refusal** with no disabled affordance — leaves the author guessing
why Register did nothing.
