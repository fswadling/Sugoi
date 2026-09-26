# Feature Specification: Actor Morph Targets

**Feature Branch**: `037-actor-morphs`

**Created**: 2026-09-26

**Status**: Draft

**Input**: User description: "let's look into implementing morph animations — you can use odin-game as a template on how to do this". Two decisions settled in the same conversation: the morph data's manifest is **s-expression syntax** (Prime), not JSON; and an actor holds at most **8** morphs.

**Origin**: `odin-game` is the template. Its `Actor_Renderer` holds `morph_slots : Slot_Map(Actor_Morph, MAX_ACTOR_MORPHS)` beside the animation slots, its asset caches morph deltas parsed from a `morphs.json` manifest and a `morphs.bin`, and `apply_actor_morphs` resets every mesh to its base vertices and then adds `delta * weight` for each enabled morph, *before* the bones are posed. `specs/009-animated-character` deferred exactly this ("`morphs.json`/`morphs.bin` parsing and application are out of scope this phase", research Decision 10), and `docs/PROJECT_STRUCTURE.md` already describes `world/Actor.fs` as "IQM model + morphs". `specs/036-actor-animation-slots` built the per-actor slot machinery and the Inspector section this mirrors.

One thing odin does not have to solve and this port does: odin morphs the CPU vertex array and lets raylib re-upload and skin from it. This port skins on the **GPU** — the actor's vertex buffer is uploaded once and the bone palette is a storage buffer — so there is no CPU vertex array to edit. The port's equivalent of "reset the base, then add `delta * weight`" is a displacement of the vertex before the bone blend, and this specification is written to that stage.

## User Scenarios & Testing *(mandatory)*

Two parties use this, as with the animation slots. The **system driving an actor** — or an author in the editor — chooses a weight for each of an actor's morphs, and the **player** sees the result: an actor whose shape is its base mesh plus whatever its morphs add at those weights. The **asset author** is the third party: the morph data itself is authored data, and its format is part of what this feature settles.

### User Story 1 - An actor's shape responds to its morph weights (Priority: P1)

An actor asset may carry named morphs — each a set of per-vertex offsets over part of the mesh — and an actor weighing one renders its base shape plus that morph's offsets scaled by the weight, with the morphed shape then posed by its animation exactly as before. A morph at weight 0, or an actor with no morph data at all, renders exactly as it does today.

**Why this priority**: this is the capability. The weights mean nothing unless the geometry moves, and the loader that reads the offsets is inseparable from the displacement that applies them. It also stands alone: an actor without morph data is provably unchanged.

**Independent Test**: place an actor whose asset carries a morph with known offsets; set that morph's weight to 1 and confirm the morphed region is displaced by exactly the offsets, to 0 and confirm the base shape returns, and to some fraction and confirm the displacement scales; then confirm an actor whose asset carries no morph data is identical to before the feature.

**Acceptance Scenarios**:

1. **Given** an actor asset carrying a morph, **When** an actor weighs that morph at 1, **Then** the morph's target vertices render displaced by the morph's offsets.
2. **Given** the same actor, **When** the weight is 0 (or the morph is not held), **Then** it renders its base shape.
3. **Given** a weight between 0 and 1, **Then** the displacement is that fraction of the offsets.
4. **Given** an asset carrying no morph data, **Then** the actor renders exactly as it did before the feature, and the existing animation and skinning behaviour is unchanged.
5. **Given** two actors sharing one asset, **When** one weighs a morph, **Then** only that actor's shape changes; the shared morph data is not disturbed.

---

### User Story 2 - A caller sets each morph's weight (Priority: P2)

The routine driving an actor sets a morph's weight at any time, naming the morph; a morph the actor does not yet hold joins its set. Each morph is independent, so an actor can carry several at once, up to the limit.

**Why this priority**: it is the handle on the capability. It follows Story 1 for the same reason the animation weights followed the blend: a weight with nothing to displace is a number with no effect.

**Independent Test**: on an actor, set a morph's weight and confirm the shape changes; set it to 0 and confirm the contribution is gone but the morph stays held; set a weight above 1 or below 0 and confirm it is clamped; name a morph the asset does not define and confirm a loud failure; hold eight and confirm a ninth fails loudly.

**Acceptance Scenarios**:

1. **Given** an actor holding no morphs, **When** a caller sets a morph's weight, **Then** the morph joins the actor's set and its displacement appears.
2. **Given** a morph at some weight, **When** a caller sets it to 0, **Then** it stops displacing while remaining held.
3. **Given** a requested weight outside 0 to 1, **Then** it is clamped into range.
4. **Given** a morph name the actor's asset does not define, **Then** the request fails loudly.
5. **Given** an actor already holding eight morphs, **When** a ninth is requested, **Then** it fails loudly rather than being dropped.
6. **Given** a morph already held, **When** its weight is set again, **Then** the existing morph is adjusted rather than a second copy added.

---

