# Specification Quality Checklist: Entity Authored State

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
- This is a developer-facing spec for a behaviour-preserving refactor, in the register
  of `specs/030-entity-command-families`: the "user" is the developer working in the
  codebase, and the outcomes are structural. It names domain concepts (authored value,
  derived resource, runtime state, entity kind) and the *build* as an outcome, and it
  deliberately does not name the types, modules, or accessors that `/speckit-plan`
  decides. FR-001's word "record" is the one implementation-ish term retained, because
  the feature is about how authored state is expressed.
- **The three user stories are not independently shippable in the usual sense**: US1's
  guarantee is delivered by the reshape itself, US2 is the property that reshape has,
  and US3 is the machinery it deletes. They are ordered by what must hold first, and
  each has its own test.
- **Scope is deliberately narrower than 030.** Only kinds carrying derived or runtime
  state are split, and the environment's part teardown is explicitly kept (FR-011):
  that teardown is a lifecycle behaviour, not a record-safety one. Naming that
  distinction is the main thing this spec owes the planner, since the codebase uses a
  superficially similar "clear the derived fields" idiom in both places.
- **FR-007 is the whole risk surface.** This touches `world/Entity.fs`, `world/Zone.fs`,
  `state/Command.fs`, and every reader of authored properties; the guarantee is that
  nothing observable changes, so the 030 behaviour tests must pass unmodified
  (SC-003, SC-006).
- **The rejected alternative is recorded in Assumptions**, with the measured churn, so
  a later reader does not re-open it without the numbers: a zone-level split was about
  half the churn but would have made resources reachable by an ungenerationed index.
