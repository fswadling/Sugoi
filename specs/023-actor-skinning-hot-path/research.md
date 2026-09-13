# Research: Actor Skinning Hot Path

Phase 0 decisions. All unknowns in the spec are resolved here; no
`NEEDS CLARIFICATION` remain.

## D1 — `sampleJoint` returns a struct tuple

**Decision**: Change `sampleJoint`'s return from `(Vector3 * Quaternion * Vector3)`
to `struct (Vector3 * Quaternion * Vector3)`, and destructure accordingly in
`samplePose` (`let struct (t0, r0, s0) = …`).

**Rationale**: Measured with `--optimize-`, an F# reference tuple of value types
allocates **56 B/call**; a struct tuple allocates 0. The sampler calls `sampleJoint`
twice per joint, so Akane (166 joints) allocates ~332 tuples ≈ 18.6 KB per frame per
actor in a Debug build. Debug is exactly the configuration where the F# compiler
does *not* fold non-escaping reference tuples to value tuples. AGENTS.md already
prescribes struct tuples for hot-path data.

**Alternatives considered**: out-params (`byref`) — more invasive and less readable;
returning a single `Matrix4x4` — can't carry all three components.

## D2 — `localMatrix` writes the translation row directly

**Decision**: Build `scale * rotation` and then set `M41..M43` to the translation,
instead of composing with `Matrix4x4.CreateTranslation`:

```fsharp
let private localMatrix (t: Vector3) (r: Quaternion) (s: Vector3) : Matrix4x4 =
    let mutable m = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r)
    m.M41 <- t.X
    m.M42 <- t.Y
    m.M43 <- t.Z
    m
```

**Rationale**: System.Numerics is row-vector (`v * M`), so the transform is
`S * R * T`. For an affine `M = S * R` (translation zero, `M44 = 1`), composing `M*T`
yields `M41 + M44·tx = tx`, etc. — i.e. writing the translation into the last row is
exactly equivalent. This removes one of the two multiplies in `localMatrix`
(166 multiplies/frame for Akane), the same reduction that dominates the Debug cost.

**Alternatives considered**: building the matrix from components by hand — more code
and easy to get the row/column convention wrong; leaving it — this is the cost.

## D3 — Leave the channel-sampling `match` alone

**Decision**: Keep `sampleJoint`'s 10-way `match` and mask loop unchanged.

**Rationale**: It runs ~3 300 iterations/frame (2 × 166 joints × 10) — a small share
of the cost — and unrolling it into ten repeated blocks would bloat a short function
for marginal gain, against the project's minimalism preference. Revisit only if a
post-change measurement shows the loop dominating.

**Alternatives considered**: unrolled mask checks; precomputed active-channel
tables — both add code/state for little measured value.

## D4 — Golden tests over a model with active channels

**Decision**: Extend `test/ActorTests.fs`'s model builder with frame channels and
channel masks, and add tests that assert the sampled `Local`/`World`/`Skinning`
matrices for a joint with active channels.

**Rationale**: The existing tests use `ChannelMask = 0`, so they do not exercise the
sampling math the refactor touches. `samplePose` is pure CPU math (`ActorTests`
already builds synthetic `Iqm.Model` records and calls it with no GPU), so exact
matrix assertions are the right guard for FR-003.

**Alternatives considered**: snapshotting Akane's output — the asset isn't needed to
prove the refactor; a synthetic multi-joint model is deterministic and fast.

## D5 — Keep `Pose.Local` written

**Decision**: Continue to write `pose.Local.[j]`.

**Rationale**: It is part of the pose's documented shape and is read by the existing
tests; it costs one array store per joint and removing it would be scope creep.
