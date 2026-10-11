# Feature Specification: Actors Addressed by Name — Retiring the Cast Role and its Index

**Feature Branch**: `072-actor-name-addressing`

**Created**: 2026-10-09

**Status**: Draft

**Input**: User description: "Remove the actor's cast role and the runtime cast index; address actors in cue ops by name resolved through the roster; rename the `SetActor` cue op to `AddActor`."

## Origin

`specs/063-cast-index` gave a placed actor a **cast role** (`ActorState.Cast`, a `Roster.Character option`)
and gave `GameplayState` a **cast index** (`Character → Place * EntityRef`) so that "which actor plays Aiko?"
was answerable in one lookup, one character at most across all zones. `specs/064-actor-cue-ops` then built
the actor cue ops on that index, and `specs/066-actor-motion-cue-ops` extended them.

Reviewing it, two things stand out:

- The cast role is a **second identity** for an actor. Every other addressable entity — a waypoint, a
  trigger — is addressed by its one identity, the entity `Name`, found by a scan
  (`Zone.tryWaypointByName`, `Zone.tryTriggerByName`). The role exists only to key the index.
- The index is a lookup **maintained by hand** at every site an actor comes into or leaves existence:
  `Spawning.spawnActor`, `History.apply`/`undo` for `ActorCreate`/`ActorDelete`, `deleteZone`, and the
  zone-delete restore. That is five or six places that must agree, where the rest of the codebase has
  none.

This feature retires both. An actor is addressed by its **name**, resolved within a zone by the same kind
of scan waypoints and triggers use; a cue op still *names* a `Roster.Character` (the closed roster), and
resolution converts it to that character's canonical name and looks for the actor of that name. Nothing
about the roster, the look/binding, the cast panel, or the dialogue portrait path changes: portraits
through the **binding** (`Casting.tryAsset (character, look)`), which never consulted the actor. The cast
panel keeps its assignment behaviour; only its **spawn** control stops recording a role.

The set op is renamed to **`AddActor`**, matching the reference lineage's name for a placement op — and,
with the uniqueness constraint withdrawn, this feature also reverts `specs/064`'s unification of odin's add
and warp into one set: `AddActor` is a pure spawn, and moving a member is a removal followed by an add.

## Clarifications

### Session 2026-10-09

- Q: Does the roster go too, or only the index and the role? → A: **Only the role and the index.** The
  roster stays the closed set a cue names, and the binding stays the portrait/art source; both are read
  as they are today.
- Q: How is a cue op's character resolved to an actor? → A: **By name.** The op keeps carrying a
  `Roster.Character`; resolution converts it to its canonical name and finds the actor whose `Name`
  matches — the same pattern as a waypoint or trigger name, no index.
- Q: Is the portrait path affected? → A: **No.** Portraits resolve through the binding, from a character
  and a look, with no actor involved (`DialogueRender.tryPortrait`). Keeping the roster and the binding
  keeps the dialogue system exactly as it is.
- Q: What is the creation op called? → A: **`AddActor`** (was `SetActor`), the reference lineage's name.
- Q: What happens to the "one character, one actor" invariant? → A: **It is dropped.** A name is a label,
  not an identity (as `specs/033-cast-spawn` already held for names); duplicates are permitted and a
  lookup resolves the first match. This supersedes `specs/063` FR-003 and FR-007.
- Q: Does `AddActor` move an existing actor, as `SetActor` did, or always create one? → A: **Always create.**
  `AddActor` is a pure spawn — it does not look for an existing actor — because the uniqueness constraint is
  being removed, so a cue that re-runs may place a second actor of the same name. Moving a member is a
  `RemoveActor` followed by an `AddActor`. This reverts `specs/064`'s add/warp unification.
- Q: When a rename orphans a cue that named the actor, should the editor guard against it? → A: **No.** A
  rename may leave a cue pointing at nothing, exactly as a waypoint or trigger rename already does for its
  ops; no warning or lock is added. The op's existing run-time "performs nothing" diagnostic is the signal.
- Q: When more than one actor has the named character's name, does a resolution op act or stay silent? →
  A: **Act, silently.** It acts on the first match and records nothing; first-wins is the whole rule, with
  no ambiguity diagnostic.
- Q: With the cast index gone, where do the cue panel's clip/morph choosers get a character's animation and
  morph names? → A: **From the cast binding alone** — the named character's asset via the first bound look
  in `Casting.allLooks` order. A placed actor is not consulted, and an unbound character offers no
  suggestions.

