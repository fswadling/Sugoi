# Feature Specification: Retire the Cast — Actors and Speakers Addressed by Name

**Feature Branch**: `074-retire-cast`

**Created**: 2026-10-10

**Status**: Draft

**Input**: User description: "Retire the cast entirely. Remove the look vocabulary, the slot → asset binding, `SharedState.Cast` and the Cast panel, and retire the roster too — the closed `Character` union moves back into the narrative subsystem so the engine still compiles. Portraits are connected in the asset manager: a portrait line's speaker is a plain string (her name) that resolves to her actor asset by name. Speaker choosers offer discovered actor names. Option B, roster included."

## Origin

`specs/072-actor-name-addressing` and `specs/073-cue-actor-by-name` removed the actor's cast role, the runtime
cast index, and the roster from the **cue vocabulary**: a cue now addresses an actor by a plain string (her
`Name`), resolved by a scan of the present places. What remained of the cast was an awkward bolt-on:

- `world/Casting.fs` — the `Look` vocabulary, the `Slot { Character; Look }` key and the `Bindings` map. After
  the two features above, its only runtime consumer was `world/DialogueRender.tryPortrait`, resolving a portrait
  line's speaker by walking the binding's looks for the first bound asset; its only other consumer was
  `editor/CastPanel.fs`, whose "Spawn" the Asset Browser already duplicates.
- `cast/Roster.fs` — the closed `Character` union, shared by the narrative engine and the game. The game used it
  only as the dialogue speaker (`Dialogue.Kind.PortraitDialogue`), the two speaker choosers, and the binding it
  keyed.

Both are redundant. Portraits are **declared by the actor asset** — the manifest's `[Portraits …]` block, read
into `ActorAsset.Portraits` (`specs/038`, `world/Assets.fs`) — and an asset's `Name` is already the character's
name (the manifest declares `[Name "Akane"]`). The binding adds only a `Character → asset id` lookup and drags
in the `Look` dimension portraits never used; the roster is a closed set the game no longer needs, because a
speaker can be a plain string like every other name in the game.

This feature **retires the cast from the game**: the look vocabulary, the slot/binding map, `SharedState.Cast`
and the Cast panel are removed, and the **roster leaves the game** — the closed `Character` union relocates into
the narrative subsystem (its only remaining owner, the engine), which keeps compiling and loses nothing. A
portrait line's speaker becomes a **plain string** (the actor's name), resolved to her actor asset **by name** in
the asset manager, exactly the by-name resolution the prior two features established for actors.

A third thing the by-name work left behind is on the play path. The cue's **in-flight actor ops** —
`MoveActorState`, `RotateActorState`, `BlendState` — re-resolve their actor **every tick** through
`tryResolveActor`, which scans the present places' actor slot arrays for the actor's `Name` on each frame. That is
the cost the removed cast index used to answer in O(1), and the resolve returns an `option`/tuple, so it allocates
per tick while the op runs. This feature also captures the resolved slot **handle** once, at `begin*`, and carries
it in the `…State` so each tick is an O(1) lookup that reuses the actor the op began on — and allocates nothing.

## Clarifications

### Session 2026-10-10

- Q: Does the cast *selection* actually do anything portraits need? → A: **No.** Portraits are declared by the
  actor asset; the binding is only a `Character → asset id` lookup that ignores the look it computes. It is
  retired.
- Q: What replaces the binding for a portrait line? → A: **A name lookup in the asset manager**: the speaker (a
  plain string) resolves to the actor asset whose declared name is that string, and that asset's `Portraits`
  supply the portrait (as `specs/038`). No assignment step, no look.
- Q: What identifies the actor asset a speaker resolves to, and is the match case-sensitive? → A: **The asset's
  declared `Name`, matched exactly (ordinal)**, the lowest-`Id` asset winning a tie. An asset that omits `Name`
  falls back to its `Id`.
- Q: How should the lookup avoid per-frame allocations? → A: **A name → actor-asset index built once when the
  assets are scanned**, so a resolve is a dictionary hit with no per-frame allocation (`specs/038` FR-010).
- Q: Does retiring the roster happen in this feature, and where does the `Character` union go? → A: **Yes.**
  `cast/Roster.fs` is removed from the game; the closed `Character` union moves into the **narrative subsystem**
  (engine-only). The game's `PortraitDialogue` speaker becomes a plain **string**.
