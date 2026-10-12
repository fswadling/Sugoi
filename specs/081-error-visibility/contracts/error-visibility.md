# Contract: Error Visibility

**Feature**: `specs/081-error-visibility` | **Date**: 2026-10-12

The project exposes no external API; its "interface" is the behaviour an author observes. This file fixes that
contract per requirement, and how each is observed.

## Global guarantees

- **G1.** Making a report is observationally identical on the console to before this feature: the same line is
  written (FR-006, SC-004).
- **G2.** With nothing reported, the application looks exactly as it did before: no indicator, no menu-bar
  item, no extra per-frame work (FR-003, FR-007, SC-005).
- **G3.** The tally never runs out of room: de-duplication bounds the detail, and the total saturates
  (FR-002, edge case).

## Per-requirement contract

| Ref | Guarantee | Observed by |
|-----|-----------|-------------|
| FR-001 | Every report is recorded with a running total | `test/DiagnosticsTests.fs` — a report raises `errorCount` and becomes `lastMessage` |
| FR-002 | Repeats de-duplicate into one entry with a count | `DiagnosticsTests` — `n` reports of the same text give one entry, `Count = n` |
| FR-003 | The total is visible whenever non-zero; nothing is shown at zero | quickstart — a clean run shows no indicator; a report shows the count in the Debug window and the menu bar |
| FR-004 | The distinct reports are inspectable, each with its count, most recent first | `DiagnosticsTests` — `recent()` order and contents |
| FR-005 | Past the threshold the indicator is prominent (focuses itself once) | quickstart — accumulate past the threshold; the Debug window is brought forward with a red read-out |
| FR-006 | The console line is unchanged | `DiagnosticsTests` + inspection — `report` calls `eprintfn` with the same text |
| FR-007 | A frame with no reports adds no per-frame cost | inspection — the display reads scalars and (only when non-zero) a short list; no allocation on the zero path |
| FR-008 | Only existing surfaces are used | inspection — a `beginWindow` body and a `menuItem`; no new window type, dialog or toast |

## Non-goals (explicitly out of contract)

- A modal dialog or a transient "toast" notification system.
- Persisting the reports (the console remains the durable record).
- Changing when or whether the application reports — this feature only makes existing reports visible.
- Any change to the console output's text or format.
