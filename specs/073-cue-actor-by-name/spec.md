# Feature Specification: Cue Actors Addressed by Name — a Scene-Driven Chooser

**Feature Branch**: `073-cue-actor-by-name`

**Created**: 2026-10-09

**Status**: Draft

**Input**: User description: "Cue actor ops address actors by name (a string) rather than the roster; the cue editor offers only actors present in the place being viewed and reads their animation/morph names from the placed actor's asset; `AddActor` names a discovered actor asset and an actor name instead of a character and look."

## Origin

`specs/072-actor-name-addressing` retired the actor cast role and the runtime cast index and resolved actors
**by name** — but it kept two things on the roster:

- the cue op argument stayed a `Roster.Character` (a compile-time convenience), converted to a name only at
  resolution time; and
- the editor kept offering the **whole roster** for an actor op, and read a character's animation-clip and
  morph names from the **cast binding** (`specs/072` FR-013).

Using it, two problems surfaced:

1. **The chooser is not the scene.** Selecting an actor in the cue editor offers all four roster members even
   when only one (Akane) is placed in the scene. The editor cannot tell the author what is actually there.
2. **Art hangs off the binding.** The clip/morph choosers resolve the character's asset through the cast
   binding, so a member with no bound asset offers no animation or morph names — even when an actor of her name
   is standing in the scene, which is the only thing the cue will actually resolve at run time.

This feature makes the cue vocabulary and its editor **scene-driven**: a cue op names an actor by a plain
**string** (her `Name`), resolved by the same scene scan `specs/072` already uses; the chooser lists only the
actors present in the place being viewed; and the clip/morph choosers read the **selected actor's** asset.
`AddActor` — which *creates* an actor, so it cannot pick one "already in the scene" — names a discovered
**actor asset** and an actor name instead of a character and a look.

The roster, the look vocabulary, the cast binding and the cast panel are **not** removed: they remain for the
**dialogue portrait path** (`specs/038`), which resolves a portrait from a character and a look through the
binding and never consulted an actor. This feature removes the roster from **cues**, not from the game.

## Clarifications

### Session 2026-10-09

- Q: Do actor cue ops keep carrying a `Roster.Character`, or a plain string? → A: **A plain string** (the
  actor's `Name`). This supersedes `specs/072` FR-004.
- Q: What does `AddActor` name, since it creates the actor? → A: **A discovered actor asset (by id) and an
  actor name**, plus a place and a waypoint; it names no character and no look. The editor offers discovered
  actor assets, and the name defaults to the asset's.
- Q: Does the roster (and the look binding, and the cast panel) leave the game? → A: **No.** They remain for
  the dialogue/portrait path; this feature removes them from the **cue vocabulary and cue editor** only.
- Q: Which place's actors does the actor chooser offer? → A: **The active place** — no existing-actor op
  names a place — as a combo over that place's live actors, mirroring the waypoint chooser (a typed field only
  when the place itself is absent).

## User Scenarios & Testing *(mandatory)*

Three parties use this: the **author**, who picks an actor in the cue editor and expects to see what is in the
scene; the **cue**, which resolves an actor by name at run time; and the **dialogue**, whose portraits must be
unaffected because they never used the cue vocabulary.

### User Story 1 - A cue names an actor by name, not by roster position (Priority: P1)

Every actor op names its actor by a plain string. Resolution is the scene scan the game already performs: the
first live actor whose `Name` matches, over the present places. Nothing about the op goes through the roster.

**Why this priority**: it is the change. The editor behaviour below follows from the op no longer being a
closed-set value.

**Independent Test**: author an actor op with an arbitrary name equal to a placed actor's `Name`, run the cue,
and assert the op acts on that actor; then assert `world/Cue.fs` names no roster value.

**Acceptance Scenarios**:

1. **Given** an actor named `Akane` in the scene, **When** a cue op names `Akane`, **Then** the op resolves
   that actor, or performs nothing when no actor has that name.
