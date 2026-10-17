# Feature Specification: Codebase Review Remediation

**Feature Branch**: `098-codebase-review-fixes`

**Created**: 2026-10-17

**Status**: Implemented (2026-10-17) — build clean, 729/729 tests

**Input**: User description: "Run a full review of the codebase for unnecessary abstraction, excessive repetition, and performance issues, then fix what the review confirms." The review was carried out by nine parallel reviewers covering every production cluster, the narrative engine, and the test suite; the confirmed findings are recorded in the Finding Register below and are the input to this feature.

## Origin

The codebase is healthy in its core disciplines: the frame loop, slot-map access, cue walk, input snapshot, physics adapters and asset async bounds were all found to be allocation-conscious and structurally sound. The review nevertheless confirmed three classes of debt:

- **Correctness adjacent to the reviewed concerns.** An authored name containing a format specifier can reach a variadic native text call; a per-frame staging buffer is mapped without cycling while multiple frames are in flight; a failed project write is recorded as written; an actor transform drag rebuilds a physics body every frame; the static-scene batch never releases an evicted environment; actor textures resolve through a hardcoded character folder; a public key case can alias other keys; and the narrative pass restriction can miss a nested premise.
- **Per-frame cost that scales with authored content rather than with change.** The static scene is gathered and uploaded every frame; every editor panel body runs even when its tab is hidden; asset-name lists are sorted per frame; the cue tree is rebuilt per frame; the pinned-asset set is rebuilt and locked per frame; the debug grid is rebuilt per frame; and a set of small reference-tuple, list and palette allocations sits on play and draw paths that explicitly claim to allocate nothing.
- **One concept implemented several times.** The editor has two undo steppers and seven copies of the same apply-and-record sequence; hidden-window helpers, name combos, dialogue controls, pipeline setup, float-array parsing, capsule offsets, body traversals and name scans are each written three or more times; and the test suite repeats its headless fixtures across more than a dozen files, with the cue pretty-printer already drifted between two copies.

The review also found dead public surface retained by tests or by no caller, and documentation/comments that no longer match behaviour. The cost today is bounded but grows with every scene, panel and test added; two of the first group are user-visible failure paths (undefined native behaviour and lost persistence), so they are fixed in the same pass rather than deferred.

## Scope and Disposition

**In scope:** every confirmed finding in the Finding Register, grouped as correctness (P1), per-frame cost (P1/P2), single-implementation cleanup (P2), test-suite consolidation (P2), and latent narrative hardening (P3).

**Out of scope:** speculative items the reviewers explicitly flagged as unverified or low-value (JIT elision at call boundaries, `bool` marshalling, the GPU release-policy disagreement, the one-shot actor-mesh upload, the unused colour-target usage flag, `Entity option` allocation in editor-only paths, and passing the gameplay state by `inref`). Those are recorded in the register as "verify/exclude" and are spiked only to record a disposition; any change to them requires a new or amended spec. V-03 (GPU release policy) and V-08 (sound voice rewind) are verify-only in this feature. Narrative integration with the game, new features, and architectural moves between clusters are also out of scope.

**Disposition rule:** the register is static-review evidence, not a proven defect list. Every finding MUST be reproduced or instrumented before it is changed; a finding that does not reproduce is recorded as excluded with the reason, not forced into a fix. Performance fixes MUST preserve observable behaviour.

## Clarifications

No clarifications were required. Scope, priorities, the retry semantics for persistence, the treatment of the latent narrative defects, and the definition of "no per-frame allocation" are settled in Requirements and Assumptions.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Authored data cannot crash, corrupt or be lost (Priority: P1)

An author types any character into any name, scenario, asset or dialogue field; saves while another process briefly holds the project file; drags an actor repeatedly; switches between environments; or loads a second character with its own texture folder. The editor renders the text literally, retries the save until it succeeds, moves the actor body without rebuilding it, releases the environment it left, and loads the new character's textures from that character's own location.

**Why this priority**: undefined native behaviour, silent loss of the last edit, and unbounded stale GPU references are failures that no amount of performance work compensates for.

**Independent Test**: type `%s`, `%d` and `%n` into every authorable name field and confirm the literal text appears; make the project file temporarily unwritable, edit, restore write access, and confirm the latest library reaches disk; drag an actor for several seconds and confirm the physics body count is stable; switch A→B→A and confirm resident submesh count and memory return to the A baseline; add a second actor with a differently named texture folder and confirm it loads.

**Acceptance Scenarios**:

1. **Given** an authored string containing `%s`, `%d`, `%n` or any other format specifier, **When** it is shown in a panel, overlay or tree row, **Then** it is rendered literally and no native call interprets it as a format.
2. **Given** a project write fails, **When** the library is unchanged and a later write opportunity occurs, **Then** the write is retried, the failure is reported at least once, and a subsequent successful write leaves the file equal to the latest library.
3. **Given** an actor is moved or rotated by drag or nudge, **When** the transform is applied, **Then** the existing body is updated in place; a scale change MAY rebuild the body because the shape depends on it.
4. **Given** an environment is replaced, **When** the previous environment's geometry was registered for instanced drawing, **Then** the previous registration is released, no stale submesh remains reachable, and a later pointer reuse cannot map new geometry onto a stale slot.
5. **Given** the renderer keeps multiple frames in flight, **When** a per-frame staging buffer is reused for several consecutive frames, **Then** no frame reads data another frame has overwritten.
6. **Given** an actor asset whose textures live beside its model or in a folder named by its manifest, **When** it is loaded, **Then** its textures resolve from that actor's own location and no character name is hardcoded.
7. **Given** a digit key outside the supported set, **When** its input mask is constructed, **Then** the construction fails loudly instead of aliasing another key.
8. **Given** an eager actor or prop load that fails after acquiring textures or creating buffers, **When** the failure is reported and the asset skipped, **Then** the acquired resources are released rather than retained for the session.

---

### User Story 2 - Frame cost follows change, not scene size (Priority: P1)

An editor works in a zone with hundreds of static instances, several panels open, a populated outliner, a cue tree and a music cross-fade. Once nothing has changed, the frame does no static-scene gather or upload, no hidden panel body, no name sorting, no cue-tree rebuild, and no pinned-set lock; the play loop and draw paths allocate nothing beyond what the existing allocation tests already permit.

**Why this priority**: the editor is the primary authoring surface, and the measured costs scale with authored content; a large scene spends its frame budget on work that is already known to be unchanged.

**Independent Test**: instrument the static batch and render calls; with no edits, consecutive frames perform zero gathers and zero uploads. With a panel tab hidden, its body executes zero statements. Allocation harnesses over each named play/draw path report zero bytes. A 1000-frame idle run reports no growth in managed allocation or retained submeshes.