- Q: What does a portrait line's speaker chooser offer now there is no roster? → A: **The discovered actor
  asset names**, with a typed fallback — so a speaker resolves regardless of whether the actor is in the scene,
  as the roster combo did.
- Q: The cue's in-flight actor ops re-resolve their actor by name every tick (a scan, and an allocation). Should
  that change? → A: **Yes.** `MoveActorState`/`RotateActorState`/`BlendState` capture the resolved slot **`Handle`**
  and its `Place` at `begin*`; each tick looks the actor up in O(1) and reuses it. The one-shot ops (`RemoveActor`,
  `EnablePlayer`/`DisablePlayer`, and the `begin*` resolutions) keep the name scan.
- Q: What happens if the actor or her place leaves the scene mid-flight? → A: **The op ends**, as today: an absent
  place, a dead/recycled slot, or a slot now holding a different name ends it. A same-named newcomer does **not**
  take over the running op — it stays on the actor it began with.

## User Scenarios & Testing *(mandatory)*

Five parties: the **asset author**, who declares a character's portraits in her actor asset and expects a
portrait line for that name to find them; the **dialogue view**, which resolves the speaker string to her asset
by name; the **editor user**, who picks a speaker from the discovered actors and no longer has a cast panel to
set up; the **narrative engine**, whose vocabulary the relocated `Character` union still serves; and the **cue
runtime**, whose in-flight actor ops resolve their actor once and reuse it rather than scanning each tick.

### User Story 1 - A portrait line's speaker is a string, resolved by name (Priority: P1)

A portrait line names a speaker and an expression. The speaker is a plain **string** — the actor's name. The game
finds that actor asset by name and draws the asset's declared portrait for the expression. Nothing has to be
assigned first, and no roster membership is required.

**Why this priority**: it is the change. The binding exists only to serve this lookup, and the roster exists in
the game only to name the speaker; re-basing the speaker on a string is what lets both go.

**Independent Test**: build a state whose asset manager holds an actor asset named for a speaker, show a portrait
line naming that speaker and an expression, and assert the asset's declared portrait for that expression is what
the view resolves — with no binding and no roster present.

**Acceptance Scenarios**:

1. **Given** `PortraitDialogue(speaker, expression)` whose speaker is a string, **Then** it is drawn from the
   asset whose declared name is that string, with no binding and no roster involved.
2. **Given** no asset of that name, **Then** the line is left undrawn and the authoring panel shows why.
3. **Given** that asset declares no portraits, **Then** the line is left undrawn and the authoring panel shows
   why (a character with no portraits cannot speak a portrait line, as `specs/038`).

---

### User Story 2 - The cast leaves the game: no binding, no look, no panel, no roster (Priority: P1)

There is no `Casting` module, no look vocabulary, no `Slot`/`Bindings` map, no `SharedState.Cast`, no Cast panel,
and no `cast/Roster.fs` in the game. The closed `Character` union moves into the narrative subsystem, which
keeps compiling and behaving exactly as before. Nothing in the game assigns an actor asset to a member-and-look,
and placing an actor works from the Asset Browser and the cue `AddActor`.

**Why this priority**: it is the removal the change exists for. It is testable independently of US1's resolution
detail: the code and the panel simply no longer exist, and the engine still builds.

**Independent Test**: assert `Casting`/`Slot`/`Bindings`/`Look` and `SharedState.Cast` are referenced nowhere in
the game, that `cast/Roster.fs` is gone and the game names no `Roster` value, that the editor has no "Cast"
window, and that the narrative engine compiles and its suite passes with the relocated union.

**Acceptance Scenarios**:

1. **Given** the codebase, **Then** no game module named `Casting`, no `Look`/`Slot`/`Bindings`/`Cast` type, and
   no `SharedState.Cast` field exist.
2. **Given** the roster, **Then** it lives in the narrative subsystem, the engine's suite passes, and no game
   file references it.
3. **Given** the editor's windows, **Then** there is no "Cast" panel, and the remaining panels still lay out.
4. **Given** the Asset Browser, **When** an actor asset is chosen, **Then** the actor is placed as before.

---

