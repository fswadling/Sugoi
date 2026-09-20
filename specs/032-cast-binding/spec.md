# Feature Specification: The Cast — a Roster Hoisted from the Engine and Bound to Actor Assets

**Feature Branch**: `032-cast-binding`

**Created**: 2026-09-20

**Status**: Draft

**Input**: User description: "Introduce the concept of a cast to the game: the narrative
engine's roster of named characters, lifted to the game's own level, with the individual
members able to carry variant options — Akane might have a school-uniform model and a
casual-clothes model — and a Cast panel in the editor that assigns actor assets to the
individual cases, building up a binding from cast slot to asset."

**Origin**: The narrative engine declares the roster itself — `Character` in
`narrative/Vocabulary.fs` — while saying of it that visual identity is the game's and not
the engine's: "nothing here names a portrait, an asset or an actor". This feature makes
that division literal. The engine keeps its constructs and its traits; the roster, which
is content rather than construct, is declared once outside the engine, and the game binds
each member and variant to an actor asset discovered on disk. `docs/ROADMAP.md` phase 10
already reserves the shape for this ("`world/Cast.fs` — name → asset id map",
"`editor/CastPanel.fs`").

## Clarifications

### Session 2026-09-20

- Q: Should the roster and the engine's other content — its traits — be hoisted
  together? → A: No. Traits are the engine's construct and nothing binds them to an
  asset; only the roster moves.
- Q: Are a character's variants per-character types — so Akane's variants are a type of
  their own — or one shared variant vocabulary? → A: One shared vocabulary. Per-character
  variant types would put the roster's names in a second place, for no check that the
  binding does not already give.
- Q: How does the game know which variants a character actually has? → A: It does not
  need to. Not every member has every variant, and an entry in the binding *is* the
  existence check: a member with no entry for a variant has no model for it.
- Q: The engine's sources are compiled before the game's, which is what makes "the engine
  cannot reference the game" a compile error rather than a convention. Where can a hoisted
  roster then live? → A: Anywhere the engine can still see it, and the game can too — which
  is below the engine in compile order, not in the game's domain layer. Moving the engine
  itself was permitted, since it is consumed by nothing yet, but the outcome chosen keeps
  the engine's independence as a property of the build.

## User Scenarios & Testing *(mandatory)*

Three parties use this: the **engine**, which names characters in events, facts and rules
and must never know a model exists; the **author**, who decides in the editor which model
plays which character in which look; and the **binding**, which is what the author edits
and what a later consumer — placing a cast member, or the simulation acting on one — reads.

### User Story 1 - A cast declared once, and consumed by the engine (Priority: P1)

The developer has one declaration of who is in the story. It is not the engine's private
vocabulary and it is not the game's private list: the engine reads it to name characters
in its constructs, and the game reads it to offer them for binding. Adding a member is one
edit that both see, and there is nowhere a second list could be kept in step.

**Why this priority**: it is what makes the binding possible without duplication. Every
other outcome here reads this declaration; a second copy of it would make the binding and
the engine silently disagree about who exists.

**Independent Test**: add a roster member, build, and confirm the build reports every place
that must account for it while the game's cast code and the panel need no edit; then search
the solution for a second list of member names and find none.

**Acceptance Scenarios**:

1. **Given** the cast, **When** a developer asks for every member (for a rule, a test, or
   the panel), **Then** one declaration answers, and no second list of member names exists
   in the engine or in the game.
2. **Given** the cast, **When** a member is added, **Then** the engine and the game both
   see that member with no edit outside the rules that must account for it.
3. **Given** a member, **When** the engine commits an event naming her, **Then** she is an
   engine value and nothing about her model, asset, or look is involved.
4. **Given** the game, **When** an actor asset is discovered on disk, **Then** discovery
   adds and removes no roster member.
5. **Given** the engine's traits and constructs, **When** the roster is hoisted, **Then**
   they are unchanged and remain the engine's.

---

### User Story 2 - An author binds each character and look to an actor asset (Priority: P1)

The author opens the Cast panel and sees the cast as a tree: every member, and under her
the look vocabulary. Each leaf is a slot — one member and one look — and the author
assigns a discovered actor asset to it, or clears it. A member whose looks are only
partly assigned is visible as exactly that, and a member with no art at all is not an
error.

**Why this priority**: this is the feature's authoring value. It is what turns "there is a
cast" into "the game knows which model plays Akane in her school uniform".

**Independent Test**: open the panel, assign two different assets to two different slots of
one member, assign one asset to a second member, confirm each slot resolves to what was
assigned, then clear one and confirm it resolves to nothing.

