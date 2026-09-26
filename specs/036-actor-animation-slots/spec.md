# Feature Specification: Actor Animation Slots with Weights

**Feature Branch**: `036-actor-animation-slots`

**Created**: 2026-09-26

**Status**: Draft

**Input**: User description: "The ActorRenderer has a single animation slot. We should have a slot map with room for 8 animations, each with a weight, and we should run the weighted average of the animations." Two boundaries were drawn in the same conversation. First: **setting weights is in scope; the gameplay logic that cross-fades idle to walk is not.** Second: **the editor is in scope** — "we need to add ... updating the editor so that we can set the animation slots. this should be a section in the actor inspector."

**Origin**: `specs/009-animated-character` gave each actor one looping clip, and the renderer's single slot was described then as leaving "room for future multi-slot blending". `odin-game`'s `Actor_Renderer` holds an animation slot map beside a blended-pose buffer, and its per-bone weighted blend is the reference this feature follows. Its editor shows those slots as an "Animations" group in the entity properties panel, which is the model for the Inspector section here.

## User Scenarios & Testing *(mandatory)*

Three parties meet here. The **system driving an actor** — the routine that decides what a character is doing — chooses which clips play and how strongly each one counts. The **author in the editor** inspects and drives that same state by hand: which clips an actor is blending, and how much each weighs, while a scene is set up. The **player** sees the result of both: one continuous pose built from all of them at once, rather than one clip snapped over another.

### User Story 1 - An actor poses as a blend of several animations (Priority: P1)

An actor holds more than one animation at the same time, each with a weight, and its rendered pose is the weighted average of them. Two clips at half weight each produce a pose halfway between them; one clip at full weight produces exactly the pose that clip would have produced on its own.

**Why this priority**: this is the capability. A weight that is stored but never blended changes nothing the player sees, so holding weights and averaging them are one story, not two. It is also the slice that stands alone: an actor with a single clip continues to behave exactly as it did.

**Independent Test**: drive an actor with two clips at equal weight and compare the resulting pose against each clip sampled alone — translations and scales sit midway between the two, and the rotation is a rotation midway between them. Then drive the same actor with one clip at full weight and confirm its pose matches what the pre-feature single-clip renderer produced.

**Acceptance Scenarios**:

1. **Given** an actor driven with two clips at equal weight, **When** its pose is produced, **Then** each joint's translation and scale is the midpoint of the two clips' values and its rotation is a rotation halfway between the two.
2. **Given** an actor with a single clip at weight 1, **When** its pose is produced, **Then** it is identical to the pose the same clip produced before this feature existed.
3. **Given** an actor whose clips together carry weight *w*, **When** only the relative proportions of the weights change (for example 0.5/0.5 becomes 1.0/1.0), **Then** the pose is unchanged — weights are relative, not required to sum to 1.
4. **Given** an actor with no clip contributing — none held, or every weight at zero — **When** its pose is produced, **Then** the actor stands in its rest pose rather than collapsing to a degenerate pose.

---

### User Story 2 - A caller sets each clip's weight (Priority: P2)

The routine driving the actor sets a clip's weight at any time: bringing a clip up to full influence, easing one down towards zero, or balancing two against each other. Setting a weight on a clip the actor does not already hold adds that clip, so a caller names a clip rather than managing slots itself.

**Why this priority**: it is the interface this feature exists to expose, and the reason the work was asked for. It follows Story 1 because a weight with nothing to blend into is a number with no effect; once Story 1 stands, this is the handle on it.

**Independent Test**: on an actor holding one clip, set a second clip's weight to 1 and confirm both contribute in the same frame; ease the first clip to 0 and confirm its contribution is gone; request a weight above 1 or below 0 and confirm it is clamped; name a clip the asset does not define and confirm a loud failure.

**Acceptance Scenarios**:

1. **Given** an actor holding one clip, **When** a caller sets a second clip's weight, **Then** the second clip joins the actor and its contribution appears in the same frame's pose.
2. **Given** a clip at some weight, **When** a caller sets that weight to 0, **Then** the clip contributes nothing to the pose.
3. **Given** a caller sets a weight for a clip the actor already holds, **Then** the existing clip's weight is adjusted rather than a second copy being added.
4. **Given** a requested weight outside 0 to 1, **Then** it is clamped into range.
5. **Given** a requested clip the actor's asset does not define, **Then** the request fails loudly rather than being ignored.

---

