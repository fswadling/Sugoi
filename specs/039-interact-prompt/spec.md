# Feature Specification: Interact Prompt

**Feature Branch**: `039-interact-prompt`

**Created**: 2026-09-27

**Status**: Draft

**Input**: User description: "id like to add the ui prompt when for when entering a trigger. look at odin-game for an example of how that should look"

**Origin**: `odin-game` is the template. `draw_interact_prompt` (`src/exposition.odin`) draws the interactable trigger's authored text as a single line — a small font, centred horizontally, a little above the bottom edge, in a muted grey — with no panel and no background. The trigger carries an interactable flag and a prompt string (`entity.odin`); the systems pass keeps the **closest** interactable trigger the player is inside (`systems.odin`), and the screen draws the prompt after the 3D scene, just before the editor's own UI (`gameplay_screen.odin`).

This port already has the data and the detection (spec 029): a trigger carries an interactable flag and a prompt, and the play-mode trigger pass resolves the trigger the player is inside. What is missing is the presentation: today that prompt is drawn by the editor's toolkit (ImGui), which is not game UI. This feature draws it with the game's own 2D layer (spec 038), whose stated purpose includes "HUD elements, prompts and menus" as its future consumers.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The prompt appears when you enter a trigger (Priority: P1)

A player walks into an interactable trigger and a line of text appears over the scene, telling them what they can do there — "Open", "Talk to Akane", and so on. Walking out, it goes. It is drawn by the game, in the game's own frame, not by the editor.

**Why this priority**: this is the capability. A prompt is how the game tells the player that something here can be interacted with, and it is the second consumer of the 2D layer that the dialogue work built — the first thing that proves the layer is general rather than dialogue-shaped.

**Independent Test**: place an interactable trigger carrying a prompt, play, and walk into it: the text appears over the scene. Walk out: it goes. With no interactable trigger entered, nothing is drawn.

**Acceptance Scenarios**:

1. **Given** the player is playing and inside an interactable trigger whose prompt is not empty, **Then** that prompt is drawn over the scene.
2. **Given** the player leaves that trigger, or it stops being interactable, or its prompt is cleared, **Then** the prompt is gone within one frame.
3. **Given** the player is inside no interactable trigger, **Then** nothing is drawn.
4. **Given** a dialogue line is shown while the player is inside an interactable trigger, **Then** the prompt is not drawn, and **When** the line is dismissed, **Then** the prompt is drawn again.

---

### User Story 2 - It reads as game UI, and stays on screen (Priority: P2)

The prompt is part of the game's presentation, not an editor window: it sits over the scene near the bottom, centred, legible against whatever is behind it, and stays fully on screen at any window size. It appears only while playing — while editing, the trigger's Inspector is where its prompt is read and set.

**Why this priority**: it is the whole reason the layer exists in this project — a prompt drawn by the editor's toolkit reads as a debugging read-out and would not exist in the packaged game. It ranks below Story 1 because a prompt that only appears in the debug read-out is still a prompt; it ranks above Story 3 because it is what makes the prompt shippable.

**Independent Test**: resize the window and confirm the prompt stays centred and fully on screen; enter edit mode and confirm it is absent; confirm it is not an editor window (it is not in the dock layout, cannot be moved or closed, and is not an ImGui panel).

**Acceptance Scenarios**:

1. **Given** a prompt is shown, **When** the window is resized, **Then** it remains centred and fully on screen.
2. **Given** the editor is in edit mode and the player is inside an interactable trigger, **Then** no prompt is drawn.
3. **Given** the prompt is shown over bright scene content and over dark scene content, **Then** it is legible against both.

---

### User Story 3 - The nearest trigger wins; silence when there is nothing to say (Priority: P3)

Where interactable triggers overlap, the one the player is closest to is the one shown, and a trigger with an empty prompt — or one that is not interactable — says nothing: no empty box, no stray line.

