# Specification Quality Checklist: Cue Panel

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-30
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- **Five questions were asked and settled during clarification** (2026-09-30), recorded in **Clarifications** and
  folded into the sections they changed:
  1. **The panel both shows and edits the cue.** The request began as display-only; the author confirmed editing is
     in scope too — so changing a node's kind and numbers, adding and removing nodes, and the cue's own undo
     history (`EDITOR_DESIGN.md` D-010, D-013) are all this feature, not a follow-on.
  2. **The running op is read-only.** The in-flight leaf can be selected and read, never retuned.
  3. **What is executing is protected, and what is ahead is editable.** The containers above the running op cannot
     be retuned, replaced or removed, while an addition is offered on **any** `Sequence`, running or not — an
     addition lands on a queue, whose tail is ahead of the running op. (This revises the first cut, which locked
     the whole running path; the author found that over-restrictive after implementation, since it left the panel
     unable to extend the sequence being watched.)
  4. **A row per container.** `Sequence`, `Parallel` and `Fork` each get a labelled row with children indented —
     the D-013 recursive tree list, and the reference's shape.
  5. **The cue's undo history is per editing session** — dropped when play begins, so a step can never refer to a
     cue that has since advanced.
- **One scope assumption remains for review to veto** (not escalated: the author's stated intent already settles
  it). **The panel is the editor's** — shown while editing, not while playing. Since a cue only advances while
  playing, the panel shows the cue *frozen* at the moment the editor was opened, and watching it move means playing
  and returning. A live overlay during play would be a different surface.
- **Reorder is not in the clarified set.** `EDITOR_DESIGN.md` D-013's tree-list interaction also names reorder;
  the author's clarification named change / add / remove, so reorder is stated out of scope in **Assumptions**
  rather than left as a silent contradiction.
- **What is asserted headlessly, and what is inspected.** The panel is ImGui, and this project's suite is
  headless, so the panel itself is verified by inspection. That is why **FR-007** requires the control's labels,
  child structure, lock state and the addition rule to come from *pure* functions — the split the house already
  uses
  (`DialogueRender`'s panel layout and `PromptRender`'s layout are pure and tested, the drawing is thin over
  them). SC-004 rests on that split; SC-001/SC-002/SC-003/SC-005/SC-006/SC-007/SC-008 are inspection outcomes with
  a stated count (0 rows for a run op, 0 stale rows, 0 copies, 0 changes to the cue, 0 controls that change a
  running op and 1 addition on the sequence holding it, 0 steps across a play).
- **The Key Entities section names files** (`world/Cue.fs`, `editor/CueTree.fs`, `editor/CueEditor.fs`) — this is
  the house style, and specs 043, 045 and 046 do the same. The requirements and success criteria are free of
  implementation detail; the file names are there because two of them are **already reserved** by
  `docs/PROJECT_STRUCTURE.md` and the reuse requirement is about which module owns the rendering.
- **The Origin cites the reference.** `ocaml-game`'s `specs/020-cue-tree-component` is the prior art for one shared
  cue-tree control instead of four near-copies; this project's `docs/EDITOR_DESIGN.md` (workspace table, D-013) and
  `docs/PROJECT_STRUCTURE.md` already anticipate the two modules. That is precedents, not implementation detail.
- **FR-010 / SC-004** name `dotnet build` and `dotnet test`. House convention for "the change is complete and
  nothing regressed" (Principle III's definition of done), matching spec 043's, 045's and 046's SC-006/SC-008.
- **Dependency**: `specs/046-cue-core` — the cue the panel shows, and the state field it lives in. The engine half
  is complete; this feature does not change it.
