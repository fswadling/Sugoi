# Feature Specification: Zone Cue Ops

**Feature Branch**: `062-zone-cue-ops`

**Created**: 2026-10-04

**Status**: Draft

**Input**: User description: "We have a set zone cue, but we also need other cues around zones — we need to be able to add, remove and delete zones from the slot map, and also trigger a GPU lazy load to start on a zone that is already in the slot map. (The lazy load may need to also unload a zone to make space — it shouldn't unload the active one.)"

## Origin

`specs/061-zones-as-places` gave a runtime zone its authored identity — a `Place` from a closed union — and
with it the cue op `SetZone of place: Place`. It deliberately stopped there: the spec's Assumptions record
that *"Bringing an absent place into the world from its registered template (streaming), and making a cue's
zone op a **no-op** when the place is not present, are the **next** feature"*. So today a cue can enter a
place that is already present, and nothing else about zones is reachable from a cue.

Two other threads point at the same feature. `docs/ENVIRONMENT_LRU_PLAN.md` (the design note behind
`specs/050`) lists, under "Out of scope (follow-ups, matching odin)", *"Async/threaded load and a
`PreloadZone` cue op"* — the async half landed as `specs/053`, leaving the op. And the runtime now has the
pieces the op needs: an environment asset is loaded **on demand and asynchronously** (`Assets.requestEnvironment`,
never on the calling thread) under a byte budget enforced by an LRU that evicts the least-recently-used
environment, protecting a small **pin set** the gameplay layer narrows to the active zone's environment every
frame (`screen/GameplayScreen.fs`).

This feature adds the zone ops a cue needs to move the story through the world: make a declared place
**present** (streaming in its registered template), make a present place **absent**, and **preload** a present
zone's environment so a later switch is instant. The preload shares one budget with everything else, so making
room for it must unload *other* environments — never the active zone's.

## Clarifications

### Session 2026-10-04

- Q: Does an add op also make the place active, or only present? → A: **Present only** — an added place never
  changes the active one; entering it is `SetZone`'s job, so add/preload/set compose into one transition.
- Q: How strongly is a preloaded environment protected from eviction? → A: **Best-effort** — a preload is an
  ordinary on-demand request (de-duplicated, non-blocking), not a lasting reservation; it makes the environment
  resident ahead of the switch and otherwise follows normal least-recently-used recency. The active zone's
  environment remains protected and is never unloaded for it.
- Q: Where does the cue no-op rule stop — does removing the active place raise? → A: **It is a no-op too** —
  zone ops never raise on redundant or contradictory state, so a cue is always safe to re-run. (The editor's own
  refusal of that action, `specs/061` FR-011, is unchanged.)