**Acceptance Scenarios**:

1. **Given** a static scene and no transform, add, remove, environment or asset change, **When** consecutive frames render, **Then** the scene is not gathered or uploaded again.
2. **Given** an editor panel whose dock tab is hidden or whose window is collapsed, **When** the editor draws its panels, **Then** that panel's body and per-frame setup do not run, while the window still ends correctly.
3. **Given** an unchanged asset set, **When** a panel or cue editor opens a name list, **Then** the sorted names/indices are read from a cache invalidated by asset changes, not rebuilt per frame.
4. **Given** an unchanged cue value, **When** the cue tree draws on consecutive frames, **Then** its row model and labels are not rebuilt.
5. **Given** an unchanged active environment, **When** the gameplay update runs, **Then** the pinned-asset set is neither allocated, cleared, refilled nor locked.
6. **Given** edit mode with no camera or selection change, **When** the viewport draws, **Then** the static grid is reused rather than rebuilt.
7. **Given** an unchanged scene, **When** the outliner and inspector draw, **Then** they do not allocate per-row identifiers or reformat unchanged transforms every frame.
8. **Given** a music level, master or cross-fade ramp is running, **When** the cue engine ticks, **Then** only the affected decks are updated; the whole mix is not rebuilt and unrelated decks are not re-gained every tick.
9. **Given** an actor is drawn, **When** its palette is uploaded, **Then** the contiguous skinning data is uploaded without per-joint repacking, the posed model and normal matrix are each computed once per actor per frame, and each slot's animation frame window is computed once per blend rather than per joint.
10. **Given** a music cue, actor blend or camera follow runs for a frame, **When** it completes, **Then** it has allocated no reference tuples, lists or closures on the play path.
11. **Given** a scene grows by one instance, **When** the instance buffer next grows, **Then** capacity grows geometrically and the previous buffers are retired as already designed.
12. **Given** a cue that forks at the front of a sequence and whose inner cue is still running, **When** subsequent idle ticks occur, **Then** the rewritten cue shape is retained rather than rebuilt each tick.
13. **Given** a slot map containing dead slots, **When** a scan visits it, **Then** dead payloads are not copied and only live payloads reach the callback.
14. **Given** an unchanged locomotion choice, **When** a play frame updates the player, **Then** the animation clip is not re-resolved by name and the slots are not rebuilt.
15. **Given** a runtime model transform, **When** its matrix is composed, **Then** the translation is written directly rather than multiplied through a translation matrix.
16. **Given** a loaded IQM asset, **When** the runtime holds its model, **Then** geometry and header fields that no consumer reads are not retained, and frame channels use narrow storage.

---

### User Story 3 - One implementation per concept (Priority: P2)

A maintainer changes how an editor edit becomes an undo step, how a hidden window is guarded, how a cue node is edited, how a dialogue presentation is chosen, how a capsule offset is computed, or how a project manifest is read. Each change is made in exactly one place, and the compiler or a single grep finds every caller.

**Why this priority**: the duplicates are correct today but already drifting; every one of them turns a one-place change into a many-place change and makes the next feature more expensive. It is below correctness and frame cost because it does not by itself break a user flow.

**Independent Test**: the named patterns each appear once in the source; the full suite is green after every consolidation; deleted exports have no remaining callers by compile or grep.

**Acceptance Scenarios**:

1. **Given** an edit command, **When** it is recorded, **Then** it goes through the one undo-commit path; the production module exposes no second, test-only stepper.
2. **Given** the selection edits, **When** each command is recorded, **Then** all of them use the shared commit helper, and the apply-and-record sequence appears once.
3. **Given** a state-level command, **When** it is classified for placement, application or inversion, **Then** one classifier answers all three and a new command cannot be silently omitted from one of them.
4. **Given** a NUL-terminated text buffer, seeding, or a selectable row, **When** a panel uses it, **Then** it calls one shared binding helper; no panel defines its own copy.
5. **Given** a graphics pipeline, **When** it is created, **Then** one builder owns the pinned-descriptor and release lifecycle, and a shared shader is compiled once per required stage pair.
6. **Given** a cue, **When** it is edited from the Cue panel or from a trigger's inline editor, **Then** both surfaces run the same editor pipeline with only identity and write-back parameterised.
7. **Given** a dialogue presentation, **When** it is chosen in the Dialogue panel or the cue editor, **Then** both use the same kind/speaker/expression control.
8. **Given** a waypoint, trigger or actor name combo, **When** it is drawn, **Then** one combo implementation serves all three.
9. **Given** a glTF float array, **When** it is parsed, **Then** one helper validates arity and produces the array for lists, vectors and rotations.
10. **Given** an entity position and a capsule collider, **When** the body centre is computed or unwound, **Then** one definition of the feet-to-centre offset is used by spawn, sync and scripted movement.
11. **Given** the bodies in a zone, **When** they are deactivated, activated or released, **Then** one traversal visits actors, props and parts for all three operations.
12. **Given** a by-name entity lookup for an actor, waypoint or trigger, **When** it scans, **Then** one helper performs it and stops at the first match.
13. **Given** an asset or audio load, **When** a failure is reported and the entry skipped, **Then** one load-and-report helper performs it.
14. **Given** an export, **When** it has no production caller and no test contract, **Then** it is removed or made private; the review's confirmed dead exports do not remain.
15. **Given** a comment or document that states behaviour the code no longer has, **When** the feature is complete, **Then** the statement is corrected.
16. **Given** the editor's panel cycle, **When** it lists panels, **Then** it names only windows that exist and includes every docked panel, including Songs.
17. **Given** a diagnostic no-op variant or a load-queue forwarder, **When** the call is made, **Then** one helper or the queue's own API owns it; dead mutability is removed.
18. **Given** a scenario fork or a viewport pick, **When** it runs, **Then** no throwaway zone is built and no screen ray is computed twice.
19. **Given** the inspector draws colour or committed-text controls, **When** it does so, **Then** a shared scaffold is used; authored entities and asset bindings use their constructors rather than re-spelled literals.
20. **Given** a timed op, a waypoint chain or an actor detach, **When** they run, **Then** one progress rule, one single-pass chain resolution and one detach implementation below both callers are used.

---

### User Story 4 - The test suite is a reliable authoring tool (Priority: P2)

A maintainer adds a field to the shared state or gameplay state, changes a cue op, or runs a single test in isolation. One fixture implementation absorbs the field addition, the cue descriptions do not drift, and the isolated test passes without depending on a previous test's emissions.

**Why this priority**: the suite is the project's main correctness tool; duplicated fixtures and shared mutable state make it brittle and slow to extend, but they do not affect shipped behaviour.