### User Story 3 - The author sets an actor's animation slots in the Inspector (Priority: P3)

With an actor selected, the Inspector shows an **Animations** section: the clips the actor is currently blending, each with its weight; a control to bring another of the actor's clips into the set; and a control to take one out. The author adds a second clip, balances it against the first, watches the pose change where the actor stands, and removes the clip again.

**Why this priority**: it is the surface that makes the weights reachable by hand. Story 2 is the runtime driver — the game decides the weights — and this section drives the *same* state while an author is setting a scene up, so the two are one mechanism seen from two sides. It ranks below Stories 1 and 2 because the feature is complete without it, and above Story 4 because it is the visible, demonstrable half of the capability.

**Independent Test**: select an actor, add a second clip from those its asset defines, balance the two clips' weights, and confirm the actor's pose reflects the blend; remove a clip and confirm its contribution is gone and it leaves the list; remove the last one and confirm the section reports the actor holds none; confirm the three degenerate cases each report rather than fail silently.

**Acceptance Scenarios**:

1. **Given** an actor is selected, **When** the Inspector draws, **Then** an Animations section lists every clip the actor holds, each with its weight.
2. **Given** the actor holds fewer clips than its asset defines, **When** the author chooses an unheld clip and confirms, **Then** the clip joins the actor's set and appears in the list.
3. **Given** a clip in the set, **When** the author sets its weight, **Then** the actor's pose reflects the new weight; **When** the author removes the clip, **Then** it stops contributing and leaves the list.
4. **Given** a selection that is not an actor, **Then** the section is not shown.
5. **Given** the actor's asset defines no clips, or the actor already holds every clip its asset defines, or the actor holds the maximum number of clips, **Then** the section reports the condition rather than offering an action that cannot succeed.
6. **Given** the author adds, removes or re-weights a slot, **Then** no undo step is recorded and nothing authored about the actor changes — the previous scene edit is still the one Ctrl+Z reverses.
7. **Given** the actor holds one clip, **When** the author removes it, **Then** the actor is posed at its rest pose and the section reports that it holds no clips.

---

### User Story 4 - Each clip keeps its own clock (Priority: P4)

Every clip an actor holds advances independently: its own playback position, its own rate, its own loop setting. Two clips of different lengths or speeds blend without either being forced to the other's timing, and a non-looping clip holds its last frame while its neighbour loops.

**Why this priority**: it is what makes blending clips that do not share a timeline possible, and it is part of Story 1's mechanism rather than a separate gesture. It is ranked last because a first cut can blend clips that happen to be in step, and correctness for differing timelines follows.

**Independent Test**: hold two clips of different frame rates, advance the actor by one fixed step, and confirm each reached its own expected frame; hold a looping and a non-looping clip together and confirm one wraps while the other holds.

**Acceptance Scenarios**:

1. **Given** two clips with different frame rates, **When** the actor advances by a step, **Then** each clip's frame advances by its own rate and neither is affected by the other.
2. **Given** a looping clip and a non-looping clip held together, **When** playback passes the end of both, **Then** the looping clip wraps and the non-looping clip holds its final frame.
3. **Given** a clip whose weight is 0, **When** the actor advances, **Then** the clip's clock continues to run — a zero weight removes its contribution, not its playback.

---

### Edge Cases