- Q: What does a preload do for a present place that is **registered but has no environment** (the state the
  editor's add leaves behind)? → A: It resolves the place's registered template, streams it into the zone, and
  loads that environment — so a cue can make a bare room ready. A place with neither an environment nor a
  registered template is still a no-op. (Follow-up to the first session: the contract as first written made
  this a no-op, which left a present-but-empty registered place with no cue path to its content.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue adds and removes zones by place (Priority: P1)

A cue makes a declared place **present** — the zone comes into the slot map with its registered template
loaded, if it has one — and elsewhere makes a present place **absent**, releasing everything it owns. Both
name a `Place`, so a cue cannot name a location the game does not declare.

**Why this priority**: it is the presence half — the thing a cue must be able to do before preloading or
entering makes sense — and it is testable headlessly against the gameplay state with no window and no GPU.

**Independent Test**: run a cue carrying an add op and assert the place became present; run one carrying a
remove op and assert it became absent; assert a place that is not declared cannot be named at all.

**Acceptance Scenarios**:

1. **Given** a declared place that is absent, **When** a cue reaches an **add** op naming it, **Then** the
   place is present, and if it has a registered template its content is in the zone.
2. **Given** a present place that is not active, **When** a cue reaches a **remove** op naming it, **Then**
   the place is absent and its zone's resources (bodies, GPU) are released.
3. **Given** an added place whose environment is GPU-resident, **When** it is removed, **Then** its
   environment is unloaded unless another still-present place uses the same environment.
4. **Given** a place the game does not declare, **When** a cue is authored, **Then** the op cannot name it
   (the place chooser offers only declared places).

---

### User Story 2 - A cue preloads the next zone's environment, protecting the active one (Priority: P2)

While the player is in one zone, a cue **preloads** another present zone's environment, starting its
asynchronous GPU load so that switching into it later shows no geometry pop-in. The load never blocks the
frame, and making room for it never unloads the active zone's environment.

**Why this priority**: it is the whole point of the op — a seamless transition — and it is what the deferred
`PreloadZone` follow-up (`specs/050`) asked for.

**Independent Test**: with two present zones and a real (headless) asset manager, preload the inactive one,
pump the loader, and assert its environment is resident and the active zone's environment is untouched; drive
the budget low and assert the active zone's environment survives eviction.

**Acceptance Scenarios**:

1. **Given** a present, inactive zone with an unloaded environment, **When** a cue reaches a **preload** op
   naming it, **Then** its environment load is requested without blocking, and the op costs no subsequent time.
2. **Given** a present place that is **registered** but has no environment of its own (added empty from the
   editor), **When** a cue reaches a preload op naming it, **Then** its registered template is streamed into
   the zone and that environment's load is requested.
3. **Given** a preloaded environment, **When** its load lands, **Then** it is resident and the active zone's
   environment is still resident.
4. **Given** the byte budget is exceeded by a preload, **When** the load lands and eviction runs, **Then**
   the least-recently-used environments are unloaded to make room, and the active zone's environment is never
   among them.
5. **Given** a preloaded zone, **When** the cue later switches into it, **Then** the switch finds its
   environment already resident (no pop-in).

---

### User Story 3 - The zone ops compose and are safe to re-run (Priority: P3)

The ops compose into a transition — preload a zone, switch to it, later remove the one you left — and a cue
that carries them behaves sanely when re-run: an op whose effect already holds does nothing rather than
failing, so a cue that runs twice does not crash on its second pass.

**Why this priority**: it is the robustness half. `specs/061` explicitly deferred the no-op rule, and a cue
whose target state is already true is the ordinary case, not an error.

**Independent Test**: run a cue that adds a place the state already has present, removes one it does not, and
preloads one with no environment — each completes without raising; then assert the composition
(preload → switch → remove) leaves the expected state.

**Acceptance Scenarios**:

1. **Given** an add op naming a place already present, **When** the cue runs, **Then** the op performs
   nothing and the cue moves on.
2. **Given** a remove op naming an absent place, **When** the cue runs, **Then** the op performs nothing.
3. **Given** a preload op naming a zone with no environment, **When** the cue runs, **Then** the op performs
   nothing.
4. **Given** a cue that preloads a zone, switches to it, and removes the previous zone, **When** it runs,
   **Then** the final active place is the preloaded one and the previous place is absent.

---

### Edge Cases

- **Removing the active place from a cue**: performs nothing — a cue does not remove the zone it is simulating
  (the author switches away first with `SetZone`). The editor's own refusal of that action (`specs/061` FR-011)
  is unchanged; the cue treats it as a no-op rather than an error, so a cue stays safe to re-run.
- **A present place with no environment**: if it is **registered**, a preload streams its template in and
  loads that environment; if it is not registered (or its template names no environment), there is nothing to
  preload and the op is a no-op. A place may be present with no environment or template (`specs/061` SC-004).
- **A place already loading or resident**: a preload of it de-duplicates against the in-flight/active request
  rather than starting a second one.
- **Two present places sharing one environment asset**: *removing* one must not unload the asset the other
  still needs (the shared-asset rule `specs/061` FR-010). Eviction is different and needs no such check: it may
  unload a present-but-inactive zone's environment to make room, because that zone reloads on demand when it is
  next switched to — only the active zone's environment is protected.
- **The budget cannot be met even after evicting everything evictable**: the protected set — the active zone's
  environment — wins over the budget; it stays resident even if that leaves the budget exceeded, rather than
  being an error.
- **A preload whose zone is removed before its load lands**: the request is dropped like any environment that
  is no longer needed, and its residency must not stay "loading" (the existing drop path, `specs/053` VR-011).
- **Re-running the same cue**: because redundant ops are no-ops, a second run does not fault on state the
  first run produced.
- **While editing**: cues do not run, so no zone op fires from the editor; only Play advances the cue.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cue MUST gain zone ops that make a declared place **present**, make a present place
  **absent**, and **preload** a present zone's environment. Each MUST name a `Place` from the closed union
  (no free string), exactly as `SetZone` does.
- **FR-002**: An **add** op MUST make an absent declared place present, and MUST stream in the place's
  registered template when one exists (its environment and placed props); a place with no registered template
  is present and valid but empty. It MUST NOT change the active place — entering the zone is `SetZone`'s job —
  so `AddZone`, `PreloadZone` and `SetZone` compose into a prepared transition.
- **FR-003**: A **remove** op MUST make a present place absent, releasing its bodies and GPU resources and
  unloading its environment unless a still-present place shares that environment (`specs/061` FR-010). Naming
  the active place performs nothing (the no-op rule, FR-007).
- **FR-004**: A **preload** op MUST start the asynchronous, non-blocking load of a present zone's environment,
  so a later switch into that zone finds it resident. The environment is the zone's own when it has one;
  otherwise it is resolved from the place's **registered template**, which is streamed into the zone first —
  the editor's add leaves a present place empty. A present place with neither an environment nor a registered
  template has nothing to preload. The op MUST cost the cue no subsequent time (a no-progress op) and MUST
  de-duplicate against an already-loading or already-resident environment.
- **FR-005**: A preload MUST NOT cause the active zone's environment to be unloaded. When making room for a
  load requires eviction, only environments that are not the active zone's MAY be unloaded, least-recently-used
  first.
- **FR-006**: A preload MUST be an ordinary on-demand request — de-duplicated against an environment already
  loading or resident, and not given a lasting reservation beyond any other request. A completed preload MAY be
  evicted later under budget pressure; its value is making the environment resident ahead of the switch, and it
  carries only the normal least-recently-used recency.
- **FR-007**: A zone op whose effect already holds, or that names a place it cannot act on, MUST perform nothing
  and let the cue continue — including **removing the active place**, which is a no-op rather than a script
  error. Re-running a cue MUST NOT fault on state a previous run produced.
- **FR-008**: In particular, a cue's zone op on a place that is not present MUST perform nothing rather than
  throw — the rule `specs/061` deferred to this feature.
- **FR-009**: The editor's cue panel MUST offer the new ops with a place chooser (the closed set, as `SetZone`
  does), and each accepted edit MUST be one step on the one undo history, like every other cue edit.
- **FR-010**: A cue-driven zone change MUST be a runtime effect, not a step on the undo history — the cue is
  the screen's own value and `SetZone` already works this way. When play ends, the zone set reflects what the
  cue did.

### Key Entities *(include if data involved)*

- **Place**: the authored identity of a zone, from the closed union (`world/Place.fs`). A compile-time
  constant; never created or deleted at run time.
- **Present / absent place**: whether a place has a runtime zone in the gameplay state's map. Presence is what
  the add and remove ops act on.
- **Environment residency**: whether a zone's environment is loaded on the GPU (the existing term, `specs/050`
  / `053`) — distinct from presence. Preload acts on this.