**Independent Test**: adding a field to the shared state requires one fixture edit; the cue description is defined once; every test passes when run alone and the suite passes in its normal order; the suite duration does not regress by more than 10%.

**Acceptance Scenarios**:

1. **Given** headless shared/gameplay state and entity/actor/prop fixtures, **When** a test needs one, **Then** it calls the one shared fixture; the copy-pasted fixtures in the review's list are gone.
2. **Given** a test that mutates shared state, **When** it runs, **Then** it owns that state; no module-level mutable value is shared across facts, and the test passes in isolation and in any order.
3. **Given** a cue value, **When** tests describe it, **Then** one description implementation is used and the two previously drifted representations agree.
4. **Given** an asynchronous load test, **When** it waits for workers, **Then** it waits on a deterministic signal with a bounded timeout rather than sleeping and polling.
5. **Given** a test checks an animation or clip weight, **When** it asserts, **Then** it calls the production accessor and asserts the optional result rather than a sentinel or a parallel implementation.
6. **Given** a test measures per-operation allocation, **When** it does so, **Then** it uses one shared measurement helper with the warm-up rule recorded once.
7. **Given** temp-directory/manifest setup or a loader defaulting claim, **When** a test needs it, **Then** one helper owns the lifecycle and the claim is exercised at the loader rather than asserted on a pre-built fixture.
8. **Given** a test asserts on member names, reference identity, a subsumed determinism run or 200 identical builds, **When** it is reviewed, **Then** it asserts observable behaviour and the production report renderer is used.
9. **Given** single-use or dead test helpers, duplicate assertion helpers or an asset rig a test never reads, **When** they are consolidated, **Then** one helper remains and a "no write" assertion compares content as well as timestamp.

---

### User Story 5 - The narrative engine cannot silently miss a conclusion (Priority: P3)

A rule author adds a rule whose nested premise is another derived construct. When that construct appears later in the same fixpoint, the rule runs and its conclusion is attributed. A commit that changes no projected fact does not recompute the whole fixpoint, and reading an already-canonical reason list does not sort it again.

**Why this priority**: the engine has no game call site yet, so the defect is latent; it must be fixed before integration makes it observable, and the performance rules keep replay cost proportional to change.

**Independent Test**: a scenario whose rule has a nested second premise that is derived in a later pass produces the expected conclusion; a rule set with no matching projection reports no new facts and performs no derivation pass; reading reasons returns the stored canonical order without re-sorting.

**Acceptance Scenarios**:

1. **Given** a rule with a nested second premise, **When** that premise is absent at the rule's first evaluation, **Then** the rule is recorded as mentioning the nested construct and is re-evaluated when it appears.
2. **Given** an event that no projection establishes or withdraws, **When** it is committed, **Then** no full fixpoint recomputation occurs and the history still records the event.
3. **Given** a fact with reasons, **When** its reasons are read from the engine, **Then** the already-canonical stored order is returned without de-duplication or sorting.
4. **Given** a conclusion already present in the fact set, **When** it is derived again, **Then** the fact set is not rebuilt and the existing reasons are preserved.
5. **Given** rule evaluation, bound checking, fact dropping and participant indexing, **When** a commit runs, **Then** the confirmed redundant traversals and per-conclusion allocations are removed; any remaining index or scan is justified by measurement.
6. **Given** a harness report, **When** it sorts and returns facts, **Then** facts are not re-rendered per comparison and history/facts are not recomputed; dead narrative surface is removed.

---

### Edge Cases

