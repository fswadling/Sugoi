# Feature Specification: Dialogue Display

**Feature Branch**: `038-dialogue-display`

**Created**: 2026-09-26

**Status**: Draft

**Input**: User description: "we need to think about displaying dialogue on the screen… our only ui system currently is imgui — which isn't really intended for in game ui". Then, settling the scope: "we dont need a full reimplementation of renpy", "we will want a custom 2d sprite / text renderer", "a struct at the gameplay screen level that holds dialogue data and a render pipeline to show it in the frontend", and — for the portrait — "I dont want to manually pick the correct portrait per character — each character will have a set of portraits corresponding to a set of emotions and we will just say Akane Sad in the data model".

**Origin**: `odin-game` is the template. Its `Exposition_State` is a small flat struct on the gameplay screen (text, speaker name, shown, kind, portrait) with three kinds — portrait dialogue, dialogue, thought — and five expressions; a cue op sets it, the op does not complete until it is dismissed, and SPACE clears it (`systems.odin`). `draw_exposition` draws a full-width panel with a rect, a border, an optional 128×128 portrait, a speaker label and word-wrapped body text. Its editor panel edits the same state's fields directly.

Two things this port must do differently. **No slow reveal**: the reference has no typewriter and no timer — a line appears whole and stays until it is dismissed — and this feature keeps that. **The portrait is derived, not stored**: the reference stores a loaded texture in the state and re-loads it from disk whenever the portrait changes, and its editor picks a speaker and an emotion; here the state names only *(character, expression)*, each character declares one portrait per expression, and the portrait is looked up from that set.

The port's existing UI is Dear ImGui, which is the editor's toolkit, not a game's: its own documentation says it "is NOT designed to create a user interface for games". This feature adds the game-side 2D layer the dialogue is the first consumer of.

## Clarifications

### Session 2026-09-26

