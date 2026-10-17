# Feature Specification: Dialogue Endings

**Feature Branch**: `097-dialogue-endings`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "we need to be able to specify that some dialogue is closed by the user and some is managed purely by cues… with many games it's not one or the other — you might be able to speed through dialogue by pressing buttons, or allow the dialogue to close itself — so the ending should be a set, so it can be any combination. We have three cases currently: ByCue, ByTimeout, ByPlayer. We would also need to update the various panels to support this." A follow-up decision removed the cue case: the set holds the two runtime conditions, and a cue can always close or replace a line.

## Origin

Two existing features meet here. `specs/038-dialogue-display` made every shown line dismissible by the player. `specs/057-exposit-cue-op` made `Exposit` a no-progress cue op that writes a line and immediately moves on. Together they leave a script unable to own a line's lifetime:

- a cutscene cannot stop the player closing a line,
- a cue cannot observe that the player closed a line, because it never waits,
- there is no cue op that closes a line, so the final line of a cutscene can remain on screen indefinitely.

This feature gives a line a **set of runtime ending conditions** and gives the cue a **waiting display op** and a **close op**. A scene can then be conversational (player closes each line), timed (the line closes itself), skippable (player or timer, whichever first), cue-managed (a cue closes or replaces the line), or any combination. The editor surfaces expose the same conditions wherever a dialogue cue is authored.

The ending vocabulary is closed for now: **ByPlayer** and **ByTimeout**. The set may contain either, both, or neither; the empty set means the cue owns the line's end. The timeout's duration is **not** part of the condition: `ByTimeout` is a kind, and the duration is a separate `voption` on the dialogue line. Closing a line from a cue is not a member of the set: a close or a replacement always works, whatever the set says, so cue closing is a universal capability rather than an authored condition.

## Clarifications

### Session 2026-10-17

- Q: What player input should count as the `ByPlayer` ending — only the existing dismiss key (`Space`), or a broader set of gameplay buttons? → A: `Space` only — the existing play-mode dismiss control, edge-triggered and focus-gated.
- Q: When a line is first authored, what ending should it get by default — and should that differ between the Dialogue panel and the cue editor? → A: A non-waiting display defaults to the empty set (cue-managed); a waiting display defaults to `{ ByPlayer }`; a hand-shown Dialogue panel line defaults to `{ ByPlayer }`.
- Q: If an author changes a line's timeout while the line is already shown, should the new duration count from when the line was originally shown, or restart from the moment of the edit? → A: Continue from the original show time — elapsed time counts against the new duration.
- Q: When a waiting display op's line is replaced by another line, should the waiting cue resume immediately, or keep waiting until the screen has no line at all? → A: Keep waiting until the screen has no line; a replacement delays the waiting cue.
- Q: Should the cue editor allow a waiting display op with no runtime ending — the one combination that can hold a cutscene indefinitely — or warn or prevent it? → A: Allow it silently; the advanced pattern stays available with no warning or prevention.

## Ending Semantics

The ending set says **which runtime conditions may end the line**. Closing the line from a cue is not a condition: a cue may always close or replace a line, whatever the set says (FR-002). The set may contain:

- **`ByPlayer`** — the player's `Space` closes the line, while playing and no text field has focus.
- **`ByTimeout`** — the line closes itself after `Dialogue.Timeout`, measured in play time. The duration lives on the line, not in the condition.

The set may contain neither, either, or both. The empty set means **the cue owns the line's end**: the line stays up until a cue closes or replaces it.

| Display op | Empty set (cue-managed) | Contains `ByPlayer` / `ByTimeout` |
|---|---|---|
| Non-waiting | The cue continues; the line stays up until a later close or replacement. | The cue continues; the line still ends on its player or timeout condition. |
| Waiting | The cue holds until the screen has no line — a close, or a replacement that is itself later closed. The same sequence cannot reach its own close, so the closing cue must come from a parallel branch or another cue. | The cue holds until the player or timeout condition ends the line, then continues in the same play frame. |

**An empty-set line in full:**

- The player's dismiss control does nothing to the line.
- The line never times out.
- It ends when a cue closes it or replaces it with another line.
- It does **not** imply that the cue blocks. A non-waiting empty-set line is the ordinary cue-managed case: **the cue continues with the dialogue still up**, and a later close or replacement clears it.
- If the cue reaches `Fin` without a close, the line stays up. That is deliberate: a persistent line is expressible, and the author owns the cleanup.
- A cue can always replace or close a line; that capability is universal and is not recorded in the set.

