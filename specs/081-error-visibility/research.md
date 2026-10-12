# Research: Error Visibility

**Feature**: `specs/081-error-visibility` | **Date**: 2026-10-12

No open `NEEDS CLARIFICATION` items. The notes below record the design decisions this plan rests on so
`/speckit-tasks` and implementation do not re-derive them.

## Decision: one process-global tally in `core`, mirroring the existing stats pattern

**Decision.** A new module `core/Diagnostics.fs` holds the tally in module-level mutable state: a saturating
`total` and a bounded, most-recent-first list of de-duplicated reports. It is compiled immediately after
`core/SlotMap.fs`, below every reporter.

**Rationale.** Reports originate in layers that do **not** share a common state object: `world/Assets.fs`'s
`scan` has no `SharedState`, `render/Ui.fs`'s `create` has none, and `Engine.fs`/`Program.fs` report before
any state exists. A single process-global sink is the only place all of them can reach. It is a direct
application of the existing pattern — `Gpu.UploadStats` and `Texture.loadStats` are process-global stats
records read by a display function (`uploadStats()`, `loadStats()`).

**Alternatives considered.**

- *A field on `SharedState`.* Rejected: the startup paths (`scan`, `Ui.create`, `Engine` init) run before
  `SharedState` exists, and several reporters never hold one.
- *Per-screen state.* Rejected: the tally is not a screen's; it must survive screen switches and be readable
  from any screen.
- *A logging framework.* Rejected as disproportionate to the need and to the constitution's Minimalism.

## Decision: `reportf` is the drop-in for `eprintfn`; the console line is unchanged

**Decision.** The tally exposes `report (message: string)` — which writes the **same** console line via
`eprintfn` and then records — and `reportf`, a `Printf.kprintf`-based formatter whose call sites read exactly
as the `eprintfn` calls they replace (e.g. `Diagnostics.reportf "cue: %s(%s) performs nothing: %s" op actor
reason`). Every existing report site is a mechanical rename.

**Rationale.** FR-006 requires the console output to be unchanged, and a formatter that mirrors `eprintfn`
keeps the ~20 edits trivial and diff-reviewable rather than converting each to `sprintf` + `report`.

**Alternatives considered.** `sprintf` at each call site then `report` — rejected: a larger, noisier diff. A
wrapping `eprintfn` that also records — rejected: `eprintfn` is `FSharp.Core`'s and cannot be shadowed safely
across modules.

## Decision: de-duplicate by exact message; bound the list; saturate the total

**Decision.** A report whose text already exists increments that entry's `Count`; otherwise it is inserted at
the front, and the oldest entry is dropped beyond a small cap (32). The running total saturates at a large
constant (1,000,000) so a runaway report loop cannot wrap it.

**Rationale.** Reporting repeats is the *normal* case here (a held key, a re-fired trigger), so
de-duplication is what stops the detail view becoming a flood (FR-002). The bound keeps memory constant; the
saturation keeps the count monotone (SC-002, edge case "an extremely large number of reports").

**Alternatives considered.** Append every report — rejected: unbounded growth and an unreadable list.
De-duplicate by a hashed key — rejected: exact text is the natural key and cannot collide.

## Decision: reuse the Debug window body and the menu bar — do **not** rename the Debug window

**Decision.** `screen/DebugOverlay.fs` shows the total and the de-duplicated detail in the Debug window's
**body** (a red read-out when non-zero, hidden when zero). `editor/Editor.fs` adds an always-visible
menu-bar item when the total is non-zero, which focuses the Debug window. The Debug window's **name stays
`"Debug"`**.

**Rationale.** Reusing existing surfaces is the spec's explicit constraint (FR-008). Crucially, an ImGui
window's name **is** its identity: folding the count into the title (`"Debug (3 errors)"`) would hand ImGui a
new ID, so the window would be treated as a fresh window — losing its docked position in `imgui.ini` — and
`setWindowFocus "Debug"` would no longer match it. Keeping the name constant avoids both. The menu-bar item
makes the indicator visible even when the Debug panel is collapsed or closed (the "collapsed or closed" edge
case).

**Alternatives considered.**

- *A toast/notification system.* Rejected by the spec (FR-008) and disproportionate.
- *A modal dialog.* Rejected: blocking; wrong for routine, repeated reports.
- *The window title carrying the count.* Rejected for the identity reason above.
- *A draw-list overlay.* Rejected: more bindings and manual sizing for no gain over the Debug panel.

## Decision: escalation is a one-time window focus past a fixed threshold

**Decision.** Once the total reaches `loudThreshold` (a small constant, 10), DebugOverlay focuses the Debug
window **once** (guarded by a module flag that resets when the tally is no longer loud), and the read-out is
drawn in red. Below the threshold the indicator is an ordinary line.

**Rationale.** FR-005 asks the indicator to "draw attention to itself" once errors accumulate. A single focus
is the lightest mechanism that makes the panel unhideable; reusing the already-bound `setWindowFocus` adds no
binding.

**Alternatives considered.** A red banner rectangle — rejected: new drawing code and bindings for a marginal
gain. Re-focusing every frame — rejected: it would fight the author's own window focus.

## Decision: style colour via the non-variadic `ImU32` push/pop

**Decision.** Add two bindings — `igPushStyleColor_U32(int idx, ImU32 col)` and `igPopStyleColor(int count)` —
and push colour index `0` (`ImGuiCol_Text`) with a soft red packed as `IM_COL32` around the read-out's text.

**Rationale.** Both symbols are already exported by the vendored `libcimgui.so` and are **non-variadic**, so
they P/Invoke cleanly. The colour is the only new visual, and it is what makes the indicator read as an error
rather than a statistic.

**Alternatives considered.** `igTextColored` — rejected: it is variadic (`(ImVec4, fmt, ...)`), which is
awkward and unsafe to P/Invoke. A `cimgui_*` C shim — rejected: not needed for a non-variadic call.
