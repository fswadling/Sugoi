# Specification Quality Checklist: Spawning a Cast Member from the Cast Panel

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-20
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
- Developer-facing, in the register of `specs/031-entity-authored-state` and
  `specs/032-cast-binding`: the "user" is the author working in the codebase. It names domain
  concepts (member, slot, binding, spawn control, placed actor) and deliberately does not name the
  modules or widgets `/speckit-plan` decides.
- **One assumption is a real choice, and it is flagged as such**: the control goes on the slot
  rather than on the member's row. The request said "next to the cast roster entries", and a roster
  entry is a member; but a member may hold several looks, each with its own asset, so a per-member
  control needs an invented rule for which one it spawns. The two readings coincide while a member
  has one look bound, which is today's case, so the difference is only visible once a second look is
  bound. If the per-member reading is the intended one, overturning it is a small change and
  `Assumptions` is the paragraph to rewrite.
- **The feature is deliberately narrow.** `specs/032-cast-binding` deferred two things — placing a
  cast member, and recording which character a placed entity is. This takes the first and leaves
  the second, which is why FR-009 exists: the actor gets a name and nothing else.
- **The two undo behaviours sit side by side on purpose.** Spawning is a scene edit and is
  undoable; assigning the model is not (`specs/032-cast-binding` FR-011). A reader of the panel sees
  one surface behaving two ways, which is why the spec states the reason rather than only the rule.
