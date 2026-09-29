# Specification Quality Checklist: Actor Outline

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-29
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
- No `[NEEDS CLARIFICATION]` markers were needed. The decisions with alternatives all have defaults
  settled by the reference and by research:
  - **The method is the inverted hull**, matching the `odin-game` plan's stated "inverted-hull second
    pass" and Godot's "Grow". The post-process (Sobel) family is excluded by FR-003: this engine has no
    normal G-buffer. This is a scope decision, not an open question.
  - **Silhouette-only** this phase (interior crease lines are the post-process family's job); recorded
    in Assumptions.
  - **Colour and width are constants**, matching spec 040's parameter policy.
- Two judgement calls are recorded in Assumptions rather than left open: **the extrusion direction is
  averaged across coincident vertices** (forced by the port's own asset — Akane is ~79% hard-edged,
  hair ~100%), and **width is a fixed world-space distance**, so the line shrinks with distance
  exactly as the actor does. The clarify session (2026-09-29) confirmed cutout coverage (hair/eyes
  outlined, masked) and settled the width behaviour.
- The one asset-driven finding that shapes the spec is the **hard-edge data**: measured from
  `assets/actors/Akane/Akane.iqm` (20 005 vertices, 4 226 coincident positions, 79.4% hard-edged;
  hair mesh 2 694/2 696). It turns "the hull may gap" from a theoretical caveat into the feature's
  central requirement (FR-005, SC-003).
- The spec names no *new* technology: the outline is a second pass over geometry the cel pipeline
  already poses, using culling and depth the engine already exposes (SDL_gpu front-face culling and
  depth bias).