- **The capacity is a hard limit.** An actor holds up to eight clips at once. Asking for a ninth — from code or from the Inspector — is a failure to report, not a silent drop of a clip the caller believes is playing.
- **A clip that carries no frames contributes nothing.** It is skipped rather than producing a zeroed pose for the joints it would have covered.
- **Rotations are not averaged as plain numbers.** Averaging the four components of two rotations does not generally give a rotation, so the blend must produce a valid rotation; equal weights between two clips give the rotation halfway between them.
- **Weights that do not sum to one are normal.** A caller balancing 2 against 1 gets the same pose as 0.66 against 0.33.
- **Blending clips of unequal length or timing is allowed.** Their poses are combined per joint; the clips need not share a frame count or rate.
- **A clip already held is adjusted, not duplicated.** Naming the same clip twice does not give it two clocks, and does not double its weight.
- **Everything at zero is a valid state, not an error.** An actor with no clip contributing — because it holds none, or the caller has zeroed them all — stands in its rest pose.
- **Identical inputs give the identical pose.** The same clips, weights and playback positions always blend to the same result.
- **Existing single-clip actors are untouched.** An actor using one clip at weight 1 must produce the same poses as the pre-feature renderer, so `specs/009-animated-character` and `specs/023-actor-skinning-hot-path` keep every assertion they already make.
- **A zero-weight clip stays in the set.** In the Inspector, setting a clip's weight to 0 leaves it listed as a held clip that contributes nothing; removing it is a separate action.
- **The add control offers only clips that can be added** — those the actor's asset defines and the actor does not already hold. When none is available, the section offers no add action rather than an empty choice.
- **Removing the last clip is a valid state, not an error.** The section reports that the actor holds no clips, and the actor is posed at its rest pose.
- **The section edits runtime state, so it is not restored by undo.** Undoing an actor's *deletion*, which rebuilds its renderer, returns it to the default single-clip state rather than to the slots it last held.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An actor MUST be able to hold more than one animation clip at the same time, each with its own weight.
- **FR-002**: An actor MUST be able to hold at least eight clips at once; asking for more MUST fail loudly rather than silently dropping a clip.
- **FR-003**: The actor's rendered pose MUST be the weighted average of its contributing clips: joint translations and scales averaged linearly by weight, and joint rotations combined into a valid rotation that is the weighted midway of the inputs.
- **FR-004**: Weights MUST be relative: only their proportions affect the pose, and a single contributing clip at any positive weight MUST produce exactly that clip's pose.
- **FR-005**: A caller MUST be able to set a clip's weight at runtime, including for a clip the actor does not yet hold, and the change MUST affect the pose produced in that same frame.
- **FR-006**: A requested weight MUST be clamped to the range 0 to 1 inclusive, where 0 removes the clip's contribution and 1 gives it full weight.
- **FR-007**: Setting a weight for a clip matching one the actor already holds MUST adjust that clip rather than add a second copy of it.
- **FR-008**: When no clip contributes — none held, or all weights zero — the actor MUST be posed at its rest (bind) pose rather than a collapsed or zeroed pose.
- **FR-009**: Each held clip MUST advance on its own clock, at its own rate and with its own loop setting, independently of every other held clip.
- **FR-010**: A clip name the actor's asset does not define MUST fail loudly when a weight is requested for it.
- **FR-011**: An actor that holds a single clip at weight 1 MUST produce the same pose it produced before this feature existed, and the existing animation behaviour and tests MUST be unchanged.
- **FR-012**: Producing the blended pose MUST NOT introduce per-frame allocations, preserving the guarantee `specs/023-actor-skinning-hot-path` records.
- **FR-013**: Choosing which clips to blend for a *gameplay* purpose — for example cross-fading between idle and walk — MUST be out of scope: this feature exposes weights, and the gameplay caller decides them. (The Inspector lets an author choose them by hand; it does not decide them automatically.)
- **FR-014**: The Inspector MUST show an Animations section when an actor is selected, listing every clip the actor holds with its weight, and MUST show no such section for any other kind of selection.
- **FR-015**: The author MUST be able to add a clip to the actor's set from the clips its asset defines and that the actor does not already hold, and to remove a clip from the set.
- **FR-016**: The author MUST be able to set a slot's weight in the section, and the actor's pose MUST reflect that weight.
- **FR-017**: The section MUST report, rather than silently offer an impossible action, each degenerate case: the actor's asset defines no clips, the actor already holds every clip its asset defines, and the actor already holds the maximum number of clips.
- **FR-018**: Removing a clip MUST leave the section showing the actor's remaining slots, and when the last one is removed the section MUST report that the actor holds no clips rather than showing an empty or broken control.
- **FR-019**: Slot edits from the Inspector MUST be runtime state: they MUST NOT be persisted, MUST NOT record an undo step, and MUST NOT be carried by an actor's captured authored state. Rebuilding an actor's renderer MUST yield the default single-clip state.
- **FR-020**: Every other Inspector and editor behaviour MUST be unchanged, and the section MUST use the same weight-setting mechanism FR-005 exposes rather than a second path.

### Key Entities *(include if feature involves data)*

