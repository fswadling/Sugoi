# Specification Quality Checklist: Dialogue Display

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-26
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
- Three decisions are settled in conversation and recorded in Assumptions rather than left open: **no reveal and no advance timer** (a line appears whole and stays until dismissed), **the portrait is derived from *(character, expression)*** and never named by hand, and **the state belongs to the screen, not to a zone**.
- One judgement call is recorded for `/speckit-clarify` to challenge: **FR-006 makes a gap in a character's portrait set a load failure**, on the reading that the state may name any expression for any character, so a partial set is a broken asset. The alternative — a character may declare only the expressions it uses, and a pair outside the set draws no portrait — is the reference's behaviour and is deliberately not chosen, because it makes a missing portrait silent.
- Two judgement calls the plan must settle rather than the spec, and which the spec deliberately leaves open: **what hosts the authoring surface in FR-009** (the Inspector is selection-driven and this state is not; a panel of its own is the likely answer), and **which control dismisses a line** (the reference uses SPACE; the key must not collide with the editor or with the player's movement keys).
- FR-004's "laid out against the current viewport size" is a real constraint, not decoration: the reference draws a fixed-height panel at a fixed offset and would clip on a resize.
- FR-005's prohibition is the point of the feature's second story: no field and no authoring surface may name a portrait, so the *(character, expression)* pair is the only handle on it, which is also what keeps the cast free of assets.
- The spec names no technology. `Origin` names the reference engine and its behaviour, which is the template this port follows; nothing in the requirements prescribes how the drawing, the font or the portraits are implemented.
- `/speckit-analyze` has not been run yet; the first pass happens after `/speckit-plan`.
