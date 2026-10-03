# Feature Specification: Exposit Cue Op

**Feature Branch**: `057-exposit-cue-op`

**Created**: 2026-10-03

**Status**: Draft

**Input**: User description: "ok, lets fully implement exposit next" — completing the cue's `Exposit` op against its
reference, which carries a line's speaker and how it is presented, not only its text.

## Origin

`specs/046-cue-core` introduced the cue vocabulary and, with it, a minimal `Exposit`: a text string that writes the
screen's dialogue. That was enough to prove the no-progress op shape, but it is only a third of what the reference
(`odin-game/src/cue.odin`, `Cue_Op_Exposit`) carries: a line also has a **speaker** and a **kind** — a portrait line
with an expression, plain dialogue, or a thought — and the reference resolves the speaker through the cast to draw her
portrait.

The display half already exists: `specs/038-dialogue-display` built `Dialogue.Kind`
(`PortraitDialogue of Roster.Character * Emotion | Dialogue | Thought`), the render that resolves the portrait through
the cast binding (`world/DialogueRender.fs`), and the authoring panel (`editor/DialoguePanel.fs`). What is missing is
the line between them: a cue that can author any of the three kinds. This feature closes that gap, so a scripted
scene can present a named character with an expression and a thought, not only a speaker-less narration.

The reference names its speaker and expression by fixed-size strings and resolves the portrait at op time
(`actor_portrait_path`); this port carries the **closed** `Dialogue.Kind` (`Roster.Character`, `Emotion`) instead, and
leaves resolution to the view where `specs/038` already put it. That is the one deliberate departure: nothing about a
speaker or an expression is decided by a string comparison, and a miss is the expected, non-fatal case `specs/038`
already models.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue presents a line, however it is spoken (Priority: P1)

A cue contains an "exposit" op carrying a line and how it is presented. When the cue reaches it, the screen shows
that line in that form: a portrait line with a named speaker and expression, a plain dialogue line, or a thought.

**Why this priority**: it is the capability, and the shape with no progress of its own. Every scripted scene begins by
showing something.

**Independent Test**: put an `Exposit` op on a state's cue, step it, and assert the screen's dialogue now shows the
authored text and kind — including a portrait line's speaker and expression — no device, no GPU.

**Acceptance Scenarios**:

1. **Given** a cue whose op is `Exposit(text, PortraitDialogue(Akane, Happy))`, **When** it is reached, **Then** the
   screen's dialogue is shown, carries `text`, and its kind is a portrait line naming `Akane` and `Happy`.
2. **Given** an op with kind `Dialogue`, **Then** the dialogue is a plain line; **Given** kind `Thought`, **Then** it
   is a thought. Neither carries a speaker or an expression.
3. **Given** the op, **When** it is reached, **Then** it costs no time: the cue is at the next op in the same step.
4. **Given** a line already shown, **When** another `Exposit` is reached, **Then** the text and the kind are replaced
   by the new op's.

---

### User Story 2 - A portrait that cannot resolve is an expected miss, not a failure (Priority: P2)

A cue presents a portrait line whose speaker has no bound actor asset, or whose asset is not loaded, or which declares
no portraits. The line is written and the run carries on; the view leaves the line undrawn and the authoring panel
explains why, in the terms `specs/038` already defines.

**Why this priority**: it is the property that makes the op safe to author before the art is bound — an author writes
the line, binds the speaker later. The run must not break and must not half-draw.

**Independent Test**: with no cast binding, put a portrait `Exposit` on the cue, step it, and assert the dialogue's
kind is the portrait line that was authored (the op succeeded), while `DialogueRender.missReason` reports the
expected miss — no device where the miss is resolved by data alone.

**Acceptance Scenarios**:

1. **Given** an unbound speaker, **When** the op is reached, **Then** the dialogue is set to the authored kind, the
   cue advances, and nothing raises.
2. **Given** the same line, **When** the view draws it, **Then** nothing is drawn (the miss is the render's, as
   `specs/038` defines), and the panel reports the reason.

---

### User Story 3 - The panel authors the op (Priority: P3)

The cue panel's kind list offers "Exposit"; a selected exposit op shows its line text, a presentation-kind chooser,
and — for a portrait line — a speaker chosen from the closed roster and an expression chosen from the closed set.
Every chooser is a closed list, because none of them needs a zone or an asset.

**Why this priority**: the authoring surface. The engine works before it is editable.

**Independent Test**: assert the kind list contains "Exposit", its default is a plain line with empty text, the
kind/speaker/expression choosers write exactly the selected node, and a portrait line's speaker/expression round-trip
— all as pure functions of the cue, no device.

**Acceptance Scenarios**:

1. **Given** the kind list, **When** a node is re-kinded to "Exposit", **Then** it becomes a plain dialogue line with
   empty text (the previous default), and its text is edited by the existing field.
2. **Given** a selected exposit op, **When** its presentation kind is changed to "Portrait", **Then** it becomes a
   portrait line with a default speaker and expression, chosen from the closed lists.
3. **Given** a portrait exposit, **When** its speaker or expression is changed, **Then** exactly that node changes, as
   one undo step.
4. **Given** any exposit op, **When** its row is drawn, **Then** it is labelled as the op it is, and it is never
   read-only (it has no run state).

---

### Edge Cases

- **The final line wins.** `Exposit` costs no time and does not wait for the line to be dismissed, so a run of exposits
  without a `Wait` between them leaves only the last on screen. This is the reference's own behaviour; a script spaces
  lines with `Wait` (or, later, a trigger).
- **Empty text**: legal — the op writes an empty line. The panel still shows it and needs no special case.
- **A portrait line's speaker has several bound looks**: the op names the *character*, not a look; which asset plays
  her is the cast binding's and the view's choice (`specs/038` picks the first bound look in declaration order), so the
  op carries no look.