- **Animation slot**: one clip held by an actor, with its own playback position, rate and loop setting, plus the weight that determines how much it contributes to the pose.
- **Actor's animation slots**: the bounded set (up to eight) of clips an actor holds at once. This is the thing weights are set on, the thing the Inspector lists, and the thing posed from.
- **Blended pose**: the per-joint transforms an actor renders, produced by combining all contributing slots by weight. It is the single output of the set, not one output per slot.
- **Rest (bind) pose**: the pose an actor falls back to when no slot contributes.
- **Inspector animation section**: the actor-only block in the Inspector that lists the selected actor's slots and offers the add, remove and weight controls.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Two clips at equal weight produce a pose that sits halfway between them at every joint — translations and scales at the midpoint within numerical tolerance, and rotation at the rotational midpoint (100% of joints).
- **SC-002**: A single clip at weight 1 reproduces the pre-feature pose exactly, and every existing animation assertion passes unmodified (100%).
- **SC-003**: Setting a clip's weight to 0 removes its contribution, and zeroing every clip yields the rest pose (0% contribution from a zeroed clip; 100% rest pose when all are zero).
- **SC-004**: An actor holds and blends up to eight clips; a request beyond that is reported as a failure rather than silently ignored (100% of over-capacity requests reported).
- **SC-005**: Blending adds no measurable per-frame memory growth: the existing no-allocation animation check still observes zero bytes allocated per frame (0 bytes/frame).
- **SC-006**: No behaviour changes for actors that hold a single clip: the existing animation and skinning suites pass with every assertion unchanged (100%).
- **SC-007**: With an actor selected, an author can add a clip, see it listed, set its weight and remove it, and the actor's pose reflects the weights at each step (100% of the four actions).
- **SC-008**: The section reports each degenerate case — no clips defined, all clips already held, at capacity — instead of offering an action that silently fails (100%).
- **SC-009**: Inspector slot edits record no undo step and persist nothing: after changing slots, Ctrl+Z reverses the previous scene edit and the actor's saved state is unchanged (0 steps, 0 persisted changes).

## Assumptions

- **The blend is part of the feature, not deferred with the gameplay logic.** "Blending idle and walk can wait" was read as the *gameplay* decision — which clips to mix, and when — being out of scope, while the *renderer* averaging whatever weights it is given is in scope. Storing a weight with nothing to average it into would deliver a number the player cannot see, so the two halves are one feature.
- **The Inspector section is a direct write to runtime state, not an undoable scene edit.** This follows the editor's authored / derived / runtime classification (`docs/EDITOR_DESIGN.md`, D-011): an actor's captured authored value deliberately carries no runtime state, and animation playback lives with the runtime renderer, so a command for it would have nothing it is allowed to hold. It is the same stance as the cast binding (D-022) and the zone registry (D-023), both direct writes because neither is a scene edit. As a consequence, slot edits are not persisted and are not restored by undo — animation is not saved at all today.
- **The section covers membership and weight; deeper controls are deferred.** Which clips are in the set, and each one's weight, is what this feature exposes. Scrubbing a slot's frame by hand, editing its playback rate, toggling its loop or enabled flag, and the reference editor's Play/Stop buttons are all deferred — `specs/009-animated-character`'s single clip plays on its own clock, and nothing here changes that.
- **The section is actor-only, and single.** It appears in the one Inspector (there is a single editing surface, one selection at a time), and only for an actor selection — a prop, part, waypoint, trigger or environment shows nothing of it.
- **Weights are relative, not fractional shares required to sum to one.** This matches how the reference engine blends, keeps callers from renormalising on every change, and is why a lone clip at any positive weight is exactly that clip.
- **The rest pose is the correct fallback when nothing contributes.** It is the pose the actor already has when no clip plays, and it is preferable to the current behaviour for a model with no clips, which leaves zeroed joint transforms.
- **A clip's clock keeps running at zero weight.** Resetting a clip's playback when its weight drops to zero is a cross-fade choice — part of the deferred gameplay logic — and is not assumed here. A caller that wants a clip to restart can restart it.
- **One slot per clip.** Naming a clip the actor already holds adjusts it; the alternative — several independent playheads of the same clip — has no use case this feature knows of, and the rule keeps weights unambiguous and the Inspector's add control well defined.
- **Capacity is eight, and exceeding it is a bug to report.** Eight is the limit asked for, and it is generous for the blends the game expects (a base motion plus a small number of overlays). A caller that exceeds it has made a mistake, so it fails loudly rather than evicting a clip.
- **Morph targets are a separate capability.** Blending shape-key weights beside animation weights exists in the reference engine and is deliberately not part of this feature.
- **The actor still animates the way it does today.** How a single clip is sampled and turned into the actor's animated skin is unchanged; this feature changes only how many clips feed the pose, how they are combined, and how an author reaches them.
