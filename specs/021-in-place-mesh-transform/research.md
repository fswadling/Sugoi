# Research: In-Place Mesh Transform

Phase 0 decisions for the fix. All unknowns in the spec are resolved here; no
`NEEDS CLARIFICATION` remain.

## D1 — Trigger: compare the stored scale with the incoming scale

**Decision**: In `Zone.setMeshTransform`, take the in-place path when
`p.Base.Transform.Scale = transform.Scale`; otherwise rebuild.

**Rationale**: The body was built from the entity's stored transform, so the stored
scale *is* the scale baked into the current shape. Comparing it with the incoming
scale is exactly the "does the shape need rebuilding?" test, and it needs no extra
state. The comparison is a heuristic in the safe direction: if a caller ever passes a
scale differing only by float rounding, the worst case is a needless rebuild — never
a shape that disagrees with the geometry.

**Alternatives considered**:

- *Always rebuild* — the current behaviour; this is the bug.
- *Store a "scale the shape was built with" field on `PhysicsAttachment`* — extra
  state for no benefit when the stored transform already carries it.
- *Compare rebuilt vertices* — strictly more work than the thing being avoided.

## D2 — In-place update uses the existing `Physics.setBodyTransform`

**Decision**: Call `Physics.setBodyTransform world ph.Body transform.Position
transform.Rotation` (`physics/Physics.fs`), i.e. Jolt `SetPositionAndRotation`.

**Rationale**: The function already exists and is exactly the operation needed; the
body keeps its shape/BVH and only its rigid transform moves. Mesh bodies are
`MotionType.Static`, so the `Activation.DontActivate` flag is irrelevant.

**Alternatives considered**: add a new physics function — unnecessary duplication;
mutate the body's shape — impossible for a scaled mesh shape and slower than a
transform write.

## D3 — Scope is mesh-backed entities only; actors keep their path

**Decision**: Change only the mesh path (placed props and environment parts).
`setTransform`'s actor branch is untouched.

**Rationale**: Capsules are scale-independent, so they are cheap to rebuild, and
actor bodies are `Dynamic`, where the activation semantics of `DontActivate` need
separate thought. Including them would widen scope for negligible gain.

**Alternatives considered**: convert the actor branch too — deferred, not required
by the spec.

## D4 — Keep the body→entity mapping call uniform

**Decision**: Leave the single `mapBody zone attachment ref` call after the match;
in the in-place case it re-sets `BodyToEntity.[same body] <- same ref`.

**Rationale**: The write is idempotent and off the gameplay hot path (editor drag
only). Keeping one call site is simpler than branching it; `destroyBody` already
removes the stale mapping in the rebuild case.

**Alternatives considered**: call `mapBody` only in the rebuild branch — saves one
dictionary write per drag frame at the cost of a less uniform structure.

## D5 — Testing: headless, via the existing `ZoneTests` fixture

**Decision**: Add tests to `test/ZoneTests.fs` using a fake prop asset in the
headless `AssetManager` plus the real `Physics.create ()` world. Assert body
identity is preserved across a move/rotate and changes across a scale change.

**Rationale**: The mesh body path needs a collider, which needs an asset binding,
but *not* a GPU device — the existing `headlessAssets` fixture already proves this
pattern, and `ZoneTests` already reads `…Props.Get … .Physics.Value.Body` and
`zone.BodyToEntity`. The remaining checks (smooth drag, collider fidelity in the
running editor) need a GPU and are covered by the quickstart.

**Alternatives considered**: extract the scale comparison into a pure helper to
unit-test it in isolation — tests a one-line comparison while missing the body
behaviour that actually matters.