### User Story 3 - The author sets an actor's morphs in the Inspector (Priority: P3)

With an actor selected, the Inspector shows a **Morphs** section: the morphs the actor is currently weighing, each with its weight, a control to bring another of its asset's morphs into the set, and a control to take one out. The author adds a morph, drags its weight, watches the actor's shape change, and removes it.

**Why this priority**: it is the surface that makes the weights reachable by hand, and it is the same surface Story 2 drives from code. It ranks below Stories 1 and 2 because the feature is complete without it, exactly as the animation section ranked below the animation blend.

**Independent Test**: select an actor, add a morph from those its asset carries, set its weight and confirm the shape changes, remove it and confirm the shape returns; confirm a non-actor shows no Morphs section, and the degenerate cases report rather than fail silently.

**Acceptance Scenarios**:

1. **Given** an actor is selected, **When** the Inspector draws, **Then** a Morphs section lists every morph the actor holds, each with its weight.
2. **Given** the actor's asset carries morphs the actor does not hold, **When** the author adds one, **Then** it joins the set and appears in the list.
3. **Given** a morph in the set, **When** the author sets its weight, **Then** the actor's shape reflects it; **When** the author removes it, **Then** the shape returns to what the remaining morphs give.
4. **Given** a selection that is not an actor, **Then** the section is not shown.
5. **Given** the actor's asset carries no morphs, or the actor already holds every one, or the actor holds the maximum, **Then** the section reports the condition rather than offering an action that cannot succeed.
6. **Given** the author adds, removes or re-weights a morph, **Then** no undo step is recorded and nothing authored about the actor changes.

---

### Edge Cases

- **The limit is eight morphs, and exceeding it is a bug to report.** Eight is the limit settled for this feature and it is mirrored wherever the shader is bounded; asking for a ninth fails loudly rather than silently dropping one. An asset that declares more than eight is a broken asset and fails at load, since the vertex stage can weigh at most eight of them.
- **Morphs are additive, not a weighted average.** Unlike rotation, offsets add, so weights that sum above 1 are meaningful (an exaggerated expression) and the result does not depend on the order morphs are held in.
- **A morph with no offsets is not offered.** A manifest entry with no usable targets describes nothing to displace, so it is treated as absent rather than held at a weight that does nothing.
- **A malformed sidecar is a broken asset, not a state to render around.** A manifest that does not parse, a binary whose size does not match, or a target range outside the mesh's vertices fails loudly rather than being skipped.
- **Positions are required, normals are optional.** A morph that displaces positions without supplying normal offsets is allowed; the geometry is right and its lighting is not corrected for the new shape.
- **An actor with morph data and every weight at 0 renders the base shape**, and so does an actor whose asset carries none — the two are indistinguishable on screen, and both must be what the actor looked like before the feature.
- **The morphs are the asset's, the weights are the actor's.** Two actors drawn from one asset share the offsets and keep independent weights; one actor's weight never moves another's shape.
- **A morph and an animation can both be in play.** The morphed shape is posed by the animation, exactly as the base shape was.
- **Morphs are actors only.** An environment or a prop has no morphs; nothing in this feature reaches them.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An actor asset MAY carry morph data: named morphs, each a set of per-vertex position offsets (and optional per-vertex normal offsets) over one or more contiguous ranges of the actor's vertices.
- **FR-002**: The morph data MUST live in two files — a manifest in s-expression syntax describing each morph and its ranges and where its offsets sit in the data file, and a binary file holding the offsets as 32-bit floats — and MUST be discovered from the actor's own manifest so it does not collide with the actor-manifest scan.
- **FR-003**: The morph data MUST be read and validated once per asset (at load, not per frame), and reused by every actor drawn from that asset. A manifest that does not parse, a data file whose size does not match the manifest, or a range outside the mesh MUST fail loudly.
- **FR-004**: An actor MUST weigh each morph it holds, and its rendered shape MUST be its base shape plus, for each held morph, the morph's offsets multiplied by the morph's weight — applied to positions, and to normals where the morph supplies them, before the animation poses the actor.
- **FR-005**: A requested weight MUST be clamped to the range 0 to 1 inclusive, where 0 contributes nothing and 1 applies the offsets in full. Contributions MUST be additive and independent of the order morphs are held in.
- **FR-006**: An actor MUST hold at most eight morphs; requesting a ninth MUST fail loudly rather than silently dropping one. An asset that declares more than eight morphs MUST fail at load, because the vertex stage can weigh at most eight.
- **FR-007**: A caller MUST be able to set a morph's weight by name at runtime, including for a morph the actor does not yet hold, in the same frame; setting it again for a morph already held MUST adjust that morph rather than add a second copy, and a name the asset does not define MUST fail loudly.
- **FR-008**: An actor that holds no morphs, whose weights are all 0, or whose asset carries no morph data MUST render exactly as it did before this feature, and the existing animation, skinning and pose behaviour MUST be unchanged.
- **FR-009**: The Inspector MUST show a Morphs section when an actor is selected, listing every morph the actor holds with its weight and offering the add and remove controls, and MUST show no such section for any other kind of selection.
- **FR-010**: The section MUST report, rather than silently offer an impossible action, each degenerate case: the asset carries no morphs, the actor holds every morph the asset carries, and the actor holds the maximum.
- **FR-011**: Morph edits from the Inspector MUST be runtime state: they MUST NOT be persisted, MUST NOT record an undo step, and MUST NOT be carried by an actor's captured authored state. Rebuilding an actor's renderer MUST yield the default (no morphs held) state.
- **FR-012**: Applying morphs MUST NOT introduce per-frame allocations, and the morph data MUST be uploaded once per asset rather than re-sent each frame.

