# Research: Spawning a Cast Member from the Cast Panel

Decisions derived from the feature spec and the code as it stands (`editor/CastPanel.fs`,
`editor/Spawning.fs`, `editor/AssetBrowser.fs`, `render/ImGui.fs`, `world/Casting.fs`).

## Decision 1 — The control is on the slot row, and empty means disabled

- **Decision**: each slot row draws a `Spawn` button after its combo. While the slot has no
  assignment the button is drawn **disabled** rather than hidden.
- **Rationale**: the asset is bound per slot, so the slot is the only place the control's meaning is
  unambiguous — the spec settles this, and with one look bound it is one control per member, on the
  row that shows the asset. Drawing it disabled rather than hiding it means the author sees where
  the action will appear and what is missing to enable it, which is the same posture the unassigned
  combo's `(none)` preview takes.
- **Alternatives considered**: a control on the member's header row (would have to invent which look
  it spawns — "the first bound look in declaration order" — for no gain today, and it would put a
  button whose target is invisible on a row that names no asset); hiding the control when unbound
  (loses the affordance's discoverability, and `(none)` next to a missing button reads as an error
  rather than a state).

## Decision 2 — The panel takes the gameplay state, and spawns through `Spawning`

- **Decision**: `CastPanel.draw` gains the `byref<GameplayState>` parameter its siblings have, and
  the control calls `Spawning.spawnActor`.
- **Rationale**: `editor/Spawning.fs` exists because every `Zone.spawn*` must be paired with the
  command that reverses it *immediately*, or a recorded ref can have its slot reused — so no panel
  may call `Zone.spawn*` directly. `Spawning.spawnActor` takes `shared` and `state` and does three
  things this feature wants and must not reimplement: it creates the actor, it pushes the reversing
  command, and it selects what it created. Placing an actor is therefore a scene edit and is
  undoable, while assigning the model stays a direct write — the two behaviours sit on one panel on
  purpose (`specs/032-cast-binding` FR-011, `docs/EDITOR_DESIGN.md` D-022).
- **Consequence for the record**: `specs/032-cast-binding`'s contract already specified this exact
  signature — `val draw : shared -> state:byref<GameplayState> -> unit` — and its *implementation*
  took `shared` alone instead, as a minimality call made while building that panel; the contract was
  never brought back into line with it. So this feature is what makes the code match the contract
  that was written for it. The line in 032 that this feature falsifies is a different one: the
  contract's bullet saying the panel "mutates nothing outside `shared.Cast`, and touches no undo
  stack", which was true of a panel that only assigns. It is corrected there
  (`contracts/cast-binding.md`). 032's data model needs no correction — its "The panel's data, and
  what it does not hold" is about selection state and roster lists, and both remain true.
- **Alternatives considered**: giving the panel a second `byref` of its own to the active zone
  (invents a selection path and a command pairing outside `Spawning`, which is exactly what the
  module's rule forbids); calling `Zone.spawnActor` and pushing the command in the panel (duplicates
  the pairing rule at a second site).

## Decision 3 — `baseEntity` moves to `Spawning`

- **Decision**: the seven-line entity literal becomes `Spawning.baseEntity`, public, and
  `editor/AssetBrowser.fs`'s private copy is deleted.
- **Rationale**: this feature makes it the third call site — `AssetBrowser`'s actor, `AssetBrowser`'s
  prop, and the cast panel's actor — which is what the rule of three in `AGENTS.md` asks for. It is
  also the only way FR-007 ("the two spawn paths cannot drift apart") is *enforced* rather than
  reviewed: one literal cannot disagree with itself. `Spawning` is its home because that module
  already owns the creation of entities.
- **Alternatives considered**: copying the literal into `CastPanel` (compiler-guarded, since a new
  `Entity` field breaks both, but it leaves FR-007 as a thing a reader has to check);
  `Spawning.spawnActorNamed` taking a name and an asset (would hide the entity's authored shape from
  both callers, and `AssetBrowser` needs the entity shape for props too, so the literal is the thing
  they actually share).

## Decision 4 — No new ImGui binding

- **Decision**: the control uses `igSameLine`, `igSmallButton` and `igBeginDisabled`/`igEndDisabled`,
  all already bound in `render/ImGui.fs`.
- **Rationale**: verified by reading the file rather than assumed — every widget the control needs is
  present, so this feature adds no P/Invoke and no native rebuild. Uniqueness comes free: the panel
  already pushes the member's name onto ImGui's ID stack before drawing a member's rows, so several
  `Spawn` buttons are distinct without a new label or a second push.
- **Alternatives considered**: a full-width button on its own line (costs a row per slot — eight
  rows today — for a control used less often than the combo beside it); an icon-only `smallButton`
  (the binding is minimal and has no icon font).

## Decision 5 — No new headless test

- **Decision**: this feature adds no test, and `quickstart.md` carries the validation instead.
- **Rationale**: what it decides is two facts that are already covered — which asset a slot spawns is
  `Casting.tryAsset` (`test/CastingTests.fs`) and what an actor is called is `Roster.characterName`
  (`test/RosterTests.fs`) — and what it does is call `Zone.spawnActor` and record `ActorCreate`,
  whose undo is covered by `test/SpawningTests.fs`. Everything left is ImGui glue, which no headless
  test can reach: the panel only draws once a real ImGui frame exists. A test would therefore have to
  be written against a new seam invented for it, which is exactly the kind of just-in-case structure
  the constitution's minimalism principle refuses.
- **Alternatives considered**: factoring the decision into a pure `Casting.trySpawn : Slot -> Cast ->
  (string * string) option` so it could be asserted (a function with one call site, existing to make
  a test possible, composing two functions that are already asserted); testing the panel's draw
  against a headless ImGui context (the project has no such harness, and building one is its own
  feature).

## Decision 6 — The actor carries no cast identity

- **Decision**: the created `ActorState` is the ordinary one. No field, flag, or reference names a
  cast member; the member survives only as the entity's name.
- **Rationale**: FR-009, and the half of `specs/032-cast-binding`'s deferral this feature leaves
  alone. Recording which character a placed entity *is* — so the simulation can act on Akane rather
  than on an actor that looks like her — needs a decision about what happens when the binding is
  reassigned afterwards, which is not this feature's question.
- **Alternatives considered**: carrying the member on the actor now (would answer that question by
  accident, and a stale character on an actor is worse than none).

## Decision 7 — Placement, selection and mode gating are inherited, not decided

- **Decision**: a spawned actor appears where the Asset Browser's spawn appears, is selected because
  `Spawning` selects what it creates, and the control is available whenever the panel is drawn — the
  same as the Asset Browser's spawn buttons.
- **Rationale**: FR-007 for the first, and for the others the existing behaviour is the answer this
  feature should not quietly change. Where a spawned actor *should* go — at the camera, in front of
  the author, at the origin — is a question about placement in general, not about the cast, and
  settling it here would settle it for one panel only.
- **Alternatives considered**: seeding the actor at the camera (a better default, arguably, but a
  second placement rule in a second panel — the drift FR-007 exists to prevent).

## No unresolved clarifications

Every choice is settled by the spec, by the code, or by the inspection recorded in Decision 4. No
`NEEDS CLARIFICATION` remains for Phase 1.
