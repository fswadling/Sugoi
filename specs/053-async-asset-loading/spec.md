# Feature Specification: Async asset loading

**Feature Branch**: `053-async-asset-loading`

**Created**: 2026-10-03

**Status**: Implemented

**Input**: User description: "Loading an environment blocks the main thread and freezes the app for the whole load — measured on ArtRoom, ~5.1 s, of which ~4.6 s is texture decode (CPU) and ~0.3 s is GPU upload, plus the glTF parse. We want the load to stop freezing the frame: run the CPU work (file read, glTF parse, image decode) on worker thread(s) and hand decoded payloads to the render thread, which creates and uploads GPU resources within a per-frame time budget, so the app keeps rendering and the environment appears (pops in) a moment after its zone is activated. A developer may cycle through zones quickly (e.g. holding undo, or clicking through the outliner), so pending load requests need a bounded, de-duplicated, most-recent-first queue that drops stale requests. Only the upload is GPU-bound, and the GPU serialises submissions, so the limit on concurrent loads is memory, not the GPU."

## Clarifications

### Session 2026-10-03

- Q: When a pending load is superseded, what happens to work already in flight for it? → A: Drop only not-yet-started requests; a load already decoding finishes, but its decoded payload is discarded (not uploaded) if the asset is no longer wanted when it completes.
- Q: Does this feature move prop assets to the async, on-demand path too, or only environments? → A: Environments only — props and actors keep their eager load (spec 050's boundary).
- Q: How should a failed asynchronous load be reported? → A: Log the error and mark the zone's existing residency indicator as a *failed* state — no new UI.
- Q: Should the pending-set size and the concurrent-decode limit be one shared limit or two? → A: Two separate limits — a pending-set capacity (drop-oldest) and a concurrent-decode limit (worker count).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Switching zones never freezes the app (Priority: P1)

A developer activates a zone whose environment is not loaded. The zone becomes active
immediately and the app keeps rendering; the environment appears (pops in) once its
resource creation and upload have finished, a short moment later. The previous freeze —
seconds of a dead frame — is gone.

**Why this priority**: this is the whole feature. Every load path is synchronous today,
and the measured freeze (~5.1 s for one environment) makes the editor unusable with real
content. Everything else here exists to make this safe and predictable.

**Independent Test**: activate a non-resident environment and confirm the frame loop keeps
running (frames stay near the frame budget) while it loads, and the environment renders
correctly when it lands.

**Acceptance Scenarios**:

1. **Given** a non-resident environment, **When** its zone is activated, **Then** the zone
   becomes active in that frame with no stall, and the environment appears when its uploads
   complete.
2. **Given** a load in progress, **When** the frame loop runs, **Then** no frame blocks for
   the load (no multi-frame or multi-second hitch).
3. **Given** an environment that has landed, **When** the developer inspects it, **Then** it
   renders and behaves exactly as a synchronously loaded one.

---

### User Story 2 - Decoding and parsing run off the main thread (Priority: P1)

The expensive CPU work — reading the model file, parsing it, and decoding its textures —
runs on worker thread(s), not the render thread. The render thread only creates GPU
resources and uploads the already-decoded data.

**Why this priority**: decode is ~90% of the measured load. If it stays on the main
thread, US1 cannot be achieved. It is P1 alongside US1 because it *is* the mechanism.

**Independent Test**: during a load, confirm the main thread is not blocked while decode
runs, and that GPU work (resource creation, upload) happens on the render thread.

**Acceptance Scenarios**:

1. **Given** a not-loaded environment, **When** loading starts, **Then** its file read,
   parse and image decode occur off the render thread.
2. **Given** decoded payloads ready, **When** the render thread picks them up, **Then** it
   creates and uploads the GPU resources on the render thread only.
3. **Given** any amount of in-flight decoding, **When** the frame is rendered, **Then** no
   GPU command-buffer, window or swapchain work happens off the render thread.

---

### User Story 3 - A burst of requests is de-duplicated and prioritised (Priority: P2)

A developer requests many environments in quick succession (cycling zones, undoing). The
pending set holds one request per asset, bounded in size; when it is full, the oldest
pending requests are dropped in favour of the most recent. The environment currently in
use is never dropped.

**Why this priority**: without it, a burst would queue every request and load environments
the developer has already moved past, wasting memory and time. The bound keeps memory
finite and the recency rule matches intent ("load where I am now").

**Independent Test**: fire more distinct load requests than the bound and confirm the
pending set never exceeds the bound, the most recent are served, and stale ones are
dropped.

**Acceptance Scenarios**:

1. **Given** N > bound distinct requests in a burst, **When** they are queued, **Then** at
   most `bound` are pending and the least recent are dropped.
2. **Given** the same asset requested twice, **When** the second request arrives, **Then**
   there is still one pending entry (de-duplicated).
3. **Given** the active zone's environment among the requests, **When** the bound is
   exceeded, **Then** the active environment's request is never dropped.

---

### User Story 4 - The readout shows loading (Priority: P2)

The editor's per-zone residency indicator distinguishes an environment that is *loading*
from one that is resident, not resident, or has no environment, so a developer can see why
a zone is momentarily empty.

**Why this priority**: async loading introduces a visible intermediate state; without a
readout a zone briefly shows nothing and looks broken. It is a small extension of the
existing indicator.

**Independent Test**: activate a non-resident environment and watch its indicator move
from loading to resident; a zone with no environment is still shown distinctly.

**Acceptance Scenarios**:

1. **Given** an environment whose load is pending or in progress, **When** the developer
   views the zones list, **Then** its indicator shows a loading state.
2. **Given** that load completes, **When** the developer looks again, **Then** the
   indicator shows resident, with no manual refresh.

---

### User Story 5 - A failed load is surfaced, not half-applied (Priority: P2)

An environment whose asset is missing or corrupt fails asynchronously. The failure is
surfaced to the developer and the zone is left in a defined state — nothing half-created,
no crash, and the failure does not leave the app stuck "loading" forever.

**Why this priority**: with synchronous loading a failure aborted the action; with async
loading the action has already happened, so a failure must be reported and the state must
settle. It matters once US1–US3 exist.

**Independent Test**: point an environment at a missing/corrupt asset, load it, and confirm
the failure is reported, the zone settles into a defined state, and no resource leaks.

**Acceptance Scenarios**:

1. **Given** an unloadable environment, **When** its load fails, **Then** the failure is
   surfaced and the zone is not left stuck in a loading state.
2. **Given** a failed load, **When** the developer looks at the zone, **Then** no partial
   environment is resident and the app keeps running.

---

### Edge Cases

- Activating a zone whose environment is already loading: no duplicate work; the existing
  request is promoted to most-recent, not re-queued anew.
- Activating a zone, then a different zone, before the first load lands: the first is
  eligible to be dropped/evicted; the second takes priority.
- Rapidly alternating between two large environments: each visit re-requests the other; the
  pending set holds both without unbounded growth; the app stays responsive.
- The active zone's environment is requested while it is being loaded or is resident: it is
  never dropped from the pending set and never evicted.
- A load that fails partway through decoding: its payload is discarded cleanly; no partial
  upload reaches the GPU.
- Shutdown with loads pending or decoding in flight: workers are stopped and their payloads
  released; no crash, no leak.
- The resident set's byte budget is still enforced: an environment loaded asynchronously is
  accounted and evicted exactly as before, and pending (not-yet-resident) environments do
  not consume the residency budget.
- Zero workers available (Bounded pool of 0? not allowed): the pool size is at least one;
  a single large environment still loads.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Activating a zone, instantiating an environment, or any other **environment** load
  trigger MUST NOT block the frame loop for the duration of that environment's load.
- **FR-002**: The CPU work of an **environment** load — reading the model file, parsing it, and
  decoding its textures — MUST run off the render thread.
- **FR-003**: GPU resource creation and upload for a load MUST run on the render thread,
  within a bounded per-frame time budget, and MAY be resumed across frames.
- **FR-004**: A zone switch MUST complete in the frame it is requested, whether or not the
  incoming environment is resident; the environment becomes resident, and visible, when its
  uploads complete.
- **FR-005**: Pending load requests MUST be de-duplicated per asset and bounded by a pending-set
  capacity (independent of the concurrent-decode limit in FR-009); when the capacity is exceeded,
  the request(s) least recently requested MUST be dropped. A load already decoding is not
  cancelled, but if its asset is no longer wanted when it completes, its decoded payload MUST be
  discarded rather than uploaded.
- **FR-006**: The environment in use (the active zone's) MUST NOT be dropped from the
  pending set, and MUST NOT be evicted while it is active.
- **FR-007**: The editor's residency readout MUST distinguish a loading environment from a
  resident one, a not-resident one, and a zone with no environment.
- **FR-008**: A load that fails MUST be surfaced as a logged error and a *failed* state on the
  zone's existing residency indicator, and MUST leave the zone in a defined state — no partial
  environment resident, never stuck loading. The zone's environment is left **awaiting** (present
  but never attached or drawn) rather than reverted to none, so the authored environment is not
  silently lost; a later request retries.
- **FR-009**: The number of loads decoding concurrently MUST be bounded by a configured limit (a
  memory bound), separate from the pending-set capacity (FR-005) and independent of the GPU.
- **FR-010**: Existing residency semantics MUST be preserved: the byte budget, the LRU
  eviction order, load-on-demand, the loud failure on non-resident use, and the byte
  accounting for a loaded asset are unchanged.
- **FR-011**: Shutdown MUST stop the workers and release any in-flight payloads without
  leaking or crashing.
- **FR-012**: The change MUST NOT move GPU work (command buffers, the swapchain, resource
  creation) off the render thread.

### Key Entities *(include if feature involves data)*

- **Load request**: a request to make one environment asset resident, de-duplicated by
  asset id and ordered by recency of request.
- **Worker**: a thread that performs the CPU half of a load (read, parse, decode) and hands
  back a decoded payload.
- **Decoded payload**: the CPU-side result of a load — parsed geometry and decoded image
  pixels — handed from a worker to the render thread, which turns it into GPU resources and
  then discards it.
- **Pending set**: the bounded, de-duplicated, most-recent-first collection of outstanding
  load requests, with the active environment pinned.
- **Residency state**: the per-asset lifecycle that now includes a *loading* state between
  not-loaded and loaded (plus a *failed* state).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Loading the largest environment no longer blocks the frame loop: frame time
  stays near the frame budget throughout the load (no single-frame stall beyond a small,
  bounded upload slice).
- **SC-002**: 100% of zone switches complete in the requesting frame, resident or not.
- **SC-003**: An asynchronously loaded environment renders identically to the synchronously
  loaded one, and appears within the load's own wall-clock (dominated by decode) after its zone
  becomes active — no additional stall beyond the budgeted upload slice.
- **SC-004**: For a burst of more distinct requests than the bound, the pending set never
  exceeds the bound and the most recently requested are the ones loaded (100% of bursts).
- **SC-005**: Rendering and residency accounting are unchanged: the same resident set bytes
  and the same LRU eviction outcomes as before.
- **SC-006**: Repeatedly loading and unloading environments produces no growth in resident
  or staging memory, and no leak at shutdown (steady state after the first cycle).
- **SC-007**: A broken environment asset never leaves a zone stuck loading and never crashes
  the app (100% of failed loads).

## Assumptions

- **The CPU/GPU split is the mechanism.** Workers only read/parse/decode; the render thread
  creates and uploads GPU resources. This follows the render API's rules (command buffers
  and the swapchain are render-thread only; submissions are serialised anyway, so
  parallelising *uploads* would gain nothing). Decode is the measured target (~4.6 s of
  ArtRoom's ~5.1 s load).
- **Pop-in, not a loading screen.** A zone shows empty (grid only) until its environment's
  uploads land; there is no blocking "loading…" screen. This is the behaviour the async
  design buys, and the readout (FR-007) explains the brief gap.
- **The bound on concurrent loads is a memory bound.** A decoded ArtRoom is ~1.3 GB of
  pixels, so the number decoding at once is set by RAM headroom, not by the GPU; the exact
  value is a plan-time decision.
- **Scope is environment assets only.** Props and actors keep their current eager load (a
  deliberate boundary carried from spec 050), so the props' startup stall is unchanged.
  Cue-driven preloading, cross-zone rendering, and texture compression remain out of scope.
- **The per-frame upload budget is small** (a few milliseconds), so uploads spread across
  frames; an environment that uploads in one budget-sized slice is unaffected.
- **Residency is unchanged.** This feature changes *when* and *on which thread* a load
  happens, not what is resident or how it is accounted (spec 050 governs that).
