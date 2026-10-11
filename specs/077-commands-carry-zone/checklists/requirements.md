# Specification Quality Checklist: Commands Carry the Zone They Act On

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-11
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

- Items marked incomplete require spec updates before `/speckit-clarify` or `/speckit-plan`
- Both open decisions were answered (Session 2026-10-11): keep `ZoneMove` undoable (FR-008), and remove the
  temporary active-place switch in a zone delete's undo (FR-014). No `[NEEDS CLARIFICATION]` markers remain.
- House style: this project's specs name the modules and records involved (as `specs/070`/`specs/076` do), so
  `Command`, `Batch`, `History`, `Place` and `ZoneMove` and the docs to refresh are named as the domain objects of
  the change, not as an implementation plan. The HOW (signatures, compile order, which files) is `/speckit-plan`'s.
