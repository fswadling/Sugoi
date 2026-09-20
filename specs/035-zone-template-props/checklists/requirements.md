# Specification Quality Checklist: A Registered Zone's Placed Props

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
- Developer-facing, in the register of `specs/031-entity-authored-state`, `specs/032-cast-binding`,
  `specs/033-cast-spawn` and `specs/034-zone-registry`: the "user" is the author working in the
  codebase. It names domain concepts (entry, placed prop, part, the zone's previous contents) and
  deliberately does not name the modules or mechanisms `/speckit-plan` decides.
- **This feature is the deferral `specs/034-zone-registry` recorded, taken in.** Its FR-015 excluded
  props explicitly and its research Decision 13 said how they would arrive — as siblings of the parts —
  and `docs/ARCHITECTURE.md`'s `Zone_Registry` entry already has the name for them (`prop_template`).
  So the shape here is inherited rather than invented, and FR-006 keeps the rest of that deferral
  (waypoints, triggers, splat maps) exactly where it was.
- **Three decisions came from the conversation and are the spec's spine**, which is why each is argued
  in `Assumptions` rather than only stated. The props are **replaced** and not added to, on the
  author's own reasoning that the environment asset is already replaced and the props belong to the
  same piece of world. An instantiation is **one** undo step, which is the requirement that will shape
  the design more than anything else here: nothing in the command model today expresses "one gesture,
  several mutations" except a pair of commands built for one case, so the plan has a real choice to
  make and this is the requirement it answers to. And an **environment is still required** to register,
  so `AssetId` stays non-optional and a props-only place stays out.
- **FR-005 is the one requirement with no obvious cheap implementation**, and it is deliberately stated
  as an outcome rather than a mechanism: a single reversal that restores settings, parts and props.
  Whether that is a compound command, a batch, or something else is `/speckit-plan`'s question; what it
  cannot do is split the gesture into a step per prop, because that is the reading this requirement
  exists to forbid.
- **One edge case corrects a claim it would have been easy to make wrongly**: a zone *can* hold props
  with no environment — that is a scene an author can build — but such a zone cannot be *registered*.
  The spec separates the two, because a reader who conflates them will think this feature changes what
  a zone is, where it only changes what can be filed away.
- **Two edge cases describe identity rather than data**, and they are the ones most likely to be got
  wrong in an implementation: undoing an instantiation restores the previous props at *their old
  identities* (not as copies at new slots), and the order props come back in is not part of the record.
  Both are properties of the reversal, not of the entry.