- **A format specifier in an asset name, scenario name, diagnostics message or trigger name.** It must render literally everywhere, including the debug overlay and tree rows, not only in the panel where it was typed.
- **A project write that fails persistently** (read-only directory, full disk). The failure stays visible and retries are bounded so the editor does not issue an unbounded stream of writes or errors per second; a later success leaves the file equal to the latest library.
- **A failed write followed by no further change.** The retry must happen without requiring the author to make another edit; if the failure persists, the unsaved state remains observable.
- **An environment switched A→B→A.** Both switches must release the previous registration; returning to A must rebuild and draw correctly, not reuse a stale or pointer-aliased slot.
- **A hidden panel whose direct-write fields are changed by another surface.** Skipping the hidden body must not discard or overwrite state; the next visible frame reflects the current state.
- **A panel that is the active tab but collapsed.** Its body is skipped exactly as when hidden, and the window still ends.
- **Allocation measurement on the first frame.** Warm-up work (buffer creation, first upload, cache population) is excluded from the steady-state measurement, as the existing tests already do.
- **A music ramp that changes a row's loop state.** A targeted update must still apply loop changes, not only gain.
- **An actor whose body is missing.** Transform application must not create a body that did not exist, and must not fail; the existing no-body path is preserved.
- **A static scene edited while the batch is clean.** The edit must mark the batch dirty before the next draw; a missed invalidation is a visible stale scene, not a silent optimization.
- **An asset load settling while names are cached.** The cache must invalidate on settle; a stale name list would hide a newly loaded asset.
- **A rule with several nested premises.** Recording only the first source is the defect; all nested constructs must be recorded, including conditional binds.
- **A commit whose projected reasons change but whose fact set does not.** Reason edits still apply; only the derivation pass is skipped.
- **Dead-export removal.** A symbol that looks test-only but is a deliberate headless seam must be kept and documented as such rather than deleted.
- **A reviewer finding that does not reproduce.** It is excluded with a recorded reason; the implementation must not invent a change to satisfy the register.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every authored string shown in any UI surface MUST be rendered literally; no UI path may interpret `%` sequences as a format.
- **FR-002**: A transfer buffer written on consecutive frames MUST remain correct while multiple frames are in flight; one-shot uploads MAY retain their current mapping behaviour.
- **FR-003**: A project write failure MUST NOT be recorded as a successful write; a subsequent persist opportunity MUST retry the latest library, report the failure at least once, and retry at most once per 0.5 seconds rather than once per frame.
- **FR-004**: Moving or rotating an actor MUST update an existing physics body in place. A scale change, whose shape depends on it, MAY rebuild the body.
- **FR-005**: An input mask for an unsupported digit key MUST fail loudly rather than collide with another key's bit.
- **FR-006**: Actor texture resolution MUST be derived from the actor's own asset location or manifest; no character name may be hardcoded.
- **FR-007**: The static-scene batch MUST release the identity registration and retained submeshes of an environment it no longer draws, and MUST NOT allow a reused GPU handle to alias a stale registration.
- **FR-008**: A slot scan MUST NOT copy the payload of a dead slot; only live payloads are read by callbacks. This is a US2 performance requirement; it is numbered with the safety group only for continuity.
- **FR-009**: The static scene MUST be gathered and uploaded only when its contents, transforms or environment change; an unchanged scene performs neither on subsequent frames.
- **FR-010**: A hidden or collapsed editor panel MUST NOT execute its body or its per-frame setup, while still ending its window.
- **FR-011**: Sorted asset name/index lists MUST be cached and invalidated when the asset set changes.
- **FR-012**: The cue tree's row model and labels MUST be reused for an unchanged cue value.
- **FR-013**: The active-environment pin set MUST NOT be allocated, cleared, refilled or locked when the pinned id is unchanged.
- **FR-014**: The editor grid MUST be built once per edit-mode activation and reused while the camera and selection are unchanged.
- **FR-015**: The outliner and inspector MUST NOT allocate per-row identifiers or reformat unchanged transform values every frame.
- **FR-016**: The named play/draw paths MUST allocate zero managed bytes in steady state: camera follow, dialogue/prompt line layout, diagnostics readout, per-frame diagnostics reporting, actor palette upload, and the posed model/normal matrix computation.
- **FR-017**: A running music level/master/cross-fade ramp MUST update only the affected decks and MUST NOT rebuild or reconcile the whole mix per tick.
- **FR-018**: Per-slot animation frame windows MUST be computed once per blend, not once per joint × slot.
- **FR-019**: Instance-buffer capacity MUST grow geometrically when more instances are needed.
- **FR-020**: Where the affected joint subtree is exactly identifiable, Sway MUST recompose only the changed joints and their descendants rather than the whole skeleton. The F-27 verify spike records whether that subtree is available; if it is not, the finding is excluded and the full recompose is retained.
- **FR-021**: A blocked fork at a sequence front MUST retain its rewritten cue shape across idle ticks.
- **FR-022**: Every confirmed dead **public** export in the review MUST be removed or made private, unless it is a documented headless test seam; the decision is recorded.
- **FR-023**: Exactly one undo-stepping implementation MUST exist in production; any test-only stepper MUST be removed and its tests routed to the production history.
- **FR-024**: Every selection edit MUST record through the one undo-commit helper; the apply-and-record sequence MUST appear once.
- **FR-025**: State-level command classification MUST be expressed once and used by placement, application and inversion.
- **FR-026**: NUL-terminated buffer reading, buffer seeding and selectable-row drawing MUST each have one shared implementation.
- **FR-027**: Graphics-pipeline construction MUST use one builder for descriptor pinning, release and the common raster/depth/colour setup; a shared shader stage MUST be compiled once.
- **FR-028**: The Cue panel and the trigger inline editor MUST share one cue-editor pipeline.
- **FR-029**: The Dialogue panel and the cue editor MUST share one dialogue kind/speaker/expression control.
- **FR-030**: Waypoint, trigger and actor name selection MUST share one combo implementation.
- **FR-031**: glTF float-array decoding MUST use one validating helper for scalar lists, vectors and rotations.
- **FR-032**: The feet-to-capsule-centre offset MUST have one definition used by spawn, actor sync and scripted movement.
- **FR-033**: Deactivate, activate and release MUST share one body traversal over actors, props and parts.
- **FR-034**: By-name slot resolution MUST use one helper that stops at the first live match.
- **FR-035**: Asset and audio load-and-report-on-failure MUST use one helper.
- **FR-036**: Headless shared state, gameplay state, and entity/actor/prop fixtures MUST each have one shared test implementation.
- **FR-037**: Tests MUST NOT share mutable state through module-level values; every test MUST pass in isolation and in any order.
- **FR-038**: The cue description/pretty-printer used by tests MUST exist once, and the two previously drifted representations MUST agree.
- **FR-039**: Asynchronous test waits MUST use a deterministic signal with a bounded timeout of at most 3 seconds rather than sleep-polling.
- **FR-040**: Tests MUST assert against production accessors and optional results, not parallel implementations or sentinel fallbacks.
- **FR-041**: Per-operation allocation measurement MUST use one shared helper with a single warm-up rule.
- **FR-042**: A rule's declared mentions MUST include every construct bound by a nested source, so a premise that appears later re-enables the rule.
- **FR-043**: A commit whose event matches no projection MUST NOT perform a full fact-map drop or derivation pass; reason-only changes MUST still apply.
- **FR-044**: Reading reasons from the engine MUST return the already-canonical stored order without re-deduplicating or re-sorting.
- **FR-045**: Adding a derived fact MUST determine novelty and insert in one logical traversal, without a separate containment lookup and duplicate sort.
- **FR-046**: Comments and documents that state retired behaviour MUST be corrected in the same change that retires it.
- **FR-047**: An eager actor or prop load that fails partway MUST release the textures, meshes and buffers it already created before reporting and skipping the asset.
- **FR-048**: The editor's panel list MUST name only panels that exist and MUST include every docked panel.
- **FR-049**: When an actor's locomotion choice is unchanged, the play loop MUST NOT re-resolve its animation clip by name or rebuild every animation slot.
- **FR-050**: Per-frame model-matrix composition MUST NOT perform a redundant translation multiply when the translation can be written directly.
- **FR-051**: IQM loading MUST NOT retain geometry or header fields that no runtime consumer reads, and MUST store frame channels in the narrowest representation the data supports.
- **FR-052**: Internal diagnostic no-op variants, load-queue forwarders and dead mutability MUST be collapsed to one helper or removed.
- **FR-053**: Cold-path waste in the scenario fork and picking paths MUST be removed: no throwaway zone for a capacity check, and no duplicated screen-ray computation.
- **FR-054**: Inspector colour/text scaffolds and authored entity/asset-binding literals MUST use shared helpers or constructors.
- **FR-055**: Ramp progress and waypoint-chain resolution MUST each be expressed once, and the chain MUST be resolved in a single pass with array indexing rather than list indexing.
- **FR-056**: Actor body detach MUST live in a module compiled below both its cue-engine and scenario-fork callers.
- **FR-057**: Test temp-directory/manifest setup, the loader's name-defaulting claim, the narrative harness's repeated runs, the timestamp-only write assertion, and single-use test helpers not already covered by FR-036 to FR-041 MUST be consolidated or corrected.
- **FR-058**: Narrative rule evaluation, the derivation bound check, inferred-fact dropping, participant indexing and report construction MUST NOT perform the confirmed redundant traversals or per-conclusion allocations.
- **FR-059**: Dead narrative public surface and unreachable canonical branches MUST be removed.
- **FR-060**: Every register item marked Fix MUST be addressed, every Verify item MUST be resolved with recorded evidence, and every Exclude item MUST remain unchanged; the plan and tasks MUST cite register IDs.

### Key Entities *(include if feature involves data)*