2. **Given** any actor op, **Then** its actor argument is a `string`, not a `Roster.Character`.
3. **Given** the cue vocabulary, **Then** it names no roster character, a look, or the cast binding.

---

### User Story 2 - The editor offers only what is in the place being viewed (Priority: P1)

The actor chooser lists the actors present in the place being viewed — the place the op names, or the active
place — rather than the roster. A typed fallback is kept, so a name may still be authored before the actor is
placed (as a waypoint name may).

**Why this priority**: it is the visible fix — no more offering four members when one is placed.

**Independent Test**: place one actor in the active place, open the cue editor's actor chooser, and assert the
list is exactly that actor — no other roster member appears.

**Acceptance Scenarios**:

1. **Given** only `Akane` placed, **When** the actor chooser opens, **Then** it offers `Akane` and no other
   roster member.
2. **Given** `AddActor` (which names a place but no existing actor), **When** its editor opens, **Then** it
   offers a discovered **asset** and a typed **name** rather than an actor chooser (US4).
3. **Given** a place that holds no actor, **When** the chooser opens, **Then** it offers no names (it shows
   `(none)`); a name is typed only when the place itself is absent, exactly as the waypoint chooser falls back.

---

### User Story 3 - Clip and morph names come from the placed actor's asset (Priority: P2)

The animation-clip and morph choosers read the asset of the **selected actor** — the actor of that name in the
place being viewed — so they populate whenever she is in the scene, with no cast binding required.

**Why this priority**: it is the second half of the fix — assignment no longer depends on the binding.

**Independent Test**: place an actor whose asset defines clips and morphs but bind no cast member; open a
blend op's choosers and assert they offer that asset's names.

**Acceptance Scenarios**:

1. **Given** an actor of the named actor in the place being viewed, **When** a blend op's chooser opens,
   **Then** it offers her asset's clip/morph names.
2. **Given** no actor of that name in the scene, **Then** the chooser offers nothing and the field falls back
   to typing.
3. **Given** the binding, **Then** it is not consulted by any cue chooser.

---

### User Story 4 - `AddActor` names an asset, and the rest of the cast is untouched (Priority: P2)

`AddActor` brings a member in by naming a discovered actor asset and an actor name, placing her at a waypoint
in a named place. The roster, the look vocabulary, the binding, the cast panel and the dialogue portrait path
are unchanged.

**Why this priority**: it closes the vocabulary change (`AddActor` was the one op that used a look) and bounds
the blast radius (portraits must keep working).

**Independent Test**: add an actor from the cue naming a discovered asset and a name, and assert the actor
appears with that asset and name; then run the dialogue and casting suites unmodified.

**Acceptance Scenarios**:

1. **Given** a discovered actor asset, **When** an `AddActor` op names it, an actor name, a place and a
   waypoint, **Then** an actor with that name and asset appears there.
2. **Given** an `AddActor` op, **Then** it names no character and no look.
3. **Given** the dialogue portrait path, **When** this feature lands, **Then** it resolves portraits exactly
   as before (binding-based, unchanged).

---

### Edge Cases

- **A name matching no actor**: the op performs nothing at run time. The chooser offers only the place's live
  actors, so a name that is not present is reached only by `AddActor`'s typed name (which may precede the
  actor) or by a value left by a deleted actor; it still displays and simply performs nothing.
