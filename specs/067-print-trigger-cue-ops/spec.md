# Feature Specification: Print & Trigger Cue Ops

**Feature Branch**: `067-print-trigger-cue-ops`

**Created**: 2026-10-04

**Status**: Draft

**Input**: User description: "lets do print and enable disable triggers" — odin's `Print` op and the `EnableTrigger` / `DisableTrigger` pair.

## Origin

Comparing odin's `Cue_Op` union (`odin-game/src/cue.odin`) against `world/Cue.fs` after
`specs/066-actor-motion-cue-ops`, two op-portable groups remain unported:

- **Print** — `Cue_Op_Print { text }`: `fmt.println("[Cue_Op]", text)` at the point the walk reaches it.
  A no-progress leaf, for tracing how far a cue ran.
- **Trigger toggles** — `Cue_Op_EnableTrigger { name }` / `Cue_Op_DisableTrigger { name }`: find the trigger
  named `name` and set its enabled flag. A no-progress leaf.

The F# trigger already carries exactly the flag these ops need: `TriggerState.Base.Enabled`, which
`Systems.updateTriggers` honours ("a disabled trigger is never inside") and the Inspector's checkbox already
writes (`specs/058`). Nothing turns it from a cue yet.

Still out of scope after this: odin's scenario ops (`InlineScenario` / `ForkScenario` / `ClearScenario`, a
feature of their own — ROADMAP phase 11) and the camera-follow / interactions toggles.

## Clarifications

### Session 2026-10-04

