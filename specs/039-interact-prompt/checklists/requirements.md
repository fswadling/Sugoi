# Specification Quality Checklist: Interact Prompt

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-27
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

- Items marked incomplete require spec updates before `/speckit-clarify` or `/speckit-plan`.
- No `[NEEDS CLARIFICATION]` markers were needed: the three decisions with alternatives — the look (a bare bottom-centre line, per the `odin-game` reference), when it shows (play mode only), and what it says (the trigger's authored text alone, no key hint) — all have defaults settled by the reference and by what already exists (spec 029's trigger prompt, the trigger Inspector).
- Two judgement calls are recorded in Assumptions rather than left open: the **editor's ImGui read-out of the prompt is removed** so the two do not duplicate, and the **interact key/hint is out of scope** because no interact action or cue engine exists yet.
- The spec names no new technology. It references the project's existing editor toolkit (ImGui) and the game's own 2D layer (spec 038) only as context for *what* must change — the prompt must be the game's UI, not the editor's.
- Planning resolved one conflict the spec had left open: the prompt and the dialogue panel are both anchored to the bottom of the screen and cannot share it. The resolution is that **a dialogue line takes precedence** (FR-012) — the prompt is suppressed while a line is shown and returns when it is dismissed. The edge case, FR-001, the new FR-012, US1's fourth acceptance scenario and the overlay assumption were updated to match.
