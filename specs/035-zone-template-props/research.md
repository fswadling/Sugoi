# Research: A Registered Zone's Placed Props

Decisions derived from the feature spec and the code as it stands (`world/ZoneTemplate.fs`,
`world/Zone.fs`, `state/Command.fs`'s command list and undo stack, `editor/Spawning.fs`,
`editor/ZoneRegistryPanel.fs`, `editor/AssetBrowser.fs`, `world/Entity.fs`'s `PropState`, and the
fixtures in `test/ZoneTests.fs` and `test/SpawningTests.fs`).

## Decision 1 — One gesture becomes one step through a compound command

- **Decision**: `state/Command.fs` gains `| Batch of Commands : Command list`. `apply` walks the list
  forwards, `undo` walks it backwards, and the commands inside are ones that already exist.
  Instantiating builds one: a `PropDelete` for each prop being replaced, then the `SetEnvironment` for
  the swap, then a `PropCreate` for each prop the entry records.
- **Rationale**: FR-005 is the requirement that shapes the feature. Instantiating is a single gesture —
  the author puts a registered place into the zone — and left as separate commands it would be
  `1 + N + M` undo steps, where the gesture the author most wants to reverse (it replaces everything
  in the room) would be the hardest to reverse, and the count would depend on how much furniture the
  room has. A compound command is the smallest thing that says "this was one action": four lines of
  match arms, no new payload type, and it composes out of commands the undo model already understands.
  `LinkSpawn` is the precedent — a create and a link recorded as one step — and this generalises it
  rather than inventing beside it.
- **Alternatives considered**: a purpose-built command carrying the zone's contents before and after
  (`Instantiate of Previous : … * PreviousProps : … * Next : … * NextProps : …`), which is the same
  behaviour with a bespoke payload type and no reuse — it would end up being a `Batch` that cannot be
  used for anything else; a **group** on the undo stack (`Applied : Command list list`), which changes
  the type every consumer of the stack touches, where a `Batch` keeps `Applied : Command list` exactly
  as it is; pushing the commands separately and accepting `1 + N + M` steps, which is the reading
  FR-005 exists to forbid.

## Decision 2 — The props are replaced, and the batch is what makes that reversible

- **Decision**: instantiating removes every prop the zone holds and places the entry's, so the zone's
  props afterwards are exactly the entry's. The removal is recorded (a `PropDelete` per prop, carrying
  the ref it was at and its authored value), which is what lets one reversal put them back.
- **Rationale**: settled in the spec, on the author's own reasoning that the environment asset is
  already replaced and the props belong to the same piece of world as it. The implementation
  consequence is worth stating, because it is what makes the batch's *shape* what it is: without
  replacement the batch would only carry creations, and undo would remove what it added without having
  to know anything about what was there. Replacing means the command has to carry both directions.
- **Alternatives considered**: adding the entry's props to the zone's (non-destructive, a smaller
  batch, and the reason it was rejected: instantiating twice would silently double every prop, and
  "the zone is the registered one" would stop being true the second time).

## Decision 3 — The capture splits across two modules, and the pure half stays pure

- **Decision**: `ZoneTemplate.ofEnvironment` stays in `world/ZoneTemplate.fs`, gains the props as a
  second argument, and remains pure and zone-free. `world/Zone.fs` gains
  `captureProps : Zone -> (EntityRef * PropState) array` and `captureTemplate : Zone -> ZoneTemplate
  option`.
- **Rationale**: forced by the compile order and then made useful. `ZoneTemplate.fs` is compiled above
  `Zone.fs` — the loader takes a record as its argument — so a function that reads a live zone cannot
  live there. Putting the zone-reading half in `Zone.fs` inverts that correctly: `captureTemplate`
  pairs the loaded environment with the zone's props and answers `None` when there is no environment,
  which is FR-007 expressed in the type, and it joins the `capture*` family the module already has.
  Keeping the constructor pure and giving it the props as an argument is what lets the registry's own
  tests build a record without a physics world, an asset manager or a zone: those tests are about the
  dictionary, and they should not need a scene to exist.
- **Alternatives considered**: moving capture wholly into `Zone.fs` and leaving `ZoneTemplate.fs` as
  the bare type, which reads well but forces the map's tests to build a zone and a loaded environment
  to get a value to put in a dictionary (and would push a third copy of the headless fixture into the
  registry's test file — the thing 034's T012 already refused once); having the panel compose the two
  halves itself (`ofEnvironment env (…props…)`), which leaves the record's construction spread across a
  caller and gets a field forgotten the moment one is added.