When the set contains `ByPlayer` or `ByTimeout`, those conditions also apply alongside the cue's universal capability. There are four set states: empty, `{ ByPlayer }`, `{ ByTimeout }`, and `{ ByPlayer; ByTimeout }`.

### How the two forms are distinguished

The waiting/non-waiting choice belongs to the **cue op**, not to the dialogue line. A shown line is the same value in both forms — text, presentation kind, ending set, timeout and the play time it was shown — and carries no "waiting" flag.

- **The non-waiting display op** is a no-progress leaf: it writes the line and is complete in the same cue step. The sequence moves on with the line still up.
- **The waiting display op** writes the same line and begins a running form of the op. While that running form is the live cue, it checks the screen's shown flag each tick. When the flag becomes false — from a player ending, a timeout, a close, or a replacement that is itself later closed — it completes. It continues in that same step when the clear is observed in its step; a clear produced by a parallel branch advanced after it is observed on the next play step. Either way the cue resumes within one play frame of the screen becoming clear.
- **In the editor**, the cue kind list offers the two forms as distinct entries. A selected node exposes the same ending controls for either; the running waiting form is read-only and is labelled as the op it came from.
- **Why not a boolean on one op?** The waiting form has the same shape as the existing span-time leaves (`Wait`, `Await`): it has a running form, is locked while running, and is visibly holding the cue. The non-waiting form stays a no-progress leaf, so the cue's shape rules remain intact. A boolean would make an op's control-flow shape depend on its data and force every consumer to inspect it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A line ends the way its author chose (Priority: P1)

An author scripts a line and chooses which runtime conditions may end it. The player then experiences the line accordingly: a line with no runtime conditions cannot be closed by input or time (only a cue can close it); a player-ending line waits for the dismiss control (`Space`); a timed line closes itself; a player-or-timeout line closes on the first of input or its duration. Any line, whatever its set, can also be ended by a cue's close or replacement.

**Why this priority**: this is the capability. Without it, a cutscene cannot distinguish a line the player must acknowledge from a line the scene owns.

**Independent Test**: put a line on screen with each set state (the empty set, ByPlayer, ByTimeout, ByPlayer+ByTimeout), leave it, press the dismiss control, advance play time, and close or replace it by cue; confirm the line ends only under its authored conditions.

**Acceptance Scenarios**:

1. **Given** a line with no runtime conditions (the empty set), **When** the player presses the dismiss control or time passes, **Then** the line remains shown; it ends when a cue closes or replaces it.
2. **Given** a line with `ByPlayer`, **When** the player presses the dismiss control, **Then** the line is no longer shown.
3. **Given** a line with `ByTimeout`, **When** its duration of play time elapses, **Then** the line is no longer shown; the dismiss control does nothing.
4. **Given** a line with both `ByPlayer` and `ByTimeout`, **When** either the dismiss control is pressed or the duration elapses, **Then** the line is no longer shown; the other condition has no further effect.
5. **Given** any line, whatever its ending set, **When** a cue closes or replaces it, **Then** the line is no longer shown; cue closing is always available.
6. **Given** a player-ending line, **When** the dismiss control is pressed while a text field has keyboard focus, **Then** the line remains shown.

---

### User Story 2 - A cue waits for a line, or moves on (Priority: P1)

An author chooses whether the cue waits for a shown line to end before reaching its next op. A waiting line makes a conversation advance on the player's input or the line's timer; a non-waiting line lets the scene continue while a line clears itself in the background. The author can also close a line explicitly.

**Why this priority**: it is what connects the ending conditions to the cue timeline. Without the waiting form, a player-closed line would still be followed by the cue's own timing, which is the awkward behaviour this feature removes.

**Independent Test**: build a sequence of two lines with a waiting display op; confirm the second line appears only after the first ends, whether by input or timeout. Then use a non-waiting empty-set line followed by other ops and a close: confirm the cue continues with the line up and the close clears it.

**Acceptance Scenarios**:

1. **Given** a waiting display op for a player-ending line, **When** the player dismisses the line, **Then** the cue reaches its next op in the same play frame.
2. **Given** a waiting display op for a timeout-ending line, **When** the timeout elapses, **Then** the cue reaches its next op in the same play frame.
3. **Given** a waiting display op for a player-or-timeout line, **When** either condition ends the line, **Then** the cue continues; both conditions resume it identically.
4. **Given** a non-waiting display op, **When** it is reached, **Then** the cue continues without holding, and the line still ends under its authored conditions.
5. **Given** a non-waiting empty-set line, **When** the cue reaches its following ops, **Then** they run with the line still shown, and a later close op clears it; the cue does not hold merely because the line has no player or timeout ending.
6. **Given** an empty-set line with a waiting display op, **When** another cue closes the screen's line, **Then** the waiting cue continues within one play frame: the same frame when the clear is observed before its step, the next frame when a parallel branch advanced after it produces the clear. If that cue replaces the line with another shown line, the wait continues until the screen is clear again.
7. **Given** a shown line, **When** a close op is reached, **Then** the line is no longer shown and the cue continues in the same frame.

