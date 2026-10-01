# Specification Quality Checklist: Cue Core

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-30
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

- **The model was revised after the first draft.** The spec was first written with an authored cue kept beside a
  separate run of frames; the owner's ruling replaced that with the model where **the cue *is* its run** (the
  `My Game 3` lineage), so the requirements and criteria below were re-derived rather than merely edited. What
  moved: FR-009 is now the **fork** (a cue runs *beside* what is running) instead of a join; FR-013 says setting
  the cue's state *is* the beginning (there is no separate run to release); the second story forks rather than
  joins; and the old fourth story (deriving a "live view") is gone, because the screen's cue *is* the view. The
  two retired arrangements — the reference's template/runtime split with its slot map, and an authored cue beside
  a run of frames — are recorded in **Origin** and measured in `docs/CUE_MODEL_DECISION.md`.
- The **Origin** section cites the design record (`docs/CUE_MODEL_DECISION.md`) and this feature's own suite as the
  runnable evidence (`test/CueTests.fs`; the scratch sketch that first demonstrated the model was removed once the
  suite asserted it). That is this project's house style for a feature that corrects a documented design
  decision — specs 043 and 045 do the same — and it is the *point* here: the feature exists because each
  documented arrangement was measured and found not to pay. The requirements and success criteria are free of
  implementation detail; the Key Entities section names files the way 045's does.
- **FR-012 / SC-004** measure allocation, as a project-level outcome rather than a design constraint: the
  constitution makes minimising GC pressure a principle of the game loop (V), and the feature's central claim —
  that a waiting cue costs nothing — is only meaningful when measured. The suite measures 0 bytes per idle tick
  (flat and nested) and 0 bytes on `Fin`, at ~110 bytes per completed op. It names no framework or API.
- **FR-018 / SC-008** name `dotnet build` and `dotnet test`. House convention for "the change is complete and
  nothing regressed" (Principle III's definition of done), matching spec 043's and 045's SC-006.
- **No clarifications were raised** (0 `[NEEDS CLARIFICATION]` markers). The decisions taken as informed guesses
  are recorded in **Assumptions** so review can veto them cheaply:
  1. **the editor is a separate feature** — the Cue panel and the cue's own undo history are deferred with a
     stated trigger;
  2. **cues are authored in code** in this feature — the tests are the authoring surface, which is what makes the
     engine half independently testable;
  3. **the first cut of the op vocabulary**, stated as *shapes* in FR-017 and named concretely in Assumptions;
  4. **there is no join and no splice** — the mechanism by which a cue arrives at a running one is the **fork**;
  5. **the documentation the feature corrects moves with it** — `ARCHITECTURE.md`'s diagram and Cue section,
     `EDITOR_DESIGN.md` D-010, `ROADMAP.md` phase 9, `PROJECT_STRUCTURE.md`'s module list.
- **A measured route to the shape.** The scratch sketch that first measured the model (`cue_machine_sketch.fsx`,
  since removed) recorded not only the numbers the criteria rest on
  but the counter-example that fixed the type: drawing the running op *inside* a sequence's deque cost 72 bytes
  per idle tick at every level, because `FDeque.uncons` allocates the tail on the *idle* path. That is why a
  sequence holds its op in flight out of the deque (`Sequence(current, rest)`), and it is recorded so it is not
  re-introduced by a later "simplification".
- **`/speckit-clarify` ran and raised no questions** (the structured scan found every category Clear, with the
  security/compliance and external-integration categories immaterial for an offline single-player engine). It
  corrected three statements that were loose rather than wrong, and they are worth reviewing: FR-007's "costs no
  time" distinguishes an op with *no progress of its own* from a timed op whose duration is zero (the edge case);
  FR-011 says the cue's memory does not grow *as it advances*, which is the property the measurement supports;
  FR-017 asked leaves to be addable "without touching the engine", and now asks that they change neither the shape
  of the run nor the walk — and adds the `…State` rule. Two more items were resolved as recorded assumptions
  rather than questions, because an existing document already answered them: the cue is **last** in the play
  chain (`docs/ARCHITECTURE.md`'s `… clock → cue`), and determinism is a property of a run of this program rather
  than a bitwise guarantee across machines (phase 13's fixed timestep is where that claim would land).
