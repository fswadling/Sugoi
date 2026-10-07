# Zone model — live zone, undo snapshot, place recipe, scenario zone

**Status**: **a decision note, not a ratified design.** It records the state of the zone-shaped values as of
`specs/070-scenario-library` and recommends an order of work. Read it before phase 12 grows `ZoneTemplate`, so
that work starts from a decision rather than re-deriving one. **One recommendation has since been taken**: 070's
Phase 6 gave a scenario its own ref-free type (`world/ScenarioZone.fs`) rather than reusing `ZoneContents` — see
"What this decides" item 4, and the fourth row of the table below.

**Evidence**: the types themselves — `world/Zone.fs` (`Zone`, `ZoneContents` and the `capture*`/`occupy*`
readers), `world/ZoneTemplate.fs`, `world/ScenarioZone.fs`, and `world/Entity.fs` (`Environment`, `PartsSource`).
The consumers are
`world/ZoneRegistry.fs` (keyed by `Place`, `specs/034`), the delete step (`specs/048`), and
`world/ScenarioRegistry.fs` (keyed by `Scenario`, `specs/070`).

## What this decides

`Zone`, `ZoneContents` and `ZoneTemplate` overlap, and `specs/070-scenario-library` made the overlap
load-bearing (the registry stores a `ZoneTemplate`; a scenario first stored a `ZoneContents`, then its own type).
This note argues that:

1. **They are roles, not spellings of one thing** — a *live container* (`Zone`), an *undo-fidelity snapshot*
   (`ZoneContents`), a *ref-free recipe* (`ZoneTemplate`), and a *scenario's zone* (`ScenarioZone`, a recipe with
   the breadth a scenario needs). They genuinely answer
   different questions, and `Zone` must stay its own type.
2. **The one real duplication is the environment's authored encoding** — a live `Environment` and
   `ZoneTemplate`'s `{ AssetId; Base; Orientation; Parts }` are two representations of the same thing, bridged by
   hand in both directions.
3. **The risk is convergence, not the current state.** `docs/ROADMAP.md` phase 12 calls waypoints/triggers the
   *deferred siblings* of an entry's parts — i.e. `ZoneTemplate` is meant to grow toward `ZoneContents`. Decide
   the direction before it grows, or you will have two broad snapshot types differing only in whether refs are kept.