- **A plain or thought kind**: carries no speaker and no expression, by construction of the closed kind — there is
  nothing to leave blank.
- **Nesting**: `Exposit` inside a `Sequence`, `Parallel` or `Fork`, and inside an `If` branch, is legal like any leaf.
- **The op is never executing**: it has no `…State` form, so it is never read-only in the panel and never locks its
  ancestors (`specs/046` FR-010/FR-011).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cue vocabulary MUST extend `Exposit` to carry the line's text **and** how it is presented, so a cue
  can author a plain dialogue line, a thought, and a portrait line with a speaker and an expression.
- **FR-002**: The op MUST carry the closed presentation vocabulary (`Dialogue.Kind`, hence `Roster.Character` and
  `Emotion`), not strings, so a speaker or expression cannot be misspelled and matching on either is exhaustive
  (`specs/038`, `specs/055`).
- **FR-003**: Where the walk reaches an `Exposit`, the engine MUST set the screen's dialogue: shown, the authored text,
  and the authored kind. This replaces any line already shown.
- **FR-004**: `Exposit` MUST hold no run state and MUST cost no time — the no-progress shape (`specs/046` FR-007); the
  walk is unchanged.
- **FR-005**: Portrait resolution (cast binding → actor asset → expression's portrait) MUST remain the view's, at draw
  time; the op MUST NOT look up an asset, and an unresolved portrait MUST NOT fail or block the run. A miss is the
  expected, non-fatal case the view and the panel already share (`specs/038` FR-005/FR-006).
- **FR-006**: The events MUST be assertable **headlessly** — the dialogue is plain data on `GameplayState` — with no
  window, GPU or device (`specs/046` FR-016).
- **FR-007**: The panel MUST offer "Exposit" and, for a selected op, a presentation-kind chooser and — for a portrait
  line — a speaker chosen from the closed roster (`Roster.characters`) and an expression from the closed set
  (`Dialogue.emotions`). Every chooser MUST be a closed list; the text MUST use the existing text field
  (`specs/047`).
- **FR-008**: Each accepted panel edit MUST be exactly one step on the one undo history (`specs/047`, `specs/049`).
- **FR-009**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with new headless tests covering the stories
  and edge cases above.

### Key Entities *(include if feature involves data)*

- **`Dialogue.Kind`** (existing, `world/Dialogue.fs`): the closed presentation vocabulary —
  `PortraitDialogue of Roster.Character * Emotion | Dialogue | Thought`.
- **`Exposit`** (`world/Cue.fs`): the cue op, extended from text alone to text and kind.
- **Cue engine** (existing, `world/CueEngine.fs`): performs the line's write where the walk reaches it.
- **`DialogueRender`** (existing, `world/DialogueRender.fs`): resolves a portrait and draws the three kinds; unchanged,
  but now reachable from a cue.
- **Cue panel control** (existing, `editor/CueTree.fs`): the "Exposit" kind and its presentation choosers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue presents all three kinds of line exactly as authored — after the op, the screen's dialogue shows
  the authored text and kind, including a portrait line's speaker and expression (0 deviations across the scenarios).
- **SC-002**: An unresolvable portrait never breaks a run: 0 raised errors and 0 blocked cues across its scenarios.
- **SC-003**: An `Exposit` costs the game loop no time: 0 seconds, and the cue steps to the next op in the same step.
- **SC-004**: The op adds no per-frame cost to an idle run and no new run state (the existing "idle tick allocates
  nothing" property still holds).
- **SC-005**: The panel offers 1 presentation-kind requirement met by 3 closed choices and 0 free-text speaker or
  expression fields.
- **SC-006**: The capability is demonstrable headlessly: `dotnet build` is clean and `dotnet test` passes.

## Assumptions

- **The op carries `Dialogue.Kind`, reusing the display union.** `world/Cue.fs` gains a dependency on
  `world/Dialogue.fs` (pure, compiled earlier), widening its "`Prime` and `System.Numerics` only" note by one pure
  module — exactly as `specs/056` widened it by `world/Advent.fs`. The alternative — separate ops per kind, or a
  string speaker resolved at runtime — reintroduces either op sprawl or the stringly-typed speaker `specs/038` and
  `specs/055` exist to remove.
- **Resolution stays at draw time**, not at op time. The reference resolves a portrait when the op runs because its
  exposition state stores a texture and a path; this port's view already resolves through the cast binding
  (`specs/038`), so the op carries the speaker and expression and looks up nothing. The engine therefore needs no
  `AssetManager`.
- **The name plate shows the character's name** (`Roster.characterName`), not the actor asset's display name; that is
  the typed equivalent of the reference's label and is fixed by `specs/038`.
- **The Dialogue panel remains the manual authoring surface** and the cue op the scripted one; both write the same
  `GameplayState.Dialogue`, and neither owns the other (`specs/038`).
- **No new `…State` case**: the reference's `Cue_ExpositState` is empty, i.e. the same no-progress shape this port
  already uses; a cue that shows a line does not wait for it to be dismissed.
- **`[<SymbolicExpansion>]` on `Emotion` is deferred** to cue/scenario persistence (roadmap phase 15): nothing
  serializes a cue yet, and `Advent` — which cues already carry — has no such attribute either. The stale note in the
  `world/Dialogue.fs` header (that the attribute arrives with "the cue op that first authors a line") is corrected to
  name persistence as its trigger.
- **Not in scope**: a "clear/hide the line" op (the reference has none), waiting for the player to dismiss a line, and
  the scenario/trigger ops that would deliver lines from other sources (phases 10–11).