**Acceptance Scenarios**:

1. **Given** the Cast panel, **When** the author opens it, **Then** the cast is a tree of
   every member, with the look vocabulary under each.
2. **Given** a slot, **When** the author assigns an actor asset, **Then** that slot
   resolves to that asset and the panel shows it assigned.
3. **Given** an assigned slot, **When** the author clears it, **Then** the slot is
   unassigned and the member has no model for that look.
4. **Given** a member with one look assigned, **When** the author views the panel,
   **Then** her other looks are present and visibly unassigned.
5. **Given** two members, **When** the author assigns the same actor asset to both,
   **Then** both resolve to it — a stand-in model is a legitimate assignment, not a
   conflict.
6. **Given** a slot, **When** the author chooses what to assign, **Then** only discovered
   actor assets can be chosen.
7. **Given** the binding, **When** the author assigns or clears a slot, **Then** no zone's
   undo history gains an entry.

---

### User Story 3 - The engine keeps its independence and its headless tests (Priority: P2)

The engine still cannot reach the game: it cannot name an asset, an entity, a renderer, or
the editor, and it is still exercised with no window, no graphics device, and no assets.
Hoisting the roster does not become the seam through which the engine grows a dependency on
the game.

**Why this priority**: it is the property the current compile order was chosen to give, and
the cheapest moment to keep it is while the roster is moving. It is P2 rather than P1 because
the authoring value above does not depend on it — but it is the reason the roster is hoisted
to where it is hoisted rather than into the game's own layer.

**Independent Test**: reference a game module from engine code and confirm the build fails;
run the engine's suite and confirm it needs no window, device, or asset.

**Acceptance Scenarios**:

1. **Given** the engine's sources, **When** a developer references a game module from them,
   **Then** the build fails.
2. **Given** the engine's tests, **When** they are run, **Then** they run with no window, no
   graphics device, and no assets.
3. **Given** the binding, **When** it changes, **Then** the engine's facts, history, and
   rules are untouched.

---

### Edge Cases

- A member with no looks assigned at all appears in the tree with every leaf unassigned.
  She is still nameable by the engine: identity is independent of art, and a member with no
  model is an authoring state rather than an error.
- A member with exactly one look assigned still shows the whole look vocabulary, so
  "she has one model" reads as one assigned leaf among unassigned ones rather than as a
  member with a shorter list. The panel offers the same leaves for every member.
- A member needing a look no other member has is expressible — she is the only one bound
  to it. This is why the vocabulary is shared rather than typed per member.
- Reassigning a slot replaces its binding; clearing it removes the entry. There is no third
  state in which a slot is assigned to nothing.
- A binding references an actor asset by identity. Since only discovered assets can be
  assigned, a slot cannot name an asset that does not exist.
- The roster and the look vocabulary are different kinds of thing: a member is an identity
  the engine reasons about, a look is a variant the game renders. A character with no art
  has no entries and no output, and the engine's facts about her are unaffected.
- The engine's existing facts, rules, and units produce identical output before and after
  the hoist; nothing about them depends on where the roster is declared.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The roster of cast members MUST be declared exactly once, outside the
  narrative engine, and the engine MUST consume that declaration — so there is no second
  list of members to keep in step, in the engine or in the game.
- **FR-002**: The engine's other vocabulary MUST remain the engine's: its traits, and its
  event and fact constructs, are not hoisted and are unchanged by this feature. The roster's
  *rendering* moves with the roster, so the engine reads a name through the hoisted layer
  rather than owning one.
- **FR-003**: Adding a roster member MUST be one declaration that both the engine and the
  game see, and the build MUST report every match over the roster that must account for the
  new member — which, because the engine's rules are roster-agnostic, is the roster's own
  rendering.
- **FR-004**: The hoist MUST NOT introduce a dependency from the engine onto the game. The
  engine MUST NOT be able to reference the game's assets, rendering, physics, entities, or
  editor, and this MUST be enforced by the build rather than by convention.
- **FR-005**: A character's looks MUST be a shared, closed vocabulary — a look (a variant)
  is one of a fixed set of values, the same set for every member — with no per-character
  look type and no look identified by a string.
- **FR-006**: Which looks a member has MUST NOT be declared anywhere other than the
  binding: a member has a look if and only if the binding holds an entry for that member
  and look.
- **FR-007**: The game MUST own a binding from a slot — one member and one look — to an
  actor asset, and MUST answer that lookup, including the absent case.
- **FR-008**: A slot MUST be assignable and clearable, and only discovered actor assets MUST
  be assignable, so a binding cannot name an asset that does not exist.