**Why this priority**: overlapping triggers are ordinary (a doorway inside a room's trigger), and showing several prompts at once, or a blank one, reads as a bug. It ranks last because the common case is a single trigger, which Story 1 already covers.

**Independent Test**: overlap two interactable triggers carrying different prompts and confirm the nearer one's prompt is shown as the player moves between them; set an empty prompt and confirm nothing is drawn; enter a non-interactable trigger and confirm nothing is drawn.

**Acceptance Scenarios**:

1. **Given** two interactable triggers overlap and the player is inside both, **Then** only the nearer one's prompt is shown.
2. **Given** the player enters an interactable trigger whose prompt is empty, **Then** nothing is drawn.
3. **Given** the player enters a trigger that is not interactable, **Then** nothing is drawn.

---

### Edge Cases

- **The prompt and the dialogue panel share the bottom of the screen.** Both are drawn by the same 2D layer and are anchored near the bottom, so they cannot both be shown without fighting for the same strip: the dialogue panel is 88 % of the width and sits above a 6 % bottom margin. While a dialogue line is shown it takes the screen and the prompt is not drawn; the prompt returns when the line is dismissed.
- **A prompt wider than the window.** It must stay on screen rather than run off an edge.
- **The prompt appears and disappears cleanly.** No empty panel, no lingering line, no flicker while the player stands at the edge of a trigger.
- **The trigger is deleted, disabled, or made non-interactable while the player stands in it.** The prompt goes on the next frame.
- **The player leaves play mode while inside a trigger.** The prompt goes with it — nothing is drawn in edit mode.
- **No player, or a player outside every trigger.** Nothing is drawn, and the frame is the frame this project drew before the feature.
- **Non-ASCII prompt text.** Any character the font can draw is drawn; a character it cannot renders as the font's fallback glyph rather than failing, as the dialogue's text does.
- **A prompt is short by design.** Like the reference, a prompt is a word or a short phrase, not a paragraph; a paragraph is the dialogue panel's job.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: While the player is playing and no dialogue line is shown, the screen MUST show the prompt of the interactable trigger the player is inside.
- **FR-002**: The shown text MUST be exactly the trigger's authored prompt. The game MUST NOT add to it (no key hint, no glyph, no decoration) and MUST NOT take it from any other source.
- **FR-003**: The prompt MUST stop being drawn — within one frame — when the player is no longer inside an interactable trigger with a non-empty prompt. This includes leaving the trigger, the trigger being deleted or disabled, its prompt being cleared or its interactable flag cleared, and leaving play mode.
- **FR-004**: When the player is inside more than one interactable trigger, the one the player is nearest MUST be the one shown; only one prompt is ever shown at a time.
- **FR-005**: An entered trigger whose prompt is empty, and a trigger that is not interactable, MUST draw nothing.
- **FR-006**: The prompt MUST be drawn as game UI over the scene: in screen space, never occluded by scene geometry, part of the game's frame rather than an editor window, and it MUST NOT change how the scene itself renders or add cost to the frame (FR-010).
- **FR-007**: The prompt MUST remain fully on screen — centred horizontally, clear of the bottom edge — at any window size.
- **FR-008**: The prompt MUST be legible over both bright and dark scene content; its legibility MUST NOT depend on what is behind it.
- **FR-009**: With nothing to show, nothing of the prompt MUST be drawn and the frame is otherwise unchanged. The only intended difference from the pre-feature frame is the removal of the editor's play-mode read-out (research Decision 7).
- **FR-010**: Drawing the prompt MUST NOT introduce per-frame allocations.
- **FR-011**: The existing behaviour MUST be unchanged: the rendering, gameplay, editor and suite assertions MUST pass unmodified.
- **FR-012**: While a dialogue line is shown, the prompt MUST NOT be drawn — the line takes the screen — and it MUST return when the line is dismissed.

### Key Entities *(include if feature involves data)*

- **Interactable trigger's prompt**: the authored line a trigger shows when the player is inside it and it is interactable. An empty prompt means "say nothing"; the same text is what the trigger's Inspector already edits (spec 029). The game adds nothing to it.
- **Interactable trigger**: a trigger the player can interact with (a flag) and that carries a prompt. The trigger pass resolves the nearest one the player is inside, while playing.
- **Player**: the entity whose position decides which triggers are entered, and thus whose prompt is shown.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Entering an interactable trigger shows its prompt within one frame, and leaving removes it within one frame (0 frames of lag, 100% of enter/leave transitions).
- **SC-002**: With no interactable trigger entered (or no player, or edit mode), no prompt is drawn and the frame is otherwise unchanged — the only intended difference from the pre-feature frame being the removed editor read-out (100%).
- **SC-003**: The prompt is fully on screen and centred at any window size (100% of window sizes), and it carries its own contrast so its legibility does not depend on the scene behind it — legible over both the brightest and the darkest scene content the walkthrough can place behind it (2 of 2 sampled backgrounds).
- **SC-004**: Where interactable triggers overlap, the nearest one's prompt is the one shown (100%).
- **SC-005**: Drawing the prompt adds no per-frame allocation and no measurable per-frame cost, and every existing rendering, gameplay and test assertion passes unmodified (0 bytes/frame, 100%).

## Assumptions

- **Play mode only.** The prompt is a play-mode read-out; in edit mode it is absent, because the trigger's Inspector already shows the same text for authoring. This follows the reference, which draws it in the play screen.
- **The prompt is the authored text alone.** No key hint or interact glyph is added, because there is no interact action or cue engine yet; when one arrives it can extend this. A prompt tells the player *what* is here, not yet *how* to act on it.
- **The editor's ImGui read-out is removed.** The editor currently prints the prompt (and the names of the triggers the player is inside) in a `Status` overlay as a stand-in for the cue a trigger will run. This feature moves the prompt to the game's 2D layer and removes that whole play-mode overlay, so nothing duplicates the prompt and no debug window appears over the game.
- **The game's 2D layer is reused with one capacity bump.** This is a consumer of spec 038's layer, not a new one: the prompt is text with a shadow for contrast, which the layer already draws. The only change to the layer is a third text slot, so it can hold every kind of string it draws; no new pass, pipeline or shader is added.
- **The trigger's data and authoring are unchanged.** The interactable flag and the prompt text already exist (spec 029) and are authored in the trigger's Inspector; this feature adds no authoring surface and changes no authored format.
- **Contrast is built into the prompt's own drawing.** The prompt is kept legible by drawing a shadow under it (research Decision 3); the requirement is only that its legibility does not depend on the scene behind it.