### User Story 3 - Speakers are chosen from discovered actors, and nothing else changes (Priority: P2)

Both speaker choosers — the cue `Exposit` editor and the Dialogue panel — offer the **discovered actor asset
names** (with a typed fallback), not a roster. The name plate shows the speaker string. Every other behaviour is
unchanged: zones, cues, the dialogue view's layout and presentation, rendering and physics produce identical
output.

**Why this priority**: it closes the vocabulary change (two combos read the roster today) and bounds the blast
radius.

**Independent Test**: with two actor assets discovered, open a speaker chooser and assert it offers their names
and no roster member; then assert the dialogue layout tests pass with the same geometry.

**Acceptance Scenarios**:

1. **Given** discovered actor assets, **When** a speaker chooser opens, **Then** it offers their names, and a
   name may be typed when none is discovered.
2. **Given** a portrait line, **Then** its name plate is the speaker string and a plain dialogue/thought draws
   exactly as before.
3. **Given** the removal, **Then** the project builds with no warnings and the suite is green, with no device.

---

### User Story 4 - An in-flight actor op resolves her once, not every tick (Priority: P2)

A move, a turn or a blend resolves its actor **once**, when the op begins, and carries the resolved slot with it
instead of scanning by name on every frame. The op reuses the actor it started on; if she — or her place — leaves
the scene, the op ends, exactly as a name miss does today.

**Why this priority**: it removes the one per-frame cost the by-name work put on the play path, and makes the step
allocation-free. It ranks below the cast retirement itself because that is the feature; this repairs the hot path
it touched.

**Independent Test**: begin a move, then assert the op keeps acting on the same actor (a same-named newcomer does
not steal it), ends when the actor is removed, and adds no allocation per tick.

**Acceptance Scenarios**:

1. **Given** an in-flight `MoveActorState`/`RotateActorState`/`BlendState`, **Then** it carries the actor's slot
   **`Handle`** and **`Place`**, resolved once at `begin*`.
2. **Given** the same op, **Then** stepping it does no name scan and adds no per-frame allocation.
3. **Given** the actor is removed (or her slot recycled) mid-flight, **Then** the op ends.
4. **Given** the one-shot actor ops, **Then** they still resolve by name (once).

---

### Edge Cases

- **A speaker with no matching asset**: the line is left undrawn and the authoring panel explains it — the
  remedy is now "add a `[Portraits …]` block to her actor asset (and give it her name)", not "assign a binding".
- **Two actor assets with the same declared name**: the one with the lowest `Id` wins, silently and
  deterministically — a name is a label (the `specs/072` rule, applied to assets).
- **An asset that omits its name in the manifest**: its name falls back to its id, so it resolves a speaker only
  if that id is the speaker string.
- **An actor asset whose name matches no story character**: legal — the game has no roster to check it against;
  any asset may be a speaker if it declares portraits.
- **A speaker named before her asset exists**: legal and unaffected — the combo offers what is discovered, the
  field accepts a typed string, and the line is simply undrawn until the asset appears (as today's unbound
  state).
- **The removed "Spawn" affordance**: placing a bound look's actor by hand is no longer offered; the Asset
  Browser places any actor asset directly.
- **A partial portrait set**: still a load-time failure (`specs/038` FR-006), unchanged.
- **The actor is removed mid-flight**: the next tick finds the slot dead, or its generation changed, and ends the
  op — the same outcome as a name miss; the generation check makes a recycled slot fail rather than alias.
- **The actor's place is removed (and perhaps re-added) mid-flight**: the op ends when the place is gone; a
  re-created place is a new zone, and the slot's name is re-checked, so a coincidental index+generation collision
  cannot redirect the op to a different actor.
- **A second actor of the same name appears mid-flight**: the op does **not** switch to her — it stays on the actor
  it began with (a change from `specs/073`, where the per-tick scan could follow the first name-match).
- **The actor is removed before the op begins**: `begin*` still resolves by name and, on a miss, the op is a no-op
  (`Fin`), exactly as `specs/073`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A portrait line's speaker (`Dialogue.Kind.PortraitDialogue`) MUST be a plain **`string`** — the
  actor's `Name` — and MUST NOT be a `Roster.Character`. This supersedes `specs/038`'s character-typed speaker.