## Decision 4 — `captureProps` returns refs, because the replacement needs them

- **Decision**: the zone's live props are read as `(EntityRef * PropState) array`, not as bare states.
- **Rationale**: the states are what a *record* holds, and the refs are what a *replacement* has to
  record — a `PropDelete` names the slot the prop was at, and `SlotMap.Occupy` is what puts it back
  there with its generation intact, which is how an undone instantiation restores the previous props
  at their old identities rather than as copies (the spec's edge case). One function serves both
  callers: the record takes the states, the replacement takes both.
- **Alternatives considered**: `captureProps : Zone -> PropState array` plus a second function for the
  refs (two walks of the same map and two functions where one does); reading the slot map directly at
  the call site (leaks how the zone stores props into an editor module, and `Zone` is where every other
  capture reads it from).

## Decision 5 — The replacement lives in `Spawning`, and the panel's swap moves with it

- **Decision**: `editor/Spawning.fs` gains `instantiateZone`, which captures the props being replaced,
  removes them through the same `PropDelete` commands it will record, loads the environment from the
  entry's environment half, places the entry's props, and pushes the one `Batch`. The panel's
  instantiate branch becomes a call to it followed by the stale-part-selection reconcile.
- **Rationale**: `Spawning`'s own header claims "the single place the editor creates entities", and
  instantiating creates props — so the panel cannot do it, because a panel calling `Zone.spawn*`
  directly is exactly what that rule forbids. The removal happening through the commands that will be
  replayed is the same discipline from the other side: the mutation and its reversal are written once.
  034 left the swap in the panel because the swap had to reconcile a stale part selection and
  `Spawning` is compiled above `Selection`; with the reconcile as the caller's step — which is already
  the idiom `Editor.undoStep` and `redoStep` use — that reason is gone, and the swap can live where it
  belongs.
- **Alternatives considered**: keeping the orchestration in the panel (a panel that spawns props,
  removes props and pushes three kinds of command is not a panel any more); leaving the swap in the
  panel and calling only the props half from `Spawning` (two functions that must be called in the
  right order to be one undo step, which is the pairing this module exists to keep).

## Decision 6 — 034's recorded extraction trigger is answered by moving, not extracting

- **Decision**: `specs/034-zone-registry`'s research Decision 7 is corrected to record what happened,
  and its contract's panel snippet is marked superseded.
- **Rationale**: that decision left the Asset Browser's five-line environment swap duplicated in the
  panel because `Spawning` could not call the reconcile, and said the third call site should extract
  it. What happened instead is that the *second* site moved into `Spawning` and stopped needing the
  reconcile at all — so there are still two sites, and they still differ: one builds an environment
  from an asset id, the other installs one an entry recorded, along with props. What they share is the
  tail — read `previous`, load, record `SetEnvironment` — which is three lines and one command
  construction. Recorded as an *unresolved* tidy-up rather than a debt paid, because extracting a
  three-line tail would be a function with two callers that hides which environment each one builds.
- **Trigger for revisiting**: a third environment-loading path — the scenario work is the likely one —
  at which point the tail is worth a function taking the builder, exactly as 034 said.

## Decision 7 — The compound command is general, and instantiating selects nothing

- **Decision**: `Batch` carries a command list and nothing else; no ordering constraint is imposed
  beyond "replayed backwards". `instantiateZone` leaves the selection alone — unlike every other
  function in `Spawning`, which selects what it created.
- **Rationale**: a compound command that also knew it was an instantiation would be the purpose-built
  command Decision 1 rejects. On selection: every other `Spawning` function creates exactly one entity
  and there is nothing to choose; an instantiation may create none, one, or twenty props, so selecting
  one would be arbitrary. Leaving the selection is also *better* here than clearing it: a selection
  naming a prop the instantiation removed goes stale, and a stale prop ref resolves again when undo
  restores it at its recorded slot — which is the rule `Selection.dropStalePartSelection` already
  states for actors and props, and the reason it only reconciles *part* selections.
- **Alternatives considered**: selecting the first placed prop (arbitrary, and it would fight the
  author's next action); clearing the selection (loses a selection that undo would have made valid
  again, and contradicts the rule the reconcile already encodes).

## Decision 8 — A part-shaped entry in a props array is a broken registration

- **Decision**: instantiating requires each recorded prop to name an asset; an entry shaped like a part
  (an absent asset id) raises rather than being drawn as something it is not.
- **Rationale**: a prop and a part are both `PropState`, and the only thing telling them apart is the
  asset id — which is how `Zone` already distinguishes them when it builds a renderer (`Some id` is a
  whole prop asset, `None` is a piece of the loaded environment). A props array holding a `None` would
  be read as "draw this piece of the environment", silently producing a prop that is not one, and
  FR-003 says the two must stay distinguishable. Nothing in the editor can produce such an entry — only
  whole props are ever added to a zone's props — so this is a boundary check on registration data
  rather than a guard against a reachable state.
- **Alternatives considered**: letting the renderer's existing behaviour stand (the `None` path in
  `propRenderer` would build an environment-part renderer, which is the silent half-right answer above);
  checking in `captureProps` instead (the zone's props cannot hold a part, so it would guard the
  side that cannot go wrong).