4. **Recommended order**: document the axis now (this note); before phase 12 grows `ZoneTemplate`, make the
   environment's authored encoding **one** value; and let scenarios follow onto whichever ref-free shape that
   produces. **(Largely taken for scenarios**: `specs/070` Phase 6 introduced `world/ScenarioZone.fs` — a
   scenario's own ref-free authored zone — so a scenario no longer reuses `ZoneContents`.)

## The constraints that drive it

- **A zone is mutated every frame and owns things no snapshot should.** `Zone` owns the static-scene `Batch`
  (`specs/020`), the `BodyToEntity` bookkeeping, the environment *epoch* counter, and the in-flight
  `Awaiting` state (`specs/053`) — a place that is set but whose asset has not landed. None of that belongs in
  a value you file away. So `Zone` is not a candidate for merging.
- **A delete must be exactly reversible, down to the slot.** `ZoneContents` keeps each entity's `EntityRef` and
  the live `Environment` (epoch and parts map included), because a ref held before a delete must still resolve
  after the undo (`specs/048` FR-009) and a part ref is epoch-scoped. This is *identity*, not authoring.
- **A registered place is a recipe, not a snapshot.** Instantiate mints fresh slots, so refs would be dead
  weight; and the author deliberately registered only the environment, its parts and its placed props. So
  `ZoneTemplate` keeps no refs and flattens the environment.
- **The loader takes a template, and it is compiled before `Zone`.** `ZoneTemplate` sits above `world/Zone.fs`
  in the compile list because `Zone.loadTemplate`/`loadEnvironmentFrom` take one as their argument. Any reshaping
  has to respect that (a ref-free recipe can live early; anything holding refs or a live `Environment` cannot).

## The four values as they stand

```fsharp
// live container — mutated in place each frame
type Zone =
    { Actors : SlotMap<Actor>              // Actor   = authored state + physics + renderer
      mutable EnvironmentState : ZoneEnvironment   // NoEnvironment | Active Env | Awaiting Env
      mutable Light : Texture.LightSettings
      Props : SlotMap<Prop>                // Prop    = authored state + physics + renderer
      Waypoints : SlotMap<Waypoint>
      Triggers : SlotMap<Trigger>
      Batch : EnvironmentBatch.EnvironmentBatch
      mutable NextEnvironmentEpoch : uint32
      BodyToEntity : Dictionary<Physics.BodyId, EntityRef>
      mutable Player : Handle voption }

// undo-fidelity snapshot — refs kept, live Environment kept
type ZoneContents =
    { Actors : (EntityRef * ActorState) list
      Props : (EntityRef * PropState) list
      Waypoints : (EntityRef * Waypoint) list
      Triggers : (EntityRef * TriggerState) list
      Environment : Environment option      // the LIVE value: Epoch, Parts : SlotMap<Prop>, PartsSource
      Light : Texture.LightSettings
      Player : Handle voption }

// place recipe — refs dropped, environment flattened
//   (a scenario zone with an environment and no entity lists; see below)
type ZoneTemplate =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      Light : Texture.LightSettings
      Parts : PropState array                // the environment's parts, as states
      Props : PropState array }              // the placed props (AssetId = Some), as states

// scenario zone — refs dropped, arrays, links and player by index (spec 070)
type ScenarioZone =
    { Environment : ScenarioEnvironment option   // { AssetId; Base; Orientation; Parts : PropState[] }
      Light : Texture.LightSettings
      Actors : ActorState array
      Waypoints : ScenarioWaypoint array          // { Base : Entity; Next : int option }
      Triggers : TriggerState array
      Player : int option }
```

| | `Zone` | `ZoneContents` | `ZoneTemplate` | `ScenarioZone` |
|---|---|---|---|---|
| **Role** | live container | undo/redo snapshot | place recipe | scenario zone (a recipe, with breadth) |
| **Refs** | live handles | **kept** (exact restore) | **dropped** | **dropped** |
| **Environment** | live `Environment` (epoch, parts map, `PartsSource`) | the live `Environment` value | `AssetId` + `Base` + `Orientation` + `Parts[]` | `ScenarioEnvironment` (the same authored shape) |
| **Breadth** | everything + physics/render | actors/props/waypoints/triggers/env/light/player | env/parts/props/light only | env/parts/light + actors/waypoints/triggers (**no props**) |
| **Compiled** | in `world/Zone.fs` | in `world/Zone.fs` | before `world/Zone.fs` | after `world/Zone.fs` (reads a live `Zone`) |
| **Consumer** | the play loop | `ZoneDelete` undo, `occupyZone` | `ZoneRegistry` instantiate | the scenario registry's capture |

## Where they overlap, and where they do not

The overlap is exactly **environment + parts + props + light**. Everything else is one type being *narrower* than
another, which is harmless:

- `ZoneTemplate` ≈ `ZoneContents` projected onto (environment, parts, props, light), with refs dropped and the
  environment flattened. A place registry entry could be derived from a `ZoneContents`; today it is derived
  from a live `Zone` (`Zone.captureTemplate`).
- `ScenarioZone` matches `ZoneTemplate` on the environment, parts and light, and adds actors, waypoints and
  triggers (and the player) — but it **deliberately omits placed props** (environment decoration, not narrative).
  So a place-registry entry is, in shape, a `ScenarioZone` with an environment and no entity lists **plus the
  placed props a scenario does not carry**. The two are close but not identical, which makes the convergence
  (option 3) concrete — and adds a question it must settle: whether placed props belong in the shared recipe at
  all, or only in a place's.
- The `PropState`-array vs `(ref, PropState) list` difference is only the refs.
- The environment difference is the **substantive** one: `ZoneContents` carries the live `Environment`
  (an `Epoch`, a `Parts : SlotMap<Prop>`, and a `PartsSource`), where `ZoneTemplate` carries an authored
  `{ AssetId; Base; Orientation; Parts[] }`. Two encodings of one authored thing, converted by hand —
  `ZoneTemplate.ofEnvironment` (Environment → template) and `Zone.loadEnvironmentFrom` (template → Environment,
  with a fresh epoch and `PartsSource = Authored`).

That hand-written round-trip is the only place a real "two sources of truth" bug can live: the `Base` vs
`Orientation` split, the epoch, and `PartsSource` are exactly the fiddly bits, and they are the reason
`loadEnvironmentFrom` and `ofEnvironment` must stay in step forever.

`ZoneContents` is also a **hybrid**, and its name hides that: it is not purely authored data — it carries the
live `Environment` and entity refs so `occupyZone` can rebuild the *exact* slots. It sits deliberately between
`Zone` and `ZoneTemplate`, which is why it reads as overlapping both.

## Why not merge them

- **`Zone` + anything** — no. A per-frame-mutated container with GPU/render and in-flight state is a different
  kind of thing from a value you file away.
- **Collapse `ZoneTemplate` into `ZoneContents`** (registry stores a `ZoneContents`) — rejected. It would put
  refs and a live `Environment` (epoch, `PartsSource`) into the registry, where they mean nothing (a recipe mints
  fresh slots), and it fights the compile order, since `ZoneTemplate` must precede `world/Zone.fs`.
- **Collapse `ZoneContents` into `ZoneTemplate`** — rejected *as-is*. It would drop the refs the delete needs to
  restore exact slots (`specs/048` FR-009).

## The risk: the roadmap invites convergence

`docs/ROADMAP.md` phase 12 (the zone registry's "done when" footnote) lists waypoints, triggers and splat maps
as **"the deferred siblings of an entry's parts"**. Read literally, that means `ZoneTemplate` is *intended to
grow* until it captures a whole zone — at which point it is `ZoneContents` minus refs, and the two types differ
by one axis. `specs/070-scenario-library` has already pushed in that direction from the other side: a scenario
stores `Map<Place, ZoneContents>`, while the registry stores `ZoneTemplate`. Two authoring surfaces, two
"a zone as authored" values.

There was a sharper tell, now acted on. 070 first stored a scenario's zones as `ZoneContents`, whose refs (and
captured epoch) are dead weight for a fork: they are discarded at the only moment they would be used. That is a
scenario wanting the **ref-free recipe**, not the identity-preserving snapshot — and 070's Phase 6 gave it one
(`world/ScenarioZone.fs`), so a scenario now stores arrays of authored states with waypoint chains and the player
by index. `ZoneTemplate` is still the registry's recipe; the two could converge (`Open questions`), but
they are no longer the same type as the delete's snapshot.

## Options

| Option | What it means | Cost |
|---|---|---|
| **0. Do nothing** | keep the four shapes: a live `Zone`, a delete's `ZoneContents`, a place's `ZoneTemplate`, a scenario's `ScenarioZone` | the overlap gets re-litigated; phase 12 may grow `ZoneTemplate` before anyone notices, and the near-duplicate of `ScenarioZone` stays |
| **1. Document the axis** *(this note)* | say plainly that the split is *refs kept vs dropped* + breadth, and that the environment is encoded twice | none; stops the churn |
| **2. Unify the environment encoding** | one authored environment value (`ScenarioEnvironment` / the `ZoneTemplate` shape) used by every recipe; `ZoneContents` embeds it rather than a live `Environment` | one round-trip deleted; touches the delete/restore path, which is ref-sensitive |
| **3. Make one recipe canonical** | `ScenarioZone` — already a superset of `ZoneTemplate` — becomes *the* authored zone recipe; the registry stores it and `ZoneContents` is expressed as **recipe + refs** | resolves both smells at once; a compile-order move (the ref-carrying wrapper goes after `world/Zone.fs`); the biggest change |
| **4. Registry stores `ZoneContents`** | collapse the recipe into the snapshot | rejected above: refs/epoch in config; fights compile order |

## Recommendation

1. **Take option 1 now** — this note. Do not touch code.
2. **Before phase 12 grows `ZoneTemplate` (its deferred siblings), take option 2**: one authored environment
   encoding, with one bridge. That is the seam that can actually rot, and it is independent of the zone-level
   question.
3. **When the registry needs waypoints/triggers, take option 3** — promote the ref-free recipe to the single
   "authored zone" shape and express the snapshot as *recipe + refs*. **Scenarios have already done this for
   themselves** (070's Phase 6 gave them `ScenarioZone`); the step that remains is to converge `ZoneTemplate` onto
   that same shape so the registry and scenarios file one recipe type.

Option 3 is the one I would bet on if forced to choose a destination; options 1 and 2 are the cheap steps that
keep the door open to it.

## Known wrinkles

- **`ZoneContents` keeps a *live* `Environment`.** Its part refs are epoch-scoped, so a captured part ref is
  only meaningful alongside the captured epoch — which is why the whole `Environment` value is carried rather
  than a flattened form.
- **`captureTemplate` then `loadTemplate` mints new parts.** A registered place's parts are new entities with a
  new epoch, not the captured ones. Correct for a recipe; it means the registry can never be used to restore
  exact refs (only the delete snapshot can).
- **The registry and a scenario now use near-duplicate shapes.** A `ZoneTemplate` is, in shape, a `ScenarioZone`
  with an environment and no entity lists — so "register a place" and "register a scenario" file very similar
  values under different type names. That near-duplication is the concrete form of the convergence question; it is
  recorded rather than resolved.
- **`ZoneTemplate` distinguishes parts from props by `AssetId`** (`None` for a part, `Some` for a placed prop),
  the same field the renderer reads — so the two arrays are not a labelled pair; the label is inside each state.
- **`ZoneTemplate` is compiled before `Zone`** because the loader takes one. Any move toward a ref-carrying
  snapshot must keep the ref-free half above `world/Zone.fs`.

## Open questions

1. **Should the registry's entry grow to hold waypoints/triggers** (phase 12's deferred siblings), and if so,
   does it store a ref-free recipe or a full snapshot?
2. **Should the registry's entry converge with a scenario's ref-free zone** — both are recipes that instantiate
   fresh zones, and 070's Phase 6 gave the scenario its own type (`world/ScenarioZone.fs`). Sharing one recipe type
   is the natural next step (option 3), but it is not taken yet.
3. **Is the name `ZoneContents` right?** It is a snapshot with identity (refs + live epoch), not "the contents";
   a name like `ZoneSnapshot` would signal that and stop it reading as another spelling of a zone.
4. **Is the double environment encoding accepted, or unified** (option 2)? If accepted, the `ofEnvironment` /
   `loadEnvironmentFrom` invariant should be stated where both live.

**Decided so far**: `Zone` stays a distinct live type; `ZoneTemplate` stays ref-free and compiled early;
`ZoneContents` stays the delete's identity-preserving snapshot; a **scenario** does not reuse `ZoneContents` — it
has its own ref-free type (`world/ScenarioZone.fs`, `specs/070` Phase 6). Whether the scenario zone and
`ZoneTemplate` converge into one recipe is open.

## Follow-ups

- **Phase 12 (the registry's deferred siblings)** is where the direction must be chosen; it should cite this note
  rather than re-derive the answer.
- **`specs/070-scenario-library`'s fork increment** consumes `ScenarioZone` (the ref-free type); it is where the
  recipe is materialised into fresh zones, and where the question of converging `ScenarioZone` with `ZoneTemplate`
  (option 3) can be settled with a real second consumer in hand.
- **If option 2 or 3 is taken**, the change touches `world/ZoneTemplate.fs`, `world/Zone.fs`
  (`captureZone`/`occupyZone`, `captureTemplate`/`loadTemplate`/`loadEnvironmentFrom`), `world/ScenarioZone.fs`,
  `world/ZoneRegistry.fs`, and `world/ScenarioRegistry.fs` — and `docs/ARCHITECTURE.md`'s zone section should carry
  the outcome.
