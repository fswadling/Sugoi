# Specification Quality Checklist: The Cast — a Roster Hoisted and Bound to Actor Assets

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
- This is a developer-facing spec, in the register of `specs/031-entity-authored-state`: the
  "user" is the author working in the codebase, and two of the three outcomes are structural.
  It names domain concepts (cast member, look, slot, binding, actor asset) and the *build*
  as an outcome, and deliberately does not name the modules, types, or files that
  `/speckit-plan` decides. FR-009 is the one place the spec describes a surface, because the
  panel is the feature's authoring deliverable.
- **The three user stories are not independently shippable in the usual sense**: US1 declares
  the cast, US2 is the authoring that reads it, and US3 is the property the declaration had to
  preserve. They are ordered by what must hold first, and each has its own test.
- **The rejected alternative is recorded in Assumptions**, because it is the shape the request
  literally described (a roster whose cases carry their variants). It is rejected on modelling
  grounds, not taste: a payload-carrying case puts a character's clothes inside her identity,
  so facts about her stop being facts about her. A later reader should not re-open it without
  that argument.
- **The biggest plan decision this spec leaves open** is where the hoisted roster is declared.
  FR-004 fixes the constraint (the engine must not be able to reach the game) and the
  Clarifications record that the engine's own position in the build order may move, since
  nothing consumes it yet; the layer's name and file are the plan's.
- **Two follow-ons are named as out of scope** rather than left implicit: persistence, and
  consuming the lookup (placing a cast member, or recording which character a placed entity
  is). Both are cheap to add afterwards precisely because a slot is expressible as a member
  and a look.