---

### User Story 3 - The editor authors the ending everywhere (Priority: P2)

An author can set a line's runtime ending conditions in the Dialogue panel and in every cue-authoring surface. The cue kind list offers the waiting and non-waiting display ops and the close op; a selected display node exposes the player and timeout conditions and the timeout duration; the cue tree's row label says how the line ends; a running waiting display op is read-only.

**Why this priority**: the engine works before it is editable, but the feature is only usable by an author once every surface that writes a dialogue cue exposes the conditions.

**Independent Test**: author a display node in the Cue panel, enable the player and timeout conditions, set the seconds, and confirm the row label and the authored cue; repeat through the trigger's inline cue editor; set a hand-shown line in the Dialogue panel and confirm the same conditions.

**Acceptance Scenarios**:

1. **Given** the cue kind list, **When** it is opened, **Then** it offers a non-waiting display op, a waiting display op, and a close op.
2. **Given** a selected display node, **When** the author toggles the player and timeout conditions and sets the duration, **Then** exactly that node is written, expressing any combination; both disabled is the empty set.
3. **Given** a display node, **When** its row is drawn, **Then** the label distinguishes cue-managed (the empty set), player, timed and their combinations, and includes the duration.
4. **Given** a running waiting display op, **When** its row is drawn, **Then** it is labelled as the op it came from and is read-only, as a running wait is.
5. **Given** the Dialogue panel, **When** the author sets the shown flag, the player/timeout conditions and the seconds, **Then** the screen and the state reflect each, and no undo step or persisted change is recorded.
6. **Given** the trigger's inline cue editor, **When** a display node is selected, **Then** the same controls are offered as in the Cue panel. The Scenario panel only captures the screen's cue, so it inherits the authored value without a separate editor.
7. **Given** a newly created display node or a newly hand-shown line, **When** it first appears, **Then** the non-waiting display starts with the empty set, the waiting display starts with `{ ByPlayer }`, and a hand-shown line starts with `{ ByPlayer }`; each remains editable.

---

### Edge Cases