- Q: When a character's declared portrait set is missing one of the expressions, should that character asset fail to load, or load with the gap and draw no portrait? → A: Fail at load — a character whose declared set has a gap (some expressions declared, not all) is a broken asset, reported at load rather than drawn around; declaring no portraits at all is a separate, legal case (see FR-006).
- Q: What should make a thought line's panel visibly distinct from a plain dialogue line's panel, when drawn? → A: Inverted colour scheme — a thought draws the same panel geometry in a light panel with a dark border; dialogue and portrait lines use the dark panel with a light border (the reference's behaviour).
- Q: When a line's kind draws no portrait, should the panel still reserve the portrait's column or should the text expand into it? → A: Text stays put — the panel always reserves the portrait's column, so the text's left edge and wrap width are the same for every kind.
- Q: What should happen when a line is too long for the panel to fit inside the viewport? → A: Left undrawn and explained in the authoring panel — the whole line stays visible while the panel fits the viewport, and a line that would overflow is not drawn and the Dialogue panel says why, rather than a silently clipped panel or a crash (it is reachable by ordinary typing).
- Q: A portrait line names a character who is not bound to an actor asset (or whose asset declares no portraits) — should the draw raise, or leave the line undrawn? → A: Leave the line undrawn and explain it in the authoring panel — an unbound character is expected while authoring rather than a bug, so a runtime resolution failure must never crash the game (the load-time set check stays loud).
- Q: Should any other draw-time condition raise? → A: No — a line too tall for the viewport is left undrawn and explained in the authoring panel on the same reasoning: no condition reachable from ordinary authoring may crash the game.

### Session 2026-09-27

- Q: Where should a draw-time miss be surfaced? → A: In the authoring panel, not the console: the Dialogue panel asks the view's `missReason` and prints the reason (unbound speaker, asset not loaded, no portraits, or the line taller than the viewport) in the author's terms. The view leaves the line undrawn and reports nowhere, which removes both the per-frame console logging and the screen state that previously debounced it.
- Q: Should the dialogue dismiss and the animation clock run while the editor is open (edit mode)? → A: No — both run only while playing, so the editor's SPACE stays free and an actor stands still while it is placed; an author clears a line by unticking Shown in the Dialogue panel.

## User Scenarios & Testing *(mandatory)*

Two parties, as with the animation and morph work. The **system driving the screen** — or an author in the editor — puts a line into the screen's dialogue state; the **player** sees it: a panel over the scene carrying the speaker's name, the speaker's portrait, and the line. A third party is the **asset author**, who declares a character's portraits; the **localiser** is out of scope.

### User Story 1 - A line appears on screen (Priority: P1)

A gameplay screen draws a dialogue panel over the scene: the speaker's name when the line has one, and the line's text, wrapped to fit and fully visible inside the panel. With nothing shown, the frame is exactly what it was before the feature.

**Why this priority**: this is the capability, and it is the whole foundation — the panel is a screen-space 2D layer, and a line of text is the first thing it draws. It stands alone: a line with no portrait, and no line at all, are both valid and both testable.

**Independent Test**: put a line into the dialogue state and confirm the panel and its text appear over the scene; confirm the text wraps and stays inside the panel; clear it and confirm the frame returns to what it was; resize the window and confirm the panel is still fully on screen for any line that fits the viewport (a line too tall for the viewport is left undrawn rather than clipped).

**Acceptance Scenarios**:

1. **Given** the screen is showing a line, **When** it draws, **Then** a panel is drawn over the scene carrying that line's text.
2. **Given** a line with a speaker, **Then** the panel carries the speaker's name; **Given** a line without one, **Then** it draws no name.
3. **Given** a line longer than the panel is wide, **Then** the text wraps and the whole line is visible inside the panel.
4. **Given** no line is shown, **Then** nothing is drawn and the frame is identical to the pre-feature frame.
5. **Given** the window is resized, **Then** the panel stays on screen and fully visible for any line that fits the viewport; a line that would not fit is left undrawn rather than clipped.

---

### User Story 2 - The speaker's portrait (Priority: P2)

A portrait line draws the speaker's portrait for the line's expression. The line names a character and an expression — "Akane, sad" — and the portrait is looked up from that character's declared portraits; nothing anywhere names a portrait directly.

**Why this priority**: it is what makes a line read as *that character, feeling that*, and it is the sprite half of the 2D layer. It ranks below Story 1 because a line is legible without it, and above Story 3 because a portrait is content rather than a control.

**Independent Test**: show a portrait line for a character and an expression; confirm that character's portrait for that expression appears; show the same character with a different expression and confirm the portrait changes; show a line with no speaker and confirm no portrait is drawn.

**Acceptance Scenarios**:

1. **Given** a portrait line for a character and an expression, **Then** that character's portrait for that expression is drawn in the panel.
2. **Given** the same character with a different expression, **Then** the other portrait is drawn.
3. **Given** a line whose kind draws no portrait, **Then** no portrait is drawn.
4. **Given** a character declared for every expression, **Then** every expression resolves; **Given** a character missing one, **Then** the asset is rejected at load rather than drawing a line without its portrait.
5. **Given** a portrait line whose character is not bound to an actor asset, **Then** the line is not drawn and the Dialogue panel shows why, and **Then** the game does not fail — the author can still reach the Cast panel and bind her.

---

### User Story 3 - The player dismisses it, the author drives it (Priority: P3)

A shown line stays until it is dismissed; while playing, the player dismisses it with a control, after which nothing is drawn. An author can put a line on screen by hand — choosing whether it is shown, which kind it is, who is speaking with what expression, and typing the text — and watch it appear, clearing it by unticking Shown rather than with the player's key.

**Why this priority**: it is the interaction and the surface that makes every other field reachable by hand. It ranks last because a line that appears and stays is already demonstrable; this adds the control over it and the way to drive it before any script exists.

**Independent Test**: show a line, leave it, and confirm it stays; dismiss it and confirm it goes; then set each field from the editor surface and confirm the screen reflects it; confirm dismissing never fires while typing in a text field.

**Acceptance Scenarios**:

1. **Given** a shown line, **When** no input arrives, **Then** it remains shown.
2. **Given** a shown line, **When** the player uses the dismiss control, **Then** nothing is drawn.
3. **Given** the author sets the shown flag, the kind, the speaker, the expression and the text, **Then** the screen reflects each.
4. **Given** the author is typing in a text field, **When** they press the dismiss control's key, **Then** the line is not dismissed.
5. **Given** the author changes the dialogue, **Then** no undo step is recorded and nothing is persisted.

---

### Edge Cases

- **A line is never partially drawn.** The reference has no typewriter and no auto-advance, settled in conversation: a line appears whole, at full opacity, and stays until it is dismissed. There is no reveal progress and no timer in the state.
- **Nothing shown is the default, and it must be invisible.** The state's default is no line; with no line the frame is byte-for-byte what it was before the feature, and every existing rendering and gameplay assertion passes unmodified.
- **The text must fit, at any viewport size.** A long line wraps inside the panel and the whole line stays visible; after a resize the panel is still on screen, in proportion, and never clipped off an edge for any line that fits the viewport, while a line that would not fit is left undrawn rather than clipped.
- **A line that cannot fit is left undrawn, not drawn clipped, and never a crash.** A line whose wrapped box is taller than the usable viewport height is left undrawn and the Dialogue panel shows why — including while the author is typing it in the editor surface, so the state that caused it can still be edited rather than taking the game down.
- **A character declares a complete portrait set, or none.** A character either declares one portrait per expression or declares none at all; a set with a gap is a broken asset and fails at load rather than drawing a line with a missing portrait, and a character that declares none cannot speak a portrait line.
- **A portrait that does not resolve leaves the line undrawn, and never fails the game.** Asking for a pair whose character is not bound to an actor asset — or whose asset declares none — is an expected authoring state rather than a bug: the line is not shown and the Dialogue panel shows why, so a half-set-up scene cannot crash the game.
- **A character with no name plate is legal.** A line with no speaker draws text and no name, and no portrait.
- **The text is not ASCII-only.** The drawn text may contain any character the line uses; a character the font cannot draw renders as the font's fallback glyph rather than failing at runtime, and the font is chosen to cover the story's text.
- **An empty line is a legal line.** A shown line with no text draws the panel and no text, rather than the panel disappearing.
- **A new line replaces the old one.** Putting a second line in the state while one is shown replaces it; there is no queue in this feature and no history.
- **Dialogue never obeys the scene.** It is drawn in screen space over the scene and is never occluded by geometry, and drawing it changes nothing about how the scene itself renders.
- **The dismiss control must not fire while typing.** The editor's text fields take the same keys; the dialogue's dismiss control does nothing while a text field has keyboard focus.
- **The state is the screen's, not a zone's.** Switching zones does not clear it — what is on screen is the script's business, not the zone's — and the dialogue state is not carried by any entity's captured authored state.
- **Fonts and portraits are loaded once.** Neither is re-read or re-uploaded per line or per frame; the reference re-loads a portrait from disk whenever it changes, and that is the behaviour this deliberately does not copy.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The gameplay screen MUST carry a dialogue state: whether a line is shown, which kind of line it is, who is speaking with which expression, and the line's text.
- **FR-002**: A line MUST be one of a fixed set of kinds — a portrait line (speaker, expression, text), a plain dialogue line (text), and a thought (text, drawn on an inverted light panel so it reads apart from a dialogue line) — and a line whose kind carries no speaker MUST draw no name plate and no portrait.
- **FR-003**: The screen MUST draw the shown line in screen space over the scene: a panel, the speaker's name when the kind has one, and the line's text. With no line shown it MUST draw nothing, and the frame MUST be identical to the pre-feature frame.
- **FR-004**: The text MUST be drawn from a font loaded once as an asset, wrapped so the whole line is visible inside the panel, and laid out against the current viewport size so that a resize leaves the panel and its text fully on screen. Its box MUST reserve the portrait column whether or not the kind draws a portrait, so its left edge and wrap width stay the same across kinds. A line whose wrapped box would be taller than the usable viewport height MUST be left undrawn and surfaced in the authoring panel rather than drawing a clipped panel.
- **FR-005**: The portrait MUST be resolved from the line's character and expression against that character's declared portraits. No part of the dialogue state and no authoring surface may name a specific portrait or file. A pair that does not resolve MUST leave the line undrawn and MUST be surfaced in the authoring panel, and MUST NOT fail the game.
- **FR-006**: A character that declares portraits MUST declare one for every expression in the fixed set — a partial set is a broken asset and MUST fail loudly at load. Declaring no portraits is legal: such a character may not speak a portrait line. A *(character, expression)* pair that does not resolve at draw time — the character is not bound to an actor asset, or that asset declares no portraits — MUST leave the line undrawn and MUST be surfaced in the authoring panel, rather than raising.
- **FR-007**: The dialogue state MUST be runtime screen state: it MUST NOT be persisted, MUST NOT record an undo step, and MUST NOT be carried by an entity's captured authored state.
- **FR-008**: A shown line MUST remain until cleared. While playing, the player MUST be able to clear it with a single control, and clearing MUST NOT happen while a text field has keyboard focus. The control is a play-mode control: it does nothing while the editor is in edit mode.
- **FR-009**: The editor MUST provide a surface that sets every field of the dialogue state directly — shown, kind, speaker, expression and text — so a line can be put on screen by hand, and it MUST record no command and store nothing beyond the state itself.
- **FR-010**: Drawing dialogue MUST NOT introduce per-frame allocations; the font and the portraits MUST be loaded once and reused rather than re-read per line or per frame.
- **FR-011**: The dialogue layer MUST be drawn over the scene, MUST NOT be occluded by scene geometry, and MUST NOT change how the scene itself renders or how it performs.
- **FR-012**: The existing behaviour MUST be unchanged: with no line shown, and for every existing feature, the rendering, gameplay, editor and suite assertions MUST pass unmodified.

### Key Entities *(include if feature involves data)*

- **Dialogue state**: the screen's current line — whether it is shown, its kind, its speaker and expression, and its text. The one thing a caller writes and the drawing reads.
- **Line kind**: portrait line, dialogue line, or thought; it decides whether a name plate and a portrait are drawn and how the panel is presented.
- **Expression**: one of a fixed, small set of feelings a portrait is drawn with.
- **Character portrait set**: one portrait per expression, declared for a character that speaks; the sole source of every portrait on screen.
- **Panel**: the screen-space region the line is drawn in; its placement follows the viewport, and its text wraps to fit it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a line shown, the panel, the speaker's name where the kind has one, and the full text are visible over the scene; with no line shown, the frame is identical to the pre-feature frame (100% of both states).
- **SC-002**: A portrait line draws the portrait declared for its *(character, expression)*; a character declaring every expression resolves all of them (100%). A pair that does not resolve leaves the line undrawn and is surfaced in the authoring panel, and the game keeps running (0 crashes, no console output).
- **SC-003**: A line longer than the panel wraps and remains fully visible, and after a viewport resize the panel and its text remain entirely on screen for any line that fits the usable viewport height (100% of fitting lines; a line that would not fit is left undrawn, 0 clipped panels).
- **SC-004**: A shown line is still shown after ten seconds with no input, and is gone within one frame of the dismiss control being used (0 changes without input, 1 frame to clear).
- **SC-005**: An author can set each field of the dialogue state and the screen reflects it (100% of the five fields).
- **SC-006**: Dialogue edits record no undo step and persist nothing: after changing dialogue, Ctrl+Z reverses the previous scene edit and the saved project is unchanged (0 steps, 0 persisted changes).
- **SC-007**: Drawing dialogue adds no measurable per-frame memory growth, and every existing rendering, gameplay and test assertion passes unmodified (0 bytes/frame, 100%).
- **SC-008**: A character portrait set with a missing expression is reported as a load failure rather than drawn around (100%).

## Assumptions

- **No reveal, no advance timer, no queue.** Settled in conversation, and it is the reference's model: a line appears whole and stays until it is dismissed. Typewriter reveal and auto-advance are deliberately out of scope, so the state carries no progress and no timing.
- **The portrait is derived from *(character, expression)*; the author never names a portrait.** Settled in conversation. The state names the pair; each character declares one portrait per expression; the cast itself stays free of assets, and the portraits are declared with the character's asset exactly as the reference declares them.
- **The expression set is fixed and small** — a neutral expression plus a handful of feelings, matching the portrait sets — so that "one portrait per expression" is a complete, checkable set rather than an open list.
- **The state belongs to the screen, not to a zone.** It is screen state beside the camera and the editor, so a zone switch does not clear it; a script that changes zones is responsible for what it leaves on screen.
- **Dialogue is drawn by the game's own 2D layer, not by the editor's UI toolkit.** Settled in conversation: the existing UI is the editor's, and the game needs its own screen-space layer. How that layer is built — the drawing mechanism, the font's source and how it is rasterised — is a design decision for the plan; this specification fixes only what must appear and when.
- **The 2D layer is general but this feature draws only dialogue.** A panel, an image and text are what dialogue needs; HUD elements, prompts and menus are future consumers of the same layer and are out of scope here.
- **A font asset is part of landing this.** The project ships no font today, so one must be added with its licence, and its coverage must include the story's text.
- **Portraits are part of landing this.** Declaring portraits is optional per character; a character that declares none cannot speak a portrait line, and a portrait line naming such a character (or one not bound to an asset) is left undrawn (the Dialogue panel shows why) rather than failing the game. At least one character must declare the full expression set so a portrait line can be demonstrated, and the reference portrait images can be adopted.
- **Nothing delivers lines to the screen yet.** The cue engine that would put a line there does not exist, so the editor surface in FR-009 is the way this feature is demonstrated; driving lines from a script is a later feature and is out of scope.
- **The existing render path is the foundation.** The 3D scene, its pipelines and the editor's UI toolkit are unchanged; this feature adds a screen-space layer over them and one field of screen state.
