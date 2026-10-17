# Specification Quality Checklist: Dialogue Endings

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-17
**Feature**: [spec.md](../spec.md)

## Content Quality

- [X] No implementation details (languages, frameworks, APIs)
- [X] Focused on user value and business needs
- [X] Written for non-technical stakeholders
- [X] All mandatory sections completed

## Requirement Completeness

- [X] No [NEEDS CLARIFICATION] markers remain
- [X] Requirements are testable and unambiguous
- [X] Success criteria are measurable
- [X] Success criteria are technology-agnostic (no implementation details)
- [X] All acceptance scenarios are defined
- [X] Edge cases are identified
- [X] Scope is clearly bounded
- [X] Dependencies and assumptions identified

## Feature Readiness

- [X] All functional requirements have clear acceptance criteria
- [X] User scenarios cover primary flows
- [X] Feature meets measurable outcomes defined in Success Criteria
- [X] No implementation details leak into specification

## Notes

- The cue operation names (`Exposit`, `ExpositWait`, `CloseExposition`) and the panel names are the
  project's author-facing vocabulary, recorded so the plan can map them to the cue module and editor
  surfaces. They are not technology choices.
- The specification fixes the model agreed in conversation: `ByPlayer` and `ByTimeout` are data-free
  members of a runtime ending `Set`; the timeout's duration is a `float voption` on the dialogue line
  rather than part of the condition's identity; cue closing is a universal capability and is not a
  member of the set. The empty set means the cue owns the line's end.
- A cue-managed (empty-set) line is defined explicitly in Ending Semantics: only a cue ends it. It does
  not imply that the cue waits; a non-waiting cue-managed line is the ordinary case where the cue
  continues with the dialogue up, and a waiting cue-managed line requires a close or replacement from
  another branch or cue.
- The one intentional behaviour change is recorded in FR-020 and the migration assumption: existing
  display ops decode as the empty set (cue-managed) and must add a close op where a line previously
  relied on the player clearing it.
- No clarifications were needed: the feature scope, the three ending behaviours, their combination, the
  waiting/non-waiting distinction, the panel surfaces, the play-time clock, and the migration are all
  settled.