- **A cue-managed line left at `Fin`.** The cue may have continued with the line up the whole way; nothing clears it at the end. The author must reach a close op or replace it. This is deliberate, so a persistent line is expressible. The Dialogue panel's existing shown flag can still clear it while editing.
- **An empty-set line with a waiting op.** The cue waits until some cue closes or replaces the line. This is a valid advanced pattern (for example, a trigger forks the close), but it is the one combination that can hold a cutscene indefinitely if nothing closes it. The editor allows it silently, with no warning and no prevention.
- **Both runtime conditions present.** The first of `ByPlayer` and `ByTimeout` to occur ends the line; the other has no effect. A cue close or replacement remains available throughout.
- **The dismiss control with a text field focused.** `ByPlayer` does not fire. `ByTimeout` still runs.
- **Play time is paused.** A timeout does not advance while the editor is open or play is paused. The line resumes its countdown when play resumes.
- **A non-positive duration.** The line ends on the next play frame rather than being cleared before it can draw. The editor starts timed lines at 3.0 seconds.
- **A line is replaced while a waiting cue is waiting.** The waiting cue is waiting for the screen line to be gone. If the replacement is still shown, the wait continues; the line has no identity beyond the screen state. Concurrent dialogue is a single shared surface.
- **A portrait cannot resolve.** The line's state is still shown, so a timeout still ends it and `ByPlayer` still responds to the dismiss control; the view leaves the line undrawn and the Dialogue panel explains why, as today.
- **Rapid dismiss presses.** Input is the press edge, not a held key. One press ends one line; the next line is not ended by the same press. Repeated presses advance rapidly.
- **An inconsistent authored state.** `ByTimeout` in the set with no duration is prevented by the editor; the runtime treats it as no timeout. The cue op always writes the set and the duration together.
- **Existing cue-only content.** Content authored before this feature decodes as the empty set (cue-managed). A cue that previously relied on the player dismissing its last line must add a close op; otherwise the line remains.
- **The line ending is never persisted as runtime state.** Only the authored conditions in a cue are stored; the Dialogue panel's hand-set line remains a direct-write authoring surface.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A shown line MUST carry a set of runtime ending conditions drawn from `ByPlayer` and `ByTimeout`, in any combination including the empty set, plus a timeout duration when `ByTimeout` is present. The timeout duration MUST be data on the line, not part of the condition's identity.
- **FR-002**: A cue MUST always be able to replace a line, and an explicit close op MUST always clear it, regardless of the ending set. Cue closing is a universal capability, not an authored condition.
- **FR-003**: `ByPlayer` MUST end the line on the existing dismiss control's (`Space`) press edge while playing and while no text field has keyboard focus.
- **FR-004**: `ByTimeout` MUST end the line after the line's duration, measured in play time. The duration MUST NOT advance while play time is paused or the editor is open. If the duration is changed while the line is shown, the new duration MUST be measured from the original show time, so time already elapsed counts against it.
- **FR-005**: When both `ByPlayer` and `ByTimeout` are present, the first to occur MUST end the line. The other MUST have no further effect. A cue close or replacement remains available throughout.
- **FR-006**: With an empty set, the line MUST remain shown until a cue closes or replaces it; input and the passage of time MUST NOT end it.
- **FR-007**: The cue vocabulary MUST provide a non-waiting display form and a waiting display form. Both MUST carry the line's text, its presentation kind, its ending set and its timeout duration, while retaining the existing two-argument `Exposit` form so persisted cues keep decoding.
- **FR-008**: The non-waiting display op MUST write the line and cost one cue step; it MUST NOT hold the cue for the line's ending.
- **FR-009**: The waiting display op MUST write the line and hold the cue until the screen has no line shown. A replacement that is still shown MUST NOT resume it. When the screen becomes clear, the waiting op MUST complete in the same play step if it observes the clear; a clear produced later in the same `Parallel` is observed on the next play step, so the cue resumes within one play frame of the screen becoming clear.
- **FR-010**: The cue vocabulary MUST provide a close op that clears a shown line and costs no time.
- **FR-011**: The Dialogue panel MUST expose the player and timeout conditions and the timeout duration, so every combination — including the empty set — can be authored by hand. A line first shown by hand MUST default to `{ ByPlayer }`. The panel MUST continue to record no undo step and persist nothing.
- **FR-012**: The cue editor MUST offer the non-waiting display op, the waiting display op, and the close op. For either display op it MUST expose the player and timeout conditions and the duration, and it MUST express every combination, including the empty set. A newly created non-waiting display op MUST default to the empty set; a newly created waiting display op MUST default to `{ ByPlayer }`; a newly enabled timeout MUST start at 3.0 seconds. A waiting display op with the empty set MUST remain selectable and MUST NOT be blocked or warned about. The cue's ability to close or replace a line MUST be presented as unconditional, not as a toggleable condition.
- **FR-013**: The cue tree MUST label a display node with its ending set and, for `ByTimeout`, the duration — for example `cue` (the empty set, meaning cue-managed), `player`, `3.5s`, `player or 3.5s` — and MUST label a running waiting op as the op it came from. A running waiting op MUST be read-only.
- **FR-014**: Every cue-authoring surface that shares the cue editor — the screen's Cue panel and a trigger's inline cue editor — MUST offer the same display ops and ending controls. The Scenario panel captures the screen's cue and therefore inherits the authored value without a second editor.
- **FR-015**: Each accepted cue edit MUST be exactly one step on the one undo history. The Dialogue panel's direct writes MUST remain outside it.
- **FR-016**: Cue values carrying ending conditions and a timeout MUST round-trip through scenario persistence. Existing scenarios MUST decode, with their existing display ops read as the empty set (cue-managed).
- **FR-017**: A non-positive timeout MUST end the line on the next play frame. The editor MUST start a newly enabled timeout at 3.0 seconds.
- **FR-018**: A line whose portrait cannot resolve MUST still honour its timeout and player conditions; the existing panel advisory MUST remain the explanation.
- **FR-019**: The feature MUST add no per-frame allocation to the play loop, and with no line shown the frame MUST be unchanged from before the feature.
- **FR-020**: Existing content that relied on every line being player-dismissible MUST be migrated: the authored cue is read as the empty set (cue-managed), and a close op is added where a line previously relied on the player clearing it.

### Key Entities *(include if feature involves data)*