- **Finding**: One reviewed observation with a category (abstraction, repetition, performance, correctness-adjacent), severity, evidence, and a disposition (fix, verify, exclude). The Finding Register is the authoritative list.
- **Static scene batch**: The per-zone identity registry, lookup and instance counts for instanced static geometry, plus the dirty state that says whether it must be rebuilt.
- **Asset name index**: The cached, sorted view of loaded actor/music/sound names used by authoring combos.
- **Actor manifest texture directory**: The optional root-relative directory an actor's textures resolve from; empty means the model's own directory.
- **Project write state**: The last successful write, the file text last written or read, and the bounded retry state that prevents a failed write from being treated as saved.
- **Pinned asset set**: The active environment's assets that must not be evicted; changes only when the active environment changes.
- **Undo history**: The single ordered record of applied and redoable commands, with one commit and one step implementation.
- **Headless fixture**: A device-free shared/gameplay state and entity/actor/prop constructor used by tests instead of production bring-up.
- **Cue description**: The textual rendering of a cue used by tests, which must not diverge between files.
- **Transfer buffer lifetime**: The relationship between a mapped staging buffer and the frames in flight that may still read it.
- **Reason list**: The canonical, de-duplicated, ordered justifications stored with a narrative fact.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of authored strings containing format specifiers render literally in every UI surface; 0 crashes or corrupted rows.
- **SC-002**: A sequence of at least 1000 rendered frames with no scene, environment or asset change performs 0 static-scene gathers and 0 static-scene uploads.
- **SC-003**: Each hidden/collapsed panel executes 0 body statements, verified by the visibility guard and the manual walkthrough; with all panels hidden the editing frame's user-side panel work is 0.
- **SC-004**: The named play/draw paths allocate 0 managed bytes in steady state, verified by the shared allocation harness.
- **SC-005**: A failed write followed by a successful retry leaves exactly the latest library on disk; 0 edits are lost, and persistent failure retries at most once per 0.5 seconds rather than once per frame.
- **SC-006**: Moving or rotating an actor produces 0 body rebuilds over at least 120 consecutive drag frames; a scale change produces exactly 1.
- **SC-007**: Switching away from an environment releases 100% of its registered submeshes; returning to it rebuilds and draws correctly, and resident submesh count returns to the pre-switch baseline.
- **SC-008**: Every duplicate pattern named in FR-023 to FR-035 exists in exactly one place, verified by grep count and a green build.
- **SC-009**: Every confirmed dead export is gone or documented as a retained seam; 0 remain silently unreferenced.
- **SC-010**: Every test passes in isolation and in the suite's normal order; 100% of tests pass with no ordering dependency.
- **SC-011**: The full suite passes with 0 failures and 0 warnings, and its duration increases by no more than 10% relative to the 705-test baseline.
- **SC-012**: A narrative rule with a later-appearing nested premise produces its conclusion in 100% of runs; a no-projection commit performs 0 derivation passes; reading reasons performs 0 re-sorts.
- **SC-013**: Every "verify/exclude" finding is either reproduced and fixed or recorded as excluded with evidence; 0 are changed speculatively.
- **SC-014**: No public behaviour changes except those explicitly required by FR-001 to FR-007, FR-048 and FR-042 to FR-045; FR-049, FR-050 and FR-051 MUST be output-preserving; existing behavioural assertions continue to hold.
- **SC-015**: An idle editing frame with an unchanged scene performs 0 cue-row rebuilds, 0 asset-name sorts, 0 grid rebuilds, 0 pin-set locks or allocations, and 0 per-row identifier allocations.

## Assumptions

- **The Finding Register is the scope baseline.** It captures the confirmed findings from the nine-reviewer pass, including the reviewer-flagged uncertainty for each item. The plan and tasks MUST trace to it.
- **Verification precedes change.** The review was static; the implementation MUST reproduce each correctness finding and instrument each performance claim before changing code. A non-reproducing finding is excluded with a recorded reason.
- **The two-frame-in-flight concern is confirmed against the graphics API's documented contract**, not assumed from the call shape. If the contract shows `cycle = false` is safe for a recurring map, the finding is excluded.
- **Test count baseline is 705 passing tests at spec time.** New tests are added for the corrected behaviours; the total may grow.
- **The narrative engine has no game call site yet.** Its fixes are validated headlessly and do not wait for integration.
- **Texture resolution may use the manifest or the model's sibling folder**; no new required manifest field is introduced unless the existing data cannot express it. Existing assets keep loading.
- **No new dependencies** are introduced for this feature.
- **Performance fixes are behaviour-preserving.** Where a fix would change ordering, timing or visible output, it is out of scope unless the change is itself a listed correctness fix.
- **Retained headless seams are allowed.** A test-only accessor that is the only way to exercise a subsystem is kept and documented; the review's "dead" label is overridden by an explicit seam decision.
- **Documentation is part of the change.** `docs/`, module comments and the statements identified as drifted are updated; historical `specs/` directories are not rewritten.
- **The working tree is the delivery surface.** No branch is required by this spec; the implementation lands as a coherent change set with the build and suite green.

## Appendix: Finding Register

Each finding is evidence from the review, not a pre-approved defect. `Fix` means the reviewer's evidence was specific enough to plan against; `Verify` means reproduce or instrument first; `Exclude` means do not change. Evidence cites `file:line` at review time; line numbers may move.

### Correctness-adjacent

| ID | Finding | Evidence | Disposition |
|---|---|---|---|
| F-01 | Dynamic strings are passed to a variadic native text call, so `%` sequences are interpreted as formats. | `render/ImGui.fs:64-65`; callers `render/DebugOverlay.fs:27,30`, `editor/AdventPanel.fs:61`, `editor/Inspector.fs:54,104`, `editor/ScenarioPanel.fs:105`, `editor/SongPanel.fs:114`, `editor/SoundPanel.fs:36`, `editor/CueTree.fs:597` | Fix |
| F-02 | Per-frame staging buffers map with `cycle = false` while multiple frames are in flight. | `render/InstanceBuffer.fs:89`, `render/DebugDraw.fs:351`, `render/Ui.fs:513,520`, `world/render/ActorRender.fs:253` | Fix (confirm API contract first) |
| F-03 | A failed project write is recorded as written, so the edit is never retried. | `world/Project.fs:320-324` | Fix |
| F-04 | Actor textures resolve through a hardcoded `"akane"` folder. | `world/assets/Assets.fs:245` | Fix |
| F-05 | Static-batch identity registry is append-only and keyed by raw GPU handle; evicted geometry is retained and a reused handle can alias a stale slot. | `world/render/EnvironmentBatch.fs:25-34,52-57`; no reset in tree | Fix |
| F-06 | Moving/rotating an actor destroys and recreates its physics body every drag frame. | `world/Zone.fs:366-376` vs in-place prop path `:346-359` | Fix |
| F-07 | `Key.Digit` outside 1..3 aliases control, space and letter keys. | `core/Input.fs:56-62` | Fix |
| F-08 | Narrative mention analysis records only the first source of a nested bind, so a later-appearing premise never re-enables the rule. | `narrative/Rules.fs:137-144`; `narrative/Derive.fs:82-87` | Fix |
| F-09 | Eager actor/prop builds leak earlier textures, meshes and storage buffers when a later step throws. | `world/assets/Assets.fs:215-322,473-483,781-790` | Fix |
| F-10 | Stale editor panel list names a retired panel and omits the Songs panel. | `editor/Editor.fs:20`; `editor/SongPanel.fs:71` | Fix |