- **Pin set**: the environments the gameplay layer protects from eviction, refreshed each frame to the active
  zone's environment. A preload does not add to it (best-effort; Clarifications).
- **Registered template**: the optional saved config for a place (`world/ZoneRegistry.fs`); what an add op
  streams in when the place has one, and what a preload falls back to when the zone has no environment of its
  own.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue can make a declared place present, stream in its registered template, make a present place
  absent, and preload a present zone's environment — all by naming a `Place`, with no free string anywhere.
- **SC-002**: A zone preloaded by a cue becomes active with no geometry pop-in — its environment is already
  resident when the switch happens.
- **SC-003**: Making room for a preload never unloads the active zone's environment, however low the budget.
- **SC-004**: A cue carrying zone ops can be run more than once without raising on state its earlier run
  produced.
- **SC-005**: The editor offers the ops with a place chooser, and each accepted edit is one step on the one
  undo history.
- **SC-006**: The project builds with no warnings and the suite is green, with no device opened.

## Assumptions

- **Remove and delete are one operation.** "Remove" and "delete" name the same presence change here — a
  present place becomes absent — because the model has one notion of absence (`specs/061`). The op mirrors the
  cue vocabulary's `AddAdvent` / `RemoveAdvent` pair.
- **This is `specs/061`'s deferred follow-up.** The streaming of an absent place from its registered template,
  and the cue no-op rule for a place that is not present, were both explicitly left to "the next feature"; this
  is that feature. The `PreloadZone` op is the one `docs/ENVIRONMENT_LRU_PLAN.md` (and odin) names.
- **Cue zone changes are runtime, not authored.** They are not steps on the undo history and are not persisted;
  this matches `SetZone`, which switches at run time without a history step.
- **The environment load machinery is unchanged.** Preloading reuses `Assets.requestEnvironment` and the
  existing pin/eviction policy as they are; it adds no second loader and no new reservation.
- **The place vocabulary is unchanged.** No new places are declared here; the ops act on the existing union.
- **No actor addressing.** Naming actors in cues is `specs/061`'s other successor, a separate feature.
- **Add prepares; `SetZone` enters.** An add op never changes the active place; it is what makes
  `AddZone → PreloadZone → SetZone` compose into one prepared transition.
- **A preload is best-effort.** It is an ordinary on-demand request — de-duplicated and non-blocking — that makes
  an environment resident ahead of the switch; it is not reserved against later eviction. The active zone's
  environment stays protected regardless.
- **Zone ops never raise on redundant or contradictory state.** Even removing the active place performs
  nothing, so a cue is always safe to re-run; the editor's own guards are unchanged.