- **FR-009**: The editor MUST present the cast as a tree: the roster at the first level, the
  look vocabulary under each member, and one assignable leaf per slot.
- **FR-010**: Each leaf MUST show whether it is assigned, and an unassigned leaf MUST mean
  "this member has no model for this look" rather than signal an error.
- **FR-011**: The binding MUST NOT be part of a zone's undo history: it is authoring state
  that outlives any one zone, and assigning to it is not a scene edit.
- **FR-012**: Every existing behaviour MUST be unchanged: the engine's rules, facts, and
  history produce identical output, and the 3D render path, the gameplay simulation, and
  physics are untouched. The change adds editor UI only — no entity, no command, and no zone
  state. (Preserves `specs/025-narrative-core`.)
- **FR-013**: The engine's tests MUST continue to run headless — no window, no graphics
  device, no assets. (Constitution, N-V.)

### Key Entities *(include if feature involves data)*

- **Cast member**: one identity in the roster. A member is an identity and nothing else — no
  model, no asset, no look, no trait. The set is fixed and small (four today), and this
  feature neither grows nor shrinks it.
- **Look** (a variant): one named visual version of a character. A small shared set; a
  member may have any subset of it, including none. A look is a value, not a name.
- **Slot**: one member and one look — the unit an actor asset is assigned to, and the
  unit the binding is keyed by.
- **Cast binding**: the map from slot to actor asset. Sparse by design: an absent entry is
  the statement that no model exists for that slot, and is also the existence check.
- **Actor asset**: an existing concept — a model discovered on disk and loaded at start-up.
  The binding references one by its identity, and the cast does not own it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Exactly one declaration of the roster exists in the solution (1 occurrence;
  verifiable by reading, not by running).
- **SC-002**: Adding a roster member compiles with zero edits to the game's cast code and to
  the panel, and the build reports each place that must account for the member (100%).
- **SC-003**: Every slot in the roster × look cross product can be assigned, reassigned,
  and cleared from the panel, and an assigned slot is distinguishable from an unassigned one
  in every case (100%).
- **SC-004**: No slot can be assigned an asset that was not discovered (0 violations;
  verifiable by reading the panel, which offers only discovered assets).
- **SC-005**: Referencing a game module from the engine fails the build (0 engine → game
  references).
- **SC-006**: The engine's existing suite passes unmodified and headless (100%).
- **SC-007**: No per-frame path gains work, the project's frame-rate target is unaffected,
  and gameplay, rendering, and physics behaviour are unchanged (behaviour tests pass
  unmodified).

## Assumptions

- **The "user" is the author working in this codebase.** The panel is theirs, the roster is
  content they own, and the engine is the consumer they must not disturb. This is a
  developer-facing spec in the register of `specs/031-entity-authored-state`.
- **The roster is hoisted because it is content, not because the engine has become
  generic.** The engine's constructs, rules, units, and traits are untouched; only the
  declaration moves. Making the engine generic over a character type was considered and is
  not this feature: it would undo the closure `specs/025-narrative-core` chose deliberately,
  and it buys reusability across stories that nothing here asks for.
- **The engine's independence is a property of the build, not a convention.** Where the
  hoisted roster sits, and what that layer is called, is a plan decision; what this spec
  fixes is that the engine can see it and the game can see it.
- **The look vocabulary is shared and small.** A member needing a look no other member has
  is still expressible — she is the only one bound to it — which is why per-character look
  types are not needed.
- **Persistence is out of scope.** The binding is session state, as loaded assets are. A slot
  is expressible as a member and a look, so a later persistence feature can store one
  without re-modelling it, and the hoisted roster's names are the natural key.
- **Placing a cast member, and recording a character on a placed entity, are out of scope.**
  This feature delivers the declaration, the binding, and the lookup. Who consumes the lookup
  — a placement action, the simulation, or a cue — is the integration that follows.
- **The panel joins the editor's existing docked panels**, as a tab beside the Outliner, per
  `docs/EDITOR_DESIGN.md`; it assigns in place rather than through the Inspector, which that
  document records as D-022. Its dock position is built in native code, so it is the one part
  needing a native rebuild — the panel works without it. The editor's other panels and its
  undo granularity are otherwise untouched.
- **Alternative considered and rejected**: making each roster case carry its own look
  payload, so a member would be constructed *with* a look. It fuses "who" and "which
  model" into one value, so an engine fact about a character would carry her clothes — change
  her clothes and every fact about her becomes a fact about a different participant. Recovering
  a look-free identity from it would then need either a second roster or names parsed back
  into members: the duplication, and the string-keyed identity, that a single declaration
  exists to prevent.