- **Dialogue line state**: what the screen is showing now — shown, text, presentation kind, the set of runtime ending conditions, the timeout duration (`voption`), and the play time at which it was shown.
- **Ending condition**: one of `ByPlayer`, `ByTimeout`. The set may contain either, both, or neither; the empty set means the cue owns the line's end. Closing from a cue is a universal capability and is not a member of the set.
- **Timeout duration**: the `voption` on the line, meaningful when `ByTimeout` is in the set. It is data, not identity.
- **Non-waiting display op**: writes a line and lets the cue continue.
- **Waiting display op**: writes a line and holds the cue until the screen has no line shown.
- **Close op**: clears a shown line and costs no time.
- **Cue editor and cue tree**: the shared authoring surface for every cue; it shows and edits the display ops, the two runtime conditions and the timeout.
- **Dialogue panel**: the direct-write surface for a hand-authored line, including the same conditions and timeout.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every set state — the empty set, `ByPlayer`, `ByTimeout`, and `ByPlayer+ByTimeout` — produces the authored result in 100% of the acceptance scenarios (0 unintended closes).
- **SC-002**: A waiting display op resumes the cue within one play frame of the screen becoming clear, for every ending condition and including a clear produced by a parallel/other cue (100%); a non-waiting display op never holds the cue (0 frames of holding).
- **SC-003**: The dismiss control closes 100% of lines containing `ByPlayer` and 0% of lines without it, and fires 0 times while a text field has focus.
- **SC-004**: A timeout ends its line within one frame of its duration, and advances 0 seconds while play time is paused.
- **SC-005**: The Dialogue panel and every shared cue-authoring surface can author every condition and combination (100%); each cue edit is one undo step, and the Dialogue panel records 0 steps and 0 persisted changes.
- **SC-006**: The play loop adds 0 bytes per frame, and every existing behavioural assertion continues to hold; any test change is limited to the new cue shape and the intentional cue-only migration.
- **SC-007**: 100% of existing scenarios decode and re-encode, and the intro cutscene gains the explicit close needed by its cue-managed lines.
- **SC-008**: Cue rows distinguish every set state 100% of the time, and a running waiting op is never editable.
- **SC-009**: A cue close or replacement closes 100% of lines regardless of the ending set.

## Assumptions

- **The ending vocabulary is closed for now.** It is `ByPlayer` and `ByTimeout`; the set may contain either, both, or neither. Future runtime endings join the set.
- **Cue closing is universal.** A cue can always replace a line and an explicit close op always works; the empty set means cue-managed, not "unclosable". The set records only the runtime conditions that can end a line.
- **The timeout's duration is data, not identity.** It lives in `Dialogue.Timeout` (`float voption`), not in `ByTimeout`. The editor maintains `ByTimeout ∈ set ⇔ Timeout.IsSome`; the runtime treats a missing duration as no timeout.
- **Waiting is separate from the ending.** The ending set says which runtime conditions end the line; the choice between the waiting and non-waiting display op says whether the cue waits.
- **Defaults preserve current behaviour.** A new non-waiting display op uses the empty set, matching today's `Exposit`; a new waiting display op is `{ ByPlayer }`; a line shown by hand in the Dialogue panel is `{ ByPlayer }`. A newly enabled timeout is 3.0 seconds.
- **The duration is play time.** It pauses with play, so a line does not expire while the editor is open or play is paused.
- **There is no reveal timer.** A line appears whole, as `specs/038` settled, so the timeout starts when the line is shown. If a typewriter reveal is added later, its interaction with the timeout must be revisited.
- **The dismiss control is the existing play-mode `Space` control**, edge-triggered and focus-gated. Holding it does not repeatedly close lines; rapid presses do. Other gameplay keys, including the interact key, do not end a line.
- **One line is on screen at a time.** A waiting op observes the screen line, not a unique line identity. Concurrent dialogue in parallel branches is a single shared surface and is not given per-line tokens by this feature.
- **The Dialogue panel remains a direct-write surface.** It records no command, no undo step, and no persisted state, following `specs/038` FR-009.
- **The visual presentation is unchanged.** Text, portraits, panel layout, wrapping and the ways a line can fail to draw are exactly as before; only the line's ending and the cue's waiting change.
- **Working vocabulary and compatibility.** The cue vocabulary keeps the existing two-argument `Exposit` as the empty-set, non-waiting form (`Timeout = ValueNone`), so persisted project files keep decoding; a general non-waiting form (`ExpositWith` in the plan) carries the set and timeout, and the editor presents both as one `Exposit` kind. The waiting form is `ExpositWait`, and the close op is `CloseExposition`; the plan may finalise the names without changing the behaviour.
- **Migration is intentional.** Existing `Exposit` forms decode as the empty set (cue-managed). The intro cutscene gains a close op so its final line clears as before; any other content that relied on the player dismissing its last line must do the same.
- **Out of scope:** a global cutscene skip or hold-to-fast-forward control, per-line voice or animation completion, and typewriter reveal. These are separate features; this one covers the two current runtime ending conditions and their combinations.