## Decision 9 — `loadEnvironmentFrom` keeps loading the environment half only

- **Decision**: `Zone.loadEnvironmentFrom` is unchanged. It takes a record and reads its `AssetId`,
  `Base`, `Orientation` and `Parts`, ignoring `Props`; the props are placed by `instantiateZone` as
  part of the same step.
- **Rationale**: the function's job is to build and activate an environment, and an environment
  genuinely does not contain props — that is what makes this a feature rather than a rename. Splitting
  it into four arguments so it takes no record it does not fully use would cost more than the smell it
  removes, and a reader who notices the ignored field finds the props placed one function away with a
  comment saying why.
- **Alternatives considered**: making `loadEnvironmentFrom` create the props too (it would then need a
  physics world, an evaluation of what to do with the zone's existing props, and a command to record
  all of it — i.e. it would become `instantiateZone`, in the world layer, where the editor's undo
  policy does not belong).

## Decision 10 — The two halves are asserted in the two files whose fixtures already drive them

- **Decision**: `test/ZoneTests.fs` covers capture (what a record takes from a zone's props, and that
  it is a copy); `test/SpawningTests.fs` covers the replacement (exactly the entry's props afterwards,
  one undo step, what that restores). No new test file; no test for `Batch` in isolation.
- **Rationale**: the fixtures exist and are the honest homes: `ZoneTests` already drives
  `loadEnvironment`/`setEnvironment` headlessly with a hand-built asset, and `SpawningTests` is the only
  file with a `SharedState`/`GameplayState` fixture, which the editor path needs. `Batch` is exercised
  through the instantiation that needs it, the way `LinkSpawn` is exercised through the extend gesture
  rather than on its own — a command's redo and undo are only meaningful over the mutations it names.
- **Verification the replacement test has to make, because it is the subtle one**: after an
  instantiation and one undo, the zone holds its previous environment *and* its previous props, at the
  refs those props had before — which works because undo frees the slots the new props were at before
  re-occupying the old ones, and `SlotMap.Occupy` restores a recorded generation. A test that compared
  prop *values* without comparing refs would not notice if the second half of that were broken.
- **Alternatives considered**: a `test/CommandTests.fs` for the compound command (a file whose first
  test is a command that exists for one gesture — and the gesture is where its ordering matters).

## Decision 11 — The panel does not capture per frame

- **Decision**: the Register control's enabled state is decided by whether the active zone has an
  environment loaded — the cheap question — and `captureTemplate` is called only when the control is
  activated.
- **Rationale**: capturing walks the zone's props and allocates an array for them. Doing that every
  frame to decide whether a button is greyed out would be exactly the per-frame allocation the
  constitution's fifth principle is about, in edit mode where nobody would measure it. The two
  questions cannot disagree: both read the same field of the same zone in the same frame.
- **Alternatives considered**: binding the capture once per frame and using it for both (simpler to
  read, and it allocates on every frame the panel is open).

## No unresolved clarifications

Every choice is settled by the spec, by the code, or by the inspection recorded above — including the
one that could only be settled by reading the compile order (Decision 3) and the one that could only be
settled by reading `Selection`'s own rule about stale refs (Decision 7). No `NEEDS CLARIFICATION`
remains for Phase 1.