### Per-frame performance

| ID | Finding | Evidence | Disposition |
|---|---|---|---|
| F-11 | Static scene is gathered twice and uploaded every frame regardless of change. | `screen/GameplayScreen.fs:170-171`; `world/render/EnvironmentBatch.fs:118-143`; `render/InstanceBuffer.fs:86-102` | Fix |
| F-12 | Every panel body runs while its tab is hidden or collapsed. | 12 discarded `beginWindow` results: `editor/*Panel.fs`, `editor/Outliner.fs:39`, `editor/Inspector.fs:218`, `editor/AssetBrowser.fs:10`, `editor/CueEditor.fs:378`, `screen/MainMenuScreen.fs:14` | Fix |
| F-13 | Asset name lists are enumerated and sorted per frame from multiple panels. | `world/assets/Assets.fs:350-351,762-772`; callers `editor/CueEditor.fs:192,361-371,420,500`, `editor/DialoguePanel.fs:78,87`, `editor/SongPanel.fs:69`, `editor/SoundPanel.fs:20` | Fix |
| F-14 | Cue tree row model and labels are rebuilt every editing frame despite a "cold path" comment. | `editor/CueTree.fs:155-179,593` | Fix |
| F-15 | Pinned asset set is allocated, cleared, refilled and locked every update even when unchanged. | `screen/GameplayScreen.fs:56-62`; `world/assets/AssetLoad.fs:147-150`; `world/assets/Assets.fs:559-565` | Fix |
| F-16 | Debug grid (402 lines) is rebuilt every frame; accumulator is passed by value per vertex. | `editor/Viewport.fs:115-118`; `render/DebugDraw.fs:173-186,312-331` | Fix |
| F-17 | Outliner formats identifiers per row per frame and repeats four slot-map loops. | `editor/Outliner.fs:24-36,84-191` | Fix |
| F-18 | Inspector formats three `%A` values every frame. | `editor/Inspector.fs:251-253` | Fix |
| F-19 | Diagnostics readout copies the report list every frame once an error exists. | `core/Diagnostics.fs:56`; `render/DebugOverlay.fs:30` | Fix |
| F-20 | Camera follow returns a reference tuple every play frame. | `world/Systems.fs:181-188,197` | Fix |
| F-21 | Dialogue/prompt layout returns reference tuples per frame while comments claim zero allocation. | `render/Ui.fs:387`; `world/render/DialogueRender.fs:168-179,230`; `world/render/PromptRender.fs:63-66` | Fix |
| F-22 | Actor palette is repacked per joint and the posed model/normal matrix are computed twice per actor per frame. | `world/render/ActorRender.fs:245-266,290-292`; `render/Mesh.fs:98,135` | Fix |
| F-23 | Animation frame window is recomputed per joint × slot. | `world/Actor.fs:275-283` | Fix |
| F-24 | Music ramps rebuild the mix, allocate per deck and reconcile every deck per tick. | `state/CueEngine.fs:206-211,225-228,257-262`; `audio/Music.fs:181-189` | Fix |
| F-25 | Instance buffer grows to exact size, reallocating on each added instance. | `render/InstanceBuffer.fs:46-55` | Fix |
| F-26 | Slot scans copy dead-slot payloads; large structs make this measurable on 512-slot maps. | `core/SlotMap.fs:97-101` | Fix |
| F-27 | Sway recomposes the whole skeleton after the pose was already composed. | `world/Sway.fs:301` | Verify (only if the exact affected subtree is available) |
| F-28 | A blocked fork at a sequence front rebuilds the cue shape every idle tick. | `state/CueEngine.fs:884-889` | Fix |
| F-29 | Player animation re-resolves its clip by name and rewrites every slot each play frame. | `world/Systems.fs:211-215`; `world/render/ActorRender.fs:223-240`; `world/Actor.fs:319-330` | Fix |
| F-30 | Static gather allocates closures and repeats residency/dictionary work per instance. | `world/render/EnvironmentBatch.fs:87-111,118-138` | Fix (with F-11) |
| F-31 | Dynamic UI/model matrices use a full translation multiply where direct row writes exist. | `world/Entity.fs:24-25` vs `world/Actor.fs:61-66` | Fix |
| F-32 | Lyrics/asset load paths allocate lists and pin sets per frame while settling. | `world/assets/AssetLoad.fs:112,123-124,136,155`; `world/assets/Assets.fs:579-581,634-643` | Verify |
| F-33 | Undo restore rebuilds the free list and rewrites whole slots. | `core/SlotMap.fs:134-141` | Verify (cold path) |
| F-34 | Copy passes are opened per upload subsystem and per actor. | `screen/GameplayScreen.fs:171-178` | Exclude for now (low value, measured non-bottleneck) |
| F-35 | IQM retains unread geometry (tangents, first-vertex counts) and widens frame channels from `uint16`. | `core/Iqm.fs:31-32,61,221-222,250` | Fix |

### Unnecessary abstraction and dead surface

| ID | Finding | Evidence | Disposition |
|---|---|---|---|
| F-36 | Two undo steppers exist; the `UndoStack` one is production-dead. | `state/Command.fs:283-287,313-331`; `state/Undo.fs:215-230` | Fix |
| F-37 | `Selection` repeats the apply-and-record sequence six times; `AssetBrowser` once more. | `editor/Selection.fs:84-161`; `editor/AssetBrowser.fs:39-40`; existing helper `state/Undo.fs:170-172` | Fix |
| F-38 | State-level command classification is repeated in three dispatchers. | `state/Command.fs:146-157,205-215,260-269` | Fix |
| F-39 | Five `noOp*` wrappers emit one diagnostic format. | `state/CueEngine.fs:123-140` | Fix |
| F-40 | `LoadPool` forwards six `LoadQueue` operations; two have no callers. | `world/assets/AssetLoad.fs:163-169,257-280` | Fix |
| F-41 | Dead or over-exposed exports across clusters. | `core/Lru.fs:41-42`; `core/Input.fs:35`; `core/Gltf.fs:55-58`; `core/Maths.fs:10-17`; `narrative/Facts.fs:68`; `narrative/History.fs:54-57`; `narrative/ParticipantIndex.fs:46`; `editor/Raycast.fs:31-52`; `world/render/EnvironmentRender.fs:30-33`; `world/assets/Assets.fs:687-688`; `world/assets/EnvironmentAsset.fs:87-95`; `audio/Music.fs:162-165`; `audio/Sound.fs:16-17`; `state/SharedState.fs:33` | Fix / document retained seams |
| F-42 | One-line wrappers split ownership of the load queue and asset manager. | `world/assets/AssetLoad.fs:257-280`; `world/assets/Assets.fs:687-688` | Fix |
| F-43 | `EnvironmentRender.release` is a no-op kept alive by an entity call. | `world/render/EnvironmentRender.fs:30-33`; `world/Entity.fs:201` | Verify |
| F-44 | Scenario fork allocates a whole throwaway zone to read fixed capacities. | `state/ScenarioFork.fs:59-64`; `world/Zone.fs:55-65` | Fix |
| F-45 | Picking computes the same screen ray twice. | `editor/Editor.fs:320-327`; `editor/Viewport.fs:219` | Fix |
| F-46 | A public music mutability field is never reassigned. | `state/SharedState.fs:33` | Fix |