- **FR-002**: A speaker string MUST resolve to an actor asset **by name** in the asset manager — the asset whose
  declared **`Name`** equals the string, matched exactly, the lowest-`Id` asset winning a tie — with **no** cast
  binding, look or slot.
- **FR-003**: The portrait drawn MUST be the resolved asset's declared portrait for the line's expression,
  exactly as `specs/038` maps an expression to a portrait field; nothing else about the dialogue's presentation
  changes.
- **FR-004**: A speaker that resolves to no asset, or whose asset declares no portraits, MUST leave the line
  undrawn and MUST be surfaced in the authoring panel (a value, not an exception), with advisory wording that no
  longer points at a Cast panel.
- **FR-005**: The `Casting` module MUST be removed: the `Look` vocabulary (`Look`, `lookName`, `allLooks`), the
  `Slot` type, the `Cast` type and the `Bindings` map no longer exist.
- **FR-006**: `cast/Roster.fs` MUST be removed from the game; the closed `Character` union (`Character`,
  `characterName`, `characters`) MUST be relocated into the **narrative subsystem** so the engine still compiles,
  and MUST NOT be referenced by any game file.
- **FR-007**: `SharedState.Cast` MUST be removed, and no `Casting.Cast` value may be threaded into the dialogue
  view or the gameplay screen.
- **FR-008**: The Cast panel (`editor/CastPanel.fs`) and its registration MUST be removed, and the remaining
  editor panels MUST still lay out.
- **FR-009**: Both speaker choosers (the cue `Exposit` editor and the Dialogue panel) MUST offer the discovered
  actor asset **names**, with a typed fallback, and MUST NOT offer a roster.
- **FR-010**: Placing an actor in the editor MUST remain possible through the Asset Browser (and the cue
  `AddActor` op), so no placement capability is lost.
- **FR-011**: The speaker-to-asset lookup MUST add no per-frame allocation: the asset MUST be found through an
  index built once when the actor assets are scanned, not by scanning the collection on each resolve
  (`specs/038` FR-010).
- **FR-012**: An actor asset's declared name MUST be the speaker string, and this contract MUST be stated where an
  asset author can find it, so a portrait line resolves by naming the asset after its speaker and declaring its
  portraits.
- **FR-013**: Every other behaviour MUST be unchanged: the zone, trigger, camera and dialogue cue ops, the
  dialogue view's geometry, rendering and physics produce identical output; the actor cue ops behave identically
  except for the resolution reuse FR-014–FR-017 specify. The build is warning-free and the suite is green, with no
  device opened.
- **FR-014**: The in-flight actor state cases — `MoveActorState`, `RotateActorState`, `BlendState` — MUST carry the
  resolved actor's slot **`Handle`** and **`Place`**, captured at `begin*`, in addition to the actor **name** (a
  label for the row and diagnostics).
- **FR-015**: Stepping an in-flight actor op MUST resolve its actor in **O(1)** from the stored handle — a place
  lookup plus an `IsAlive`/`Get` on the actor slot map — and MUST NOT scan by name per tick.
- **FR-016**: Stepping an in-flight actor op MUST add **no per-frame allocation** (it returns through slot-map
  access, not an `option`/tuple).
- **FR-017**: An in-flight actor op MUST end when its stored place is absent, its slot is dead or recycled, or the
  slot's occupant is no longer the stored name; and `MoveActorState`/`RotateActorState` MUST end when the place is
  no longer the active zone (unchanged). It MUST NOT switch to a different actor of the same name.
- **FR-018**: The one-shot actor ops (`RemoveActor`, `EnablePlayer`, `DisablePlayer`, and the `begin*` resolutions
  of the acting ops) MUST keep resolving by name (once, not per frame).
- **FR-019**: The cue vocabulary (`world/Cue.fs`) MUST remain free of zone, entity, asset and device types: it may
  name the slot `Handle` (`core/SlotMap.fs`, compiled below it) and `Place`, but not `Zone` or `EntityRef`.

### Key Entities *(include if data involved)*

- **Speaker**: a plain string — the actor's `Name` — a portrait line's address to its speaker and the text of its
  name plate.
- **Actor asset**: the discovered model a place is populated with, carrying its declared **name** and, when it
  declares them, its **portraits**. Its name is how a speaker string resolves to it.