## User Scenarios & Testing *(mandatory)*

Three parties use this: the **author**, who names a character in a cue and expects the actor of that name
to be found; the **dialogue**, which resolves a portrait from a character and a look and must be
unaffected; and the **scene**, which holds anonymous named actors and no longer carries a role on any of
them.

### User Story 1 - An actor is found by its name, with no index (Priority: P1)

A cue op names a character. The scene is walked for the actor whose name is that character's name — across
the present places — and the op acts on it. No lookup table tracks which actor is which; the actor's name
is the whole of its addressability, exactly as a waypoint's or a trigger's name is.

**Why this priority**: it is the change. Every other outcome here follows from the actor being found by
name rather than by a maintained index.

**Independent Test**: place an actor named for a character, author a cue that names her, run it, and
assert she is found, placed and addressed; then assert no cast index exists on the gameplay state.

**Acceptance Scenarios**:

1. **Given** an actor named for a roster character, **When** a resolving op names that character, **Then**
   the op finds that actor (by name), or performs nothing when no actor has that name.
2. **Given** an actor of a character's name in a different place, **When** a resolution op (removal,
   movement, a blend, or a player toggle) names her, **Then** she is found by scanning the present places.
3. **Given** no runtime cast index in the gameplay state, **When** any resolving actor op runs, **Then** it
   finds its actor by name and succeeds.
4. **Given** two actors of the same name, **When** an op resolves that name, **Then** it acts on the first
   match and does not raise (a name is a label, not an identity).

---

### User Story 2 - The portrait and binding paths are untouched (Priority: P2)

The dialogue still turns a character and an expression into a portrait through the cast binding — the
character's look resolves to an actor asset, and the asset carries its portraits. None of this ever went
through the actor's role or the index, and none of it changes.

**Why this priority**: it is the property that makes this change safe to make. If the portrait path
depended on the role, retiring the role would break the dialogue; proving it does not is the regression
guard.

**Independent Test**: for every (character, look) the binding covers, resolve the portrait before and
after and assert it is identical; run the dialogue suite unmodified.

**Acceptance Scenarios**:

1. **Given** a binding from a character and look to an actor asset, **When** a portrait line is drawn,
   **Then** the portrait resolves through the binding exactly as before.
2. **Given** the cast panel, **When** the author assigns or clears a slot, **Then** behaviour and state
   are unchanged.
3. **Given** the roster, **When** a cue op names a character, **Then** the argument is still the closed
   `Roster.Character`, not a free string.

---

### User Story 3 - The creation op is `AddActor`, and creates an actor (Priority: P3)

The op that brings a member into a place is renamed `AddActor`. It always creates an actor — named for the
character, placed at the named waypoint, drawn from the binding for her look — and never moves one. Moving a
member is an explicit removal followed by an add, so a cue's placement is exactly what it says.

**Why this priority**: it is the vocabulary change this feature records. It is P3 because the addressing
change above does not depend on it, but it is the visible half of withdrawing the uniqueness constraint: the
op no longer quietly reconciles with an actor that is already there.

**Independent Test**: author an `AddActor` op, run it twice, and assert two actors of the character's name
exist; then confirm the op appears under no other name anywhere.

**Acceptance Scenarios**:

1. **Given** a character, **When** an `AddActor` op names her, a place and a look, **Then** an actor named
   for her is created in that place, at the op's waypoint, drawn from the binding for her look.
2. **Given** an actor of her name already present, **When** the op runs again, **Then** a second actor of
   that name is created — a name is a label, not an identity — and the earlier actor is left untouched.
3. **Given** a cue that must move a member, **When** it removes her and adds her in the new place, **Then**
   one actor of her name remains, at the new placement.
4. **Given** the cue panel, **When** the author adds an actor op, **Then** the choice reads "Add actor" and
   carries a character, a look, a place and a waypoint, as before.

---

### Edge Cases

- **An actor renamed so a cue no longer resolves by name**: the op performs nothing (the actor it named is
  gone), reporting it through the op's existing "performs nothing" diagnostic. This is the same fragility a
  waypoint or trigger rename already has for its ops, and is accepted rather than guarded against.
- **A cue naming a character the scene has no actor for**: performs nothing, as every actor op already
  does for state it cannot act on.
- **Duplicates**: two actors of one name are a legitimate scene; resolution takes the first match in slot
  order, then place order. No error, no diagnostic, no numbering.