### Excessive repetition

| ID | Finding | Evidence | Disposition |
|---|---|---|---|
| F-47 | NUL-terminated buffer read, seed and selectable helpers are copy-pasted. | `bufferText` 7×: `editor/AdventPanel.fs:30-33`, `ScenarioPanel.fs:61-64`, `Outliner.fs:19-22`, `Inspector.fs:20-23`, `CueEditor.fs:33-36`, `CueTree.fs:655-658`, `DialoguePanel.fs:26-29`; seed 3×: `Inspector.fs:14-17`, `DialoguePanel.fs:21-24`, `CueEditor.fs:28-31`; selectable 3× | Fix |
| F-48 | Cue panel and trigger inline editor duplicate the editor pipeline. | `editor/CueEditor.fs:383-398,409-444` vs `:468-481,491-521` | Fix |
| F-49 | Dialogue kind/speaker/expression control is implemented twice. | `editor/DialoguePanel.fs:69-99`; `editor/CueTree.fs:817-844` | Fix |
| F-50 | Waypoint/trigger/actor name combos are three near-identical functions. | `editor/CueEditor.fs:109-176` | Fix |
| F-51 | Inspector repeats the colour-picker scaffold three times and the committed-text scaffold twice. | `editor/Inspector.fs:166-176,193-203,205-215,237-249,279-289`; `editor/CueEditor.fs:69-99` | Fix |
| F-52 | Graphics-pipeline construction is copy-pasted five times; the mesh shader is compiled twice. | `render/Gpu.fs:466-493,590-614,671-695`; `render/DebugDraw.fs:114-140`; `render/Ui.fs:232-256`; `render/Gpu.fs:379,500,504` | Fix |
| F-53 | Shader loading, sampler creation and matrix writing have local duplicates. | `render/DebugDraw.fs:45-55,335-342`; `render/Ui.fs:136-146,148-156`; canonical `render/Gpu.fs:44-54,349-354`; `render/Texture.fs:85-93` | Fix |
| F-54 | glTF float-array decoding is written three times. | `core/Gltf.fs:207-213,240-246,247-253` | Fix |
| F-55 | The feet-to-capsule-centre offset is defined three times. | `world/Zone.fs:216-217`; `world/Systems.fs:174-176`; `state/CueEngine.fs:493` | Fix |
| F-56 | Body deactivate/activate/release repeat the same three scans. | `world/Zone.fs:980-1005,1007-1032,1039-1071` | Fix |
| F-57 | By-name slot resolution is three copies with no early exit. | `world/Zone.fs:568-576,652-660,703-711` | Fix |
| F-58 | Authored entity literals bypass `Entity.baseEntity`; asset bindings are re-spelled. | `world/Zone.fs:768-774,934-940`; `world/Entity.fs:89-98`; bindings at `world/Zone.fs:481,497,727,755,834,971` | Fix |
| F-59 | Load-and-report-on-failure is repeated four times; manifests are passed as six-tuples and every caller destructures them. | `world/assets/Assets.fs:114-126,326-337,781-790,793-799,807-817` | Fix |
| F-60 | The deadline/interpolation formula is hand-copied five times. | `state/CueEngine.fs:100,184-186,258,418,501,558` | Fix |
| F-61 | Waypoint chain resolution is duplicated and indexes a list with `chain.[i]`, making it quadratic. | `state/CueEngine.fs:63-83,448-458`; `world/Zone.fs:665-671` | Fix |
| F-62 | Actor body detach is duplicated between cue engine and scenario fork. | `state/CueEngine.fs:267-272`; `state/ScenarioFork.fs:21-25` | Fix |
| F-63 | Paired projection plumbing repeats across narrative units. | `narrative/Units/Cast.fs:14-21`; `Friendship.fs:11-23`; `Romance.fs:12-39` | Verify (judgment call) |

### Test suite