- **Name index**: the map from an asset's declared name to the asset, built once at scan, so a resolve is a
  directory hit on the draw path.
- **Portrait**: the texture an asset declares for one expression, loaded once by the asset manager.
- **Character** (*relocated*): the closed union of story members — the narrative engine's vocabulary; it keeps
  the engine compiling and leaves the game.
- **Cast binding / look / slot / roster-in-the-game** (*removed*): the `(character, look) → asset id` map, the
  look vocabulary it keyed on, and the Cast panel that wrote it — the bolt-on this feature retires.
- **Actor handle** (*new*): the slot `Handle` (`core/SlotMap.fs`) and `Place` an in-flight actor op carries,
  captured once at `begin*`; the step looks the actor up through it instead of scanning by name.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A portrait line for a speaker whose actor asset exists resolves and draws with **no assignment
  step** — 0 slots to bind (verifiable by showing the line against an asset manager holding the asset).
- **SC-002**: The `Casting` module, `SharedState.Cast`, the Cast panel and the game's use of `Roster` no longer
  exist (0 references in the game layer; verifiable by reading).
- **SC-003**: The narrative engine compiles and its suite passes with the relocated `Character` union (100% of
  its tests), proving the roster retirement cost the engine nothing.
- **SC-004**: A portrait line whose speaker has no matching asset is left undrawn and explained — never raises
  (100% of such lines).
- **SC-005**: The dialogue suite passes, proving the panel/portrait/text geometry for a speaker whose asset is
  present is unchanged from the binding-based behaviour (100%).
- **SC-006**: Resolving a shown portrait line adds no per-frame allocation — 0 allocations on the draw path.
- **SC-007**: The project builds with no warnings and the suite is green, with no device opened.
- **SC-008**: An in-flight actor op (`MoveActorState`/`RotateActorState`/`BlendState`) resolves its actor in O(1)
  per tick and adds **0 allocations per tick** (verifiable by an allocation guard on the step).
- **SC-009**: A same-named actor added mid-flight does not steal an in-flight op, and removing the actor (or her
  place) ends it (100% of such cases).

## Assumptions

- **The roster is the engine's.** The closed `Character` union relocates into the narrative subsystem, its only
  remaining owner; the game adopts the string speaker the prior two features established for actors. No engine
  behaviour changes.
- **One actor asset per speaker.** Retiring looks means a speaker's portrait-bearing asset is the single asset
  named for her; a speaker need not have one (she then cannot speak a portrait line, as `specs/038`).
- **Resolution is by the asset's declared `Name`, matched exactly, the lowest `Id` winning a tie.** The manifest's
  `Name` is the canonical speaker name (it already equals the roster case name); `Id` is a free lowercase slug.
- **The speaker chooser reads the asset manager.** It offers discovered actor names, so a speaker resolves
  regardless of placement, as the roster combo did; a name may still be typed before its asset exists.
- **The Asset Browser already places actors.** The Cast panel's "Spawn" was redundant; removing the panel loses no
  capability.
- **Superseded.** `specs/032-cast-binding` (the binding) and `specs/033-cast-spawn` (the panel's Spawn) are
  superseded; `specs/063-cast-index` was already withdrawn by `specs/072`. `specs/038` (portrait display) and
  `specs/057` (the `Exposit` op) are retained, with their speaker re-based on a string.
- **Advisory wording changes, behaviour does not.** The two miss states (no such asset, no portraits) remain; only
  the message text is reworded to name the asset rather than the Cast panel.
- **An in-flight op pins the actor it began with.** Carrying the handle changes `specs/073`'s behaviour, where the
  per-tick name scan followed the *first* match: a same-named newcomer could take over a running move. Pinning is
  the correct reading of "this op acts on that actor", and the name re-check keeps a re-created place from
  aliasing. The one-shot ops keep the scan, so an author can still name an actor by a string they type.
- **`Cue.fs` may name a `Handle`.** `core/SlotMap.fs` (and its `Handle`) is compiled below `world/Cue.fs`, so the
  cue vocabulary can carry one without naming a zone, entity, asset or device; the module's dependency rule holds.
  The slot `Handle` is the actor's identity within her zone's slot map — index plus generation, with the generation
  making a recycled slot fail rather than alias.