- Q: Does a trigger toggle name a zone, or act on the active zone only? → A: **Name a place** — the op carries a
  `Place` and a trigger name in it, so a cue can arm a trigger in a zone it is not in; the trigger resolves in
  that place's zone and the op is a no-op when the place is not present. This is the project's cross-zone
  convention (a cue's ops were designed to span zones), and the editor defaults the chooser to the active zone.
- Q: Where does `Print` write? → A: **The console (stdout)** — a debug convenience, matching odin's
  `fmt.println`; player-facing text is `Exposit` (`specs/057`).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue arms or disarms a trigger (Priority: P1)

A cue turns a named trigger on or off in a named place, where the story reaches it — e.g. an area only begins
detecting the player after a scene, or a trigger is retired once it has served its purpose. The change is a
runtime effect: it takes hold on the next play tick and is not an undoable edit.

**Why this priority**: it is the gameplay-relevant half — it gates world behaviour mid-scene, and the trigger
already carries the flag to flip, so it is the smallest useful slice.

**Independent Test**: place a trigger in a present place, run a cue carrying `DisableTrigger (place, name)`,
step the play systems, and assert the trigger no longer fires or tracks the player; run `EnableTrigger` and
assert it resumes. A place that is not present, a name the place does not hold, and a redundant toggle perform
nothing.

**Acceptance Scenarios**:

1. **Given** a placed, enabled trigger, **When** a cue reaches `DisableTrigger (place, name)`, **Then** the
   trigger is disabled and on the next play tick it neither fires nor tracks the player.
2. **Given** a disabled trigger, **When** a cue reaches `EnableTrigger (place, name)`, **Then** it is enabled
   and behaves as an enabled trigger again.
3. **Given** a trigger already in the named state, **When** the matching toggle runs, **Then** nothing changes
   (a no-op) and the cue continues.
4. **Given** a place that is not present, or a name the place holds no trigger for, **When** the op runs,
   **Then** it performs nothing and reports why on stderr.
5. **Given** a cue that disables then re-enables a trigger, **When** it is run more than once, **Then** it does
   not fault.

---

### User Story 2 - A cue prints a line (Priority: P2)

A cue emits a diagnostic line where the walk reaches it, so an author can trace how far a cue ran without
attaching a debugger. It is a developer-facing trace, not dialogue (`Exposit` is the player-facing line).

**Why this priority**: it is a small authoring aid — genuinely useful but not required for any world behaviour,
so it rides behind the trigger toggles.

**Independent Test**: run a cue carrying `Print` and assert the line is emitted once at that point and the op is
then done; run it again and assert it prints again (no hidden state).

**Acceptance Scenarios**:

1. **Given** a cue carrying `Print "hello"`, **When** the walk reaches the op, **Then** the text is emitted
   once and the op is done.
2. **Given** a print reached mid-sequence, **When** the cue runs again, **Then** it prints again.
3. **Given** a cue being edited (not playing), **When** the panel draws it, **Then** nothing is printed.

---

### Edge Cases

- **A trigger whose name is shared**: two triggers in a place may hold the same name; the first match wins (the
  same rule `tryWaypointByName` uses).
- **A place that is not present**: the toggle performs nothing and says so — the author makes it present with
  `AddZone` first, as the actor ops require (the place's zone must exist to hold a live trigger).
- **Disabling the trigger the player is inside**: the next play tick recomputes the inside/tracked state, so the
  prompt clears and the trigger stops firing — the op sets only the enabled flag.
- **A disabled trigger's authored cue**: it does not fire while disabled (`specs/058`), which is the point.
- **A redundant toggle**: enabling an enabled trigger, or disabling a disabled one, performs nothing and says so.
- **While editing**: cues do not run, so no trigger op fires and no line is printed from the editor.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cue MUST gain `EnableTrigger` and `DisableTrigger`, each naming a **place** and a **trigger by
  name** in it, and setting that trigger's enabled flag. The trigger is resolved in the named place's zone — the
  cross-zone convention — so a cue can arm a trigger in a zone it is not in; a place that is not present performs
  nothing (FR-003).
- **FR-002**: A trigger toggle MUST set the named trigger's enabled flag; a disabled trigger MUST NOT fire nor
  track the player, and an enabled one MUST behave as an enabled trigger — the behaviour `Systems.updateTriggers`
  already implements.
- **FR-003**: A toggle whose place is not present, whose trigger does not resolve in that place, and a toggle
  whose effect already holds, MUST perform nothing; each MUST report its reason on stderr (the `specs/066` no-op
  convention), and none may fault, so a cue is safe to re-run.
- **FR-004**: The cue MUST gain `Print`, which emits its text once when the walk reaches it and is then done — a
  no-progress leaf.
- **FR-005**: Every op here MUST be a **runtime** effect, never a step on the undo history.
- **FR-006**: The editor's cue panel MUST offer the new ops with their fields — a place chooser and a
  trigger-name chooser over that place's triggers (defaulting to the active zone, where the author works), and a
  text field for `Print` — and each accepted edit MUST be one step on the one undo history (`specs/049`).
- **FR-007**: This feature MUST add no new zone or actor vocabulary, and MUST NOT touch the scenario ops or the
  camera-follow / interactions toggles.

### Key Entities *(include if data involved)*

- **Trigger**: the per-zone volume with an enabled flag and an authored cue (`TriggerState`, `specs/058`); what
  the toggles set and what the trigger system reads. A toggle reaches one through the place it names.
- **Print line**: the text a `Print` op emits; a developer-facing trace, distinct from `Exposit`'s dialogue.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue can disable and re-enable a named trigger in a named place, and the change takes hold on the
  next play tick (the trigger stops, then resumes, firing and tracking).
- **SC-002**: A cue can print a line once, at the point the walk reaches it.
- **SC-003**: A cue carrying either op can be run more than once without raising on state its earlier run
  produced.
- **SC-004**: The editor offers the ops with their fields, and each accepted edit is one step on the one undo
  history.
- **SC-005**: The project builds with no warnings and the suite is green, with no device opened.

## Assumptions

- **Toggles name a place** (Clarifications). The op carries a `Place` and resolves the trigger in that place's
  zone — the cross-zone convention the zone and actor ops follow. The editor defaults the chooser to the active
  zone; the stored name is resolved at run time, so the chooser's zone is a convenience, not a coupling.
- **Triggers are addressed by name.** The name is the trigger's `Entity.Name` (what the Inspector shows); if two
  triggers in a place share a name, the first match wins, as `tryWaypointByName` does for waypoints.
- **The flag is the whole effect.** A toggle sets `Enabled` only; the trigger's runtime `Triggered`/tracked-prompt
  state is recomputed by the next play tick, exactly as the Inspector's checkbox behaves.
- **Print is developer-facing.** It writes to the console/stdout (Clarifications), matching odin's `fmt.println`;
  player-facing text is `Exposit` (`specs/057`).
- **A cue runs during play**, so the named place's zone is present when the op acts on it.
- **This is the last op-portable piece of odin's cue family** bar the scenario ops and the camera-follow /
  interactions toggles.
