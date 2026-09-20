# Specification Quality Checklist: The Zone Registry

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
- Developer-facing, in the register of `specs/031-entity-authored-state`,
  `specs/032-cast-binding` and `specs/033-cast-spawn`: the "user" is the author working in the
  codebase. It names domain concepts (place, zone vocabulary, zone entry, settings, part state) and
  deliberately does not name the modules or widgets `/speckit-plan` decides.
- **Two names were settled in conversation rather than here.** The vocabulary's union is a `Place`,
  and what a registered place holds is a `ZoneTemplate` — the docs' word for the registry's entries
  (`docs/ARCHITECTURE.md`, `docs/ROADMAP.md` Phase 12), kept because it is already the project's
  term for this. The variant unions are the author's to name per place (`Period`, `Light`, …).
- **One assumption is a real choice, and it is flagged as such**: registration is the only way an
  entry comes into being, with no separate control that binds an asset to a place with no parts. The
  cast panel has an assigning combo because nothing else could put an asset in a slot; a zone's
  asset arrives with the environment the author loads, so the capture carries it. A reader expecting
  the cast panel's shape will look for the combo and find a register control instead, which is why
  `Assumptions` argues it rather than only stating it. If pre-assigning an asset turns out to be
  wanted, it is additive.
- **The second real choice is what a part records.** Storing each part's own piece of the asset
  rather than its position in the asset's list is what lets an entry represent a part the author
  deleted; the price is that an asset re-exported with its geometry rearranged invalidates what a
  place already registered says about its parts. The spec states the price in `Assumptions` and the
  edge case, rather than leaving it to be discovered.
- **The feature is deliberately asymmetric about undo**, and says so twice: registering is a direct
  write with no stack, instantiating is an ordinary scene edit. This mirrors the cast panel, where
  assigning is a direct write and placing is undoable — one surface, two behaviours, on purpose.
- **The scope boundary is the documented one.** `docs/ARCHITECTURE.md`'s `Zone_Registry` entry lists
  an environment asset, settings, prop templates and splat maps; this feature takes the first two
  and the environment's parts, and records the rest as deferred siblings (FR-015) rather than
  silently narrowing the entry's shape.
- **`docs/EDITOR_DESIGN.md` D-011 needs a correction when this lands**: it names the zone registry
  among the surfaces that will want a project-scoped undo stack. This feature answers that by not
  giving it one, for D-022's reason, so the line becomes an override rather than an open gap.