### Key Entities *(include if feature involves data)*

- **Morph**: a named set of offsets held by an actor asset — the thing a weight is set on.
- **Morph target**: one contiguous range of the asset's vertices, carrying the position offsets (and optional normal offsets) for that range.
- **Actor's morph slots**: the bounded set (up to eight) of morphs an actor is currently weighing, and the weight of each. The asset owns the offsets; the actor owns this.
- **Base shape**: the actor's unmorphed vertices — what a weight of 0 leaves, and what every morph is measured against.
- **Morph sidecar**: the manifest and data file pair that describes an asset's morphs; authored data, not part of the actor.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A morph at weight 1 displaces its target vertices by its offsets in full, at weight 0 by none, and at a weight *w* by exactly *w* times the offsets (100% of targets).
- **SC-002**: An actor with no morph data, and an actor whose morph weights are all 0, render identically to the pre-feature actor, and every existing animation, skinning and pose assertion passes unmodified (100%).
- **SC-003**: An actor holds and applies up to eight morphs; a request beyond that is reported as a failure rather than silently ignored (100% of over-limit requests reported).
- **SC-004**: A malformed sidecar — unparsable manifest, mismatched data size, out-of-range target — is reported as a load failure rather than rendered around (100%).
- **SC-005**: Morph data is read and uploaded once per asset; applying weights adds no measurable per-frame memory growth, and the existing no-allocation animations checks still observe zero bytes per frame (0 bytes/frame).
- **SC-006**: With an actor selected, an author can add a morph, set its weight and remove it, and the actor's shape reflects each step (100% of the three actions).
- **SC-007**: Inspector morph edits record no undo step and persist nothing: after changing morphs, Ctrl+Z reverses the previous scene edit and the actor's saved state is unchanged (0 steps, 0 persisted changes).

## Assumptions

- **The manifest is s-expression syntax, not JSON.** Settled in conversation. It keeps the morph manifest in the format every other authored manifest uses, parsed with the same machinery, and adds no dependency. It is a deliberate departure from `specs/009`'s tentative `morphs.json`.
- **The manifest must not use the `*.manifest` extension.** The actor scan enumerates `*.manifest` recursively under `assets/actors`, so a morph manifest named that way would be read as an actor. The morph manifest is referenced by path from the actor's manifest and uses a distinct extension, which is where the discovery rule in FR-002 comes from.
- **Eight morphs.** Settled in conversation. It is the bound the vertex stage must respect — the weights array's length and the loop's ceiling — mirrored the way the bone limit already is, and it is generous for the expressions this game expects (a handful of face shapes). It bounds the **asset** as well as the actor: a manifest declaring more than eight morphs cannot be weighed by the vertex stage, so it is a broken asset and fails at load rather than loading a set the shader cannot address.
- **Morphs are applied before skinning, in the vertex stage.** odin resets base vertices and adds `delta * weight` on the CPU because raylib skins on the CPU; this port skins on the GPU, so the equivalent obligation is a displacement of the vertex before the bone blend. The **how** — a storage buffer of offsets read by the shader, or a re-uploaded vertex buffer — is a design decision for the plan; this spec fixes only that the displacement happens before posing.
- **A morph's offsets are measured against the base shape, and are additive.** This is the reference engine's model: reset, then add. It is also why the result is order-independent, unlike the rotation blend the animation slots use.
- **Normal offsets are optional; position offsets are required.** A morph without normal offsets is a legal, slightly under-lit morph rather than a broken one.
- **Morphs are actor-only, and there is no morph clock.** A morph's weight is set, not animated; a future feature that drives weights over time is separate.
- **Morph weights are not persisted.** The reference engine copies them into a scenario; this port persists no animation or morph state, and the Inspector's edits are runtime state exactly as the animation section's are.
- **No exporter exists yet, so the feature must ship a way to author a morph.** The port's actor meshes are IQM, which has no shape keys, and its glTF loader serves environments and props; the sidecar is therefore authored beside the mesh, and at least one asset carrying morphs (or a small generator that writes one) is part of landing this.
- **The existing render path is the foundation.** The vertex layout, the bone palette, the pipelines and the animation slots are unchanged; this feature adds a displacement to the vertex stage and the data that drives it.