- **An actor not in the roster's name-space** (spawned from the Asset Browser with any name): it can never
  be named by a cue, because a cue names only a roster character; it is otherwise an ordinary actor.
- **A zone holding actors is deleted and its delete undone**: the actors come back with the zone; nothing
  re-indexes, because there is nothing to index.
- **A cue that must move a member to another place**: it removes her and adds her there; `AddActor` alone
  would leave the earlier actor where it is, leaving two actors of the same name.
- **A cue that re-runs an `AddActor`**: it places a second actor of that name — the uniqueness that once
  prevented this is gone. A cue that must not duplicate pairs the add with a `RemoveActor`.
- **While editing**: cues do not run, so no actor op fires; only Play advances the cue (unchanged).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An actor MUST NOT carry a cast role. The actor's addressability MUST be its entity `Name`
  alone; `ActorState.Cast` (spec 063) MUST be removed.
- **FR-002**: A gameplay state MUST NOT hold a runtime cast index. The map from a character to the actor
  playing her (spec 063 `GameplayState.Cast` and its accessors) MUST be removed.
- **FR-003**: An actor-naming cue op that must act on an existing actor — removal, movement, a blend, a
  player toggle — MUST resolve it **by name**: it converts the named `Roster.Character` to that character's
  canonical name and scans the present places for the actor whose `Name` matches, taking the first match.
  Resolution MUST NOT consult any index. (`AddActor` does not resolve: it always creates.)
- **FR-004**: The roster MUST remain the closed set of names a cue may write. A cue op's character argument
  MUST stay a `Roster.Character`; no free string for *who* is introduced. (A no-change requirement:
  `cast/Roster.fs` and the op argument types are untouched. Preserves `specs/032`.)
- **FR-005**: Character-to-asset resolution MUST continue to go through the cast binding
  (`Casting.tryAsset (character, look)`) — for a first placement's art and for dialogue portraits — and
  MUST NOT change. The look vocabulary, the binding, and the cast panel's **assignment** behaviour MUST be
  unchanged (the only part of the panel this feature touches is its spawn path, FR-010). (Preserves
  `specs/032` and `specs/038`.)