| ID | Finding | Evidence | Disposition |
|---|---|---|---|
| F-64 | Headless shared-state fixture is copied across ~14 files. | `test/ScenarioTests.fs:37-43`, `ScenarioForkTests.fs:52-62`, `SelectionTests.fs:26-32`, `SkydomeTests.fs:17-23`, `SoundTests.fs:34-40`, `SpawningTests.fs:46-47`, plus eight more | Fix |
| F-65 | Three tests hand-write the gameplay-state record instead of using its constructor. | `test/SelectionTests.fs:34-53`; `test/SkydomeTests.fs:25-44`; `test/SpawningTests.fs:51-70` | Fix |
| F-66 | Entity/actor/prop fixture constructors are redefined 6-8 times. | `test/ScenarioTests.fs:18-30`, `ProjectTests.fs:23-35`, `ScenarioForkTests.fs:67-73`, `SelectionTests.fs:16-22`, `SystemsTests.fs:14-20`, `ZoneTests.fs:27-61`, `SpawningTests.fs:19-25` | Fix |
| F-67 | Headless asset composites and the one-triangle submesh literal are duplicated. | `test/ZoneTests.fs:83-93`; `test/SpawningTests.fs:32-44`; literals also `ZoneTests.fs:1008`, `CueTests.fs:71`, `GameplayStateTests.fs:41,505,740,785` | Fix |
| F-68 | `CueTests` shares one mutable state across every fact. | `test/CueTests.fs:40` with mutations at `:706,716,738,771-772,798,805,821` | Fix |
| F-69 | Cue descriptions are duplicated and already drifted. | `test/CueTests.fs:82-145`; `test/CuePanelTests.fs:40-100` | Fix |
| F-70 | Async load test sleep-polls with a 3s timeout. | `test/AssetLoadTests.fs:113-119` | Fix |
| F-71 | `weightOf` reimplements the production clip-weight accessor; `weightOr` hides `None` behind a sentinel. | `test/ActorTests.fs:709-715`; `test/CueTests.fs:1069-1070` (25 uses) | Fix |
| F-72 | Allocation-measurement boilerplate is repeated 15+ times. | `test/ActorTests.fs:199-210,331-343`; `test/CueTests.fs:408-416,615-629,690-700,777-787,1300-1342,1700-1764`; `test/DialogueTests.fs:232-247,314-334` | Fix |
| F-73 | Temp-dir/manifest setup is repeated; three asset tests are near-identical. | `test/AssetsTests.fs:21-55`; `test/AudioTests.fs:26-31,36-131` | Fix |
| F-74 | A test named "omits its name" never omits it. | `test/AssetNameIndexTests.fs:44-48` | Fix |
| F-75 | Tests assert implementation details (reflection over members, reference identity). | `test/CuePanelTests.fs:750-762,837-844`; `test/ZoneTests.fs:855-858` | Fix |
| F-76 | Narrative harness repeats a subsumed determinism run and 200 identical builds; the rule-set test reimplements the report renderer. | `test/NarrativeHarnessTests.fs:65-69,144-150,248-262`; `test/NarrativeRuleSetTests.fs:15-32` | Fix |
| F-77 | Single-use/dead test helpers and duplicate assertion helpers. | `test/TestSupport.fs:47-52`; `test/SwayTests.fs:55`; `test/RaycastTests.fs:8-9` vs `test/IqmTests.fs:66-67` | Fix |
| F-78 | Spawning tests rebuild the full asset rig when they do not read assets. | `test/SpawningTests.fs:153-158` and seven repeats | Fix |
| F-79 | Narrative mechanism tests copy the production projection definitions. | `test/NarrativeProjectionTests.fs:17-41`; `test/NarrativeDerivationTests.fs:21-64` | Verify (keep isolation, share test-only declarations) |
| F-80 | A "no write happened" test relies on timestamp resolution alone. | `test/ProjectTests.fs:171-174` | Fix |

### Narrative engine

| ID | Finding | Evidence | Disposition |
|---|---|---|---|
| F-81 | Every commit drops inferred facts and recomputes the fixpoint even when no projection matched. | `narrative/Engine.fs:62-66`; `narrative/Derive.fs:89-93` | Fix |
| F-82 | Each conclusion does three map traversals and two canonicalisations. | `narrative/Derive.fs:66-71`; `narrative/Facts.fs:76-89` | Fix |
| F-83 | Rule evaluation allocates arrays/resize lists per conclusion and per bind. | `narrative/Rules.fs:106-140` | Fix |
| F-84 | The bound check materialises every conclusion before counting and traverses the list three times. | `narrative/Derive.fs:49-61` | Fix |
| F-85 | `dropInferred` rebuilds the fact map twice per commit. | `narrative/Facts.fs:114-117` | Fix |
| F-86 | Participant index does a lookup plus add per participant and allocates a list per fact. | `narrative/ParticipantIndex.fs:24-31`; `narrative/Vocabulary.fs:88-96` | Fix |
| F-87 | `reasonsFor` re-canonicalises an already-canonical list. | `narrative/Engine.fs:81-82`; `narrative/Facts.fs:57-58` | Fix |
| F-88 | `factsOf` scans every fact and allocates participant lists; the index built during derivation is discarded. | `narrative/Engine.fs:76-77`; `narrative/Facts.fs:129-131` | Verify |
| F-89 | Harness report re-renders facts during sorting and recomputes history/facts after the run. | `narrative/Harness.fs:33-34,79-93` | Fix |
| F-90 | Dead/forwarding narrative surface and an unreachable canonical branch. | `narrative/Facts.fs:68`; `narrative/History.fs:54-57`; `narrative/Canonical.fs:62-64`; `narrative/Rules.fs:128-131` | Fix |

### Documentation drift

| ID | Finding | Evidence | Disposition |
|---|---|---|---|
| F-91 | Comments and docs state behaviour the code no longer has: camera orbit, scenario undo, retired cue ops, "cold path" cue rows, "zero allocation" draw paths, duplicated zone comment blocks. | `render/Camera.fs:38-48`; `docs/EDITOR_DESIGN.md:183-184`; `world/Cue.fs:7-9`; `editor/CueTree.fs:155-179`; `world/render/EnvironmentBatch.fs:9-10`; `world/render/DialogueRender.fs:45-46`; `world/render/PromptRender.fs:57-59`; `world/Zone.fs:608-615,624-633` | Fix |

### Verify or exclude (do not change without new evidence)

| ID | Item | Evidence | Disposition |
|---|---|---|---|
| V-01 | JIT may already elide large-struct copies at call boundaries. | reviewer uncertainty | Exclude unless measured |
| V-02 | `bool` P/Invoke marshalling is technically ABI-mismatched; works on x86-64. | `render/ImGui.fs` externs | Exclude unless reproduced |
| V-03 | The codebase uses both immediate and deferred GPU release policies. | `render/Gpu.fs:770-775` vs `render/InstanceBuffer.fs:21-23` | Verify-only (no change in 098) |
| V-04 | `Mesh.create` uses two one-shot uploads deliberately. | `render/Mesh.fs:68-71`; `docs/RENDER_PERFORMANCE.md §13` | Exclude |
| V-05 | Textures request colour-target usage that nothing uses. | `render/Texture.fs:165` | Verify (harmless) |
| V-06 | `Zone.tryEntityAt` allocates `Some` on editor-only paths. | `world/Zone.fs`; `editor/Editor.fs:46,63` | Exclude unless hot |
| V-07 | `GameplayState.activeZone` takes a struct by value several times per frame. | `state/GameplayState.fs:91`; `screen/GameplayScreen.fs:49,57,62,91` | Exclude unless measured |
| V-08 | `Sound.play` relies on the mixer rewinding a reused voice. | `audio/Sound.fs:39-44`; `audio/Music.fs:125` | Verify-only (follow-up if broken) |
| V-09 | Struct narrative premises/reasons may not repay their complexity. | `narrative/Facts.fs:15-17,29-32` | Exclude |
| V-10 | `Scenario.Name` is unread by the harness but part of the contract. | `narrative/Harness.fs:15-16` | Exclude |
| V-11 | Rule builder `Return`/`YieldFrom`/`Combine` are unused but complete the builder surface. | `narrative/Rules.fs:128-131` | Verify intent |
| V-12 | Environment-batch handle reuse aliasing is a risk not yet observed. | `world/render/EnvironmentBatch.fs:52-57` | Fix the reset (F-05); alias itself need not be reproduced |