- **Duplicate names**: permitted; the first match in place order wins, silently (as `specs/072`).
- **An actor in a present-but-inactive place**: the chooser (which offers the viewed place's actors) does not
  list her, but a cue whose op names her still resolves at run time, because the run-time scan is global. The
  editor is a convenience over the same resolution, not a gate.
- **A name equal to a placed actor's, added again**: `AddActor` is a pure spawn, so a second actor of that
  name appears (as `specs/072`); a name is a label.
- **An actor asset that defines no clips or morphs**: the choosers offer nothing for it; the field falls back
  to typing.
- **While editing**: cues do not run; only Play advances the cue (unchanged).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every cue op that names an actor MUST name it by a **`string`** — the actor's `Name` — and MUST
  NOT name a `Roster.Character`. This supersedes `specs/072` FR-004.
- **FR-002**: An actor op MUST resolve its actor as `specs/072` FR-003 (a scan of the present places for the
  first live actor whose `Name` matches), with the string used directly and no roster conversion.
- **FR-003**: `AddActor` MUST name a discovered **actor asset** (by id) and an actor **name**, plus a place and
  a waypoint. It MUST NOT name a look and MUST NOT consult the cast binding.
- **FR-004**: The cue vocabulary (`world/Cue.fs`) MUST NOT depend on `Roster` or on `Casting` (the look/binding).
- **FR-005**: For an op that references an existing actor, the editor's actor chooser MUST offer only the actors
  present in the **place being viewed** (the active place; no existing-actor op names a place) as a combo over
  that place's live actors, mirroring the waypoint chooser (a typed field only when the place itself is absent).
- **FR-006**: The animation-clip and morph choosers MUST read the **selected actor's** asset — the actor of the
  named name in the place being viewed — and MUST NOT read the cast binding.
- **FR-007**: The roster, the look vocabulary (`Casting.Look`), the cast binding, the cast panel and the
  dialogue portrait path MUST be unchanged. (`specs/032`, `specs/038`.)
- **FR-008**: `AddActor`'s editor surface MUST offer discovered actor assets (as the Asset Browser does) and an
  actor name (defaulting to the asset's), plus the place and the named place's waypoints.
- **FR-009**: Every other behaviour MUST be unchanged: the zone, trigger and camera ops, the dialogue view,
  rendering and physics produce identical output; the build is warning-free and the suite green, with no device.

### Key Entities *(include if data involved)*

- **Actor name**: a string — the actor's `Name` — the whole of a cue's address to an actor. A label, not an
  identity: nothing enforces it, and a duplicate resolves to the first match.
- **Actor asset**: a discovered model, named by id, that `AddActor` places; its clips/morphs are what the
  blend choosers offer.
- **Place being viewed**: the place a chooser reads its actors from — the op's named place, or the active place.
- **Roster / look / cast binding** (*unchanged*): the character identity, the look vocabulary and the
  `(character, look) → asset` binding. Retained for the dialogue portrait path; no longer used by cues.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No cue op names a `Roster.Character`, and `world/Cue.fs` names no roster or look value (0
  occurrences; verifiable by reading).
- **SC-002**: With one actor placed, the actor chooser offers exactly one actor (0 roster members offered),
  it being the placed one.
- **SC-003**: For an actor placed in the viewed place, the blend choosers offer her asset's names regardless of
  whether any cast binding exists (100%).
- **SC-004**: The dialogue and casting suites pass **unmodified** — portraits are unaffected (100%).
- **SC-005**: The project builds with no warnings and the suite is green, with no device opened.

## Assumptions

- **`AddActor` names an asset, not a look** (option A). It is the only op that creates an actor, so it is the
  only one that cannot pick one from the scene; naming a discovered asset is the odin shape and removes the
  binding from the cue vocabulary entirely.
- **The chooser reads the viewed place; resolution stays global.** The editor offers the place being viewed's
  actors, but the run-time scan is over all present places (as `specs/072`), so a cue authored by hand for an
  actor elsewhere still resolves. The editor is a convenience over the resolution, not a constraint on it.
- **Free-string actor names are accepted.** A misspelled or not-yet-placed name silently no-ops, exactly as a
  waypoint or trigger name does; the closed roster was never enforced at run time (`specs/072` already
  resolved by name).
- **The roster and binding stay for portraits.** They are removed from cues, not from the game; the cast panel
  remains the binding's authoring surface and the dialogue keeps resolving through it.
- **Superseded.** `specs/072` FR-004 (roster in cues) is withdrawn; `specs/072` FR-005 (binding for a first
  placement's art) and FR-013 (choosers read the binding) are re-based on the placed actor. `specs/032` and
  `specs/038` survive unchanged.
- **No op is added or removed.** The op set is unchanged; `AddActor`'s argument shape and every actor op's
  name type change.