- **FR-006**: The story/creation cue op MUST be named **`AddActor`** and MUST always **create** an actor —
  named for the character, placed at the named waypoint, drawn from the binding for the named look. It MUST
  NOT look for an existing actor by name: with the uniqueness constraint withdrawn, a second `AddActor` of a
  character places a second actor of that name. Moving a member MUST be expressed as a removal followed by
  an add; no op relocates or re-skins an existing actor in place. An `AddActor` it cannot carry out — the
  named look resolves no art in the binding, or the named waypoint does not resolve — MUST perform nothing,
  as it already does. (Supersedes `specs/064`'s `SetActor` and its add/warp unification.)
- **FR-007**: Removal and the movement, blend and player-toggle ops MUST resolve their actor as FR-003 (by
  name, over the present places, first match) and MUST keep their current argument shape — they MUST NOT gain
  a place argument.
- **FR-008**: A name MUST be a label, not an identity: duplicate actor names MUST be permitted, and a
  lookup MUST resolve the first match silently — it MUST NOT raise and MUST NOT report the ambiguity. No
  uniqueness constraint MUST be enforced at any layer. (Supersedes `specs/063` FR-003, FR-007.)
- **FR-009**: Every path an actor comes into or leaves existence MUST require no index maintenance: a
  create, a delete, an undo or redo of either, a zone delete, and a zone-delete undone MUST be complete
  without touching any cast bookkeeping. (Supersedes `specs/063` FR-004.)
- **FR-010**: The spawn path that placed a cast member from the cast panel MUST no longer record a role or
  check for a duplicate; it MUST place an actor named for the character, exactly as the Asset Browser places
  one. (Restores `specs/033`'s "names are labels".)
- **FR-011**: The editor surfaces whose subject was the removed role or index MUST be removed: the
  Inspector's character readout (reads `ActorState.Cast`), the Outliner's per-zone cast tag (reads the
  index), and the Scenario panel's per-scenario cast tag (reads the stored actors' roles).
- **FR-012**: Every other behaviour MUST be unchanged: the zone, trigger and camera ops, the dialogue
  view, the binding, rendering and physics produce identical output (behaviour tests pass modulo the changes
  above).
- **FR-013**: The cue panel's animation-clip and morph choosers MUST take their suggestions from the cast
  binding alone — the named character's asset via the first bound look in `Casting.allLooks` order — and MUST
  NOT consult a placed actor. An unbound character offers no suggestions, and the field falls back to typing,
  as before.

### Key Entities *(include if data involved)*

- **Roster character**: an identity from `Roster.Character`, the closed roster — *who* a cue may name. The
  name it renders is the key an actor is resolved by; the roster is unchanged.
- **Actor**: a zone entity that is addressed by its `Name`. It carries no role; two actors may share a
  name and are then indistinguishable to a cue.
- **Actor name**: the addressing key. A label, not an identity — nothing enforces it, and a rename changes
  what a cue resolves.
- **Cast binding**: the game's sparse map from a member-and-look to an actor asset (`Casting.tryAsset`),
  unchanged; it is what a portrait and a first placement resolve art through.
- **Cast index** (*removed*): the gameplay state's map from a character to the actor playing her, with its
  one-character-across-all-zones invariant, is deleted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No runtime cast index and no actor cast role exists anywhere in the codebase (0 occurrences;
  verifiable by reading).
- **SC-002**: A cue naming a character can place a new actor of that name, act on the actor of that name
  (removal, movement, a blend, a player toggle), or remove her again, with a resolution that consults no
  index (100%).
- **SC-003**: For every (character, look) the binding covers, the resolved portrait and the asset a first
  placement uses are identical before and after (100%; the dialogue and casting suites pass unmodified).
- **SC-004**: A create, delete, undo, redo, zone delete and zone-delete-undo complete with no cast
  bookkeeping, and the affected suite passes with no cast-index assertions (100%).
- **SC-005**: The project builds with no warnings and the suite is green, with no device opened.
- **SC-006**: An `AddActor` run twice places two actors of the same name (100%), showing the uniqueness
  constraint is fully withdrawn.

## Assumptions

- **`AddActor` is a pure spawn.** Renaming `SetActor` to `AddActor` records the reference lineage's name,
  and reverts `specs/064`'s add/warp unification: `AddActor` always creates an actor and never moves an
  existing one. Because the uniqueness constraint is withdrawn, a cue re-run may place duplicates; a cue
  that must be idempotent pairs the add with a `RemoveActor`. A distinct warp/move op (odin's `WarpActor`) is
  deliberately not introduced here; a later feature may add one.
- **Resolution scans the present places.** The ops that resolve an actor by name (removal, movement,
  blends, player toggles) name no place, so they scan the present places in order; the first match wins.
  (`AddActor` does not resolve at all: it always creates.) The zone count is small (a closed `Place` union),
  so a scan is cheap and needs no index.
- **A rename can break a cue, and that is accepted.** With the role gone there is nothing linking an actor
  to a roster name except the name string, so renaming an actor (spec 027) makes a cue that named it
  perform nothing. This is the same fragility a waypoint or trigger rename already has, and this feature
  adds no guard against it.
- **The roster stays hoisted exactly as `specs/032` left it.** The engine still reads the same single
  declaration; this feature touches the game's use of the roster, not the roster's place in the build.
- **The looks, the binding and the dialogue portraits are unchanged; the cast panel keeps its assignment
  behaviour.** The binding never consulted the actor's role, so portraits are provably unaffected, and the
  panel remains the binding's authoring surface. The only part of the panel this feature touches is its
  **spawn** control, which stops recording a role (FR-010).
- **Two editor readouts are dropped, not re-pointed.** The Outliner's per-zone cast tag and the Scenario
  panel's per-scenario cast tag summarised the index and the role; with both gone their subject no longer
  exists. Re-pointing them at "the actors here" would invent a new affordance rather than preserve one, so
  they are removed (Minimalism, Constitution I).
- **Superseded specs.** `specs/063-cast-index` is superseded (its index and its uniqueness invariant are
  withdrawn); `specs/064-actor-cue-ops` is superseded in part (its `SetActor` is renamed `AddActor`, is a
  pure spawn rather than an add/warp union, and resolves by name); `specs/066-actor-motion-cue-ops` is
  preserved with resolution re-based on name; `specs/032-cast-binding` and `specs/038-dialogue-display`
  survive unchanged.
- **The narrative engine is untouched.** It reads the same roster declaration it read before; nothing it
  owns — events, facts, rules — is affected. This feature changes how the *scene* addresses an actor, not
  who the engine believes exists.
- **No cue op is added or removed.** Only `SetActor` is renamed; the op set is otherwise identical.
