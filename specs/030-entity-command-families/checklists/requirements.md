# Specification Quality Checklist: Entity Command Families

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
- This is a developer-facing spec for an editor/model refactor, matching the
  register of `specs/028-waypoint-chains` and `specs/029-triggers`: it names
  editor surfaces (Outliner, Inspector, viewport) and states build-level outcomes
  (SC-004) because the project's "user" here is the developer authoring in the
  editor. It deliberately does not name modules, types, or command shapes — those
  belong to `/speckit-plan`.
- **Scope of the change is model-wide but behaviour-preserving**, except where
  behaviour is currently lossy. The three losses that motivated it are named in
  FR-003 (the actor collider overlay, currently reset by a restore), FR-004
  (trigger properties, currently round-tripping via a bespoke path), and FR-005
  (the waypoint link, currently stored in a zone-level table). Everything else is
  explicitly a non-regression requirement (FR-009, FR-012).
- **FR-010 is a constraint, not a capability.** It records the deliberate
  architecture decision to keep one storage per entity kind, so this refactor
  cannot quietly become "introduce a single tagged entity type". It is verifiable
  by inspection and by SC-006.
- **FR-006 is the only requirement with a build-level acceptance.** It is the
  mechanism that keeps the invariant from decaying, and it is testable: add a
  property without handling it and the build fails.
- Reference points for the design phase: `specs/028-waypoint-chains/research.md`
  Decision 1 and `specs/029-triggers` for the current workarounds; `EDITOR_DESIGN.md`
  D-011 ("commands store values", "a generic `Field` command") and principle 4
  ("Undo everywhere") for the documented intent the code drifted from.
