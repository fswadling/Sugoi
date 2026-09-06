# Research: Actor Capsule Collider

## 1. How `odin-game` models the actor capsule

- **Decision**: Match `odin-game`'s actor capsule exactly.
- **Rationale**: `odin-game` is the reference implementation this project mirrors. Its capsule is a fixed-width, full-height capsule whose feet sit at the entity position.
- **Alternatives considered**: Deriving the capsule from the actor mesh bounds (the previous behavior), or from the skeleton. Rejected because bounds are arm-span-dominated and skeleton-derived capsules add complexity without matching the reference.

Key reference facts:

- `DEFAULT_ACTOR_COLLIDER_RADIUS = 0.35` (fixed).
- The actor entity position is the **feet** (ground contact).
- The capsule spans `[position.y + radius, position.y + height - radius]` using the actor's full oriented height.

## 2. Collider derivation in `fsharp-game`

- **Decision**: Derive `height` from the IQM Z extent (`bounds.Max.Z - bounds.Min.Z`) and use the fixed radius; store `{ Radius; HalfHeight }` where `HalfHeight = height / 2 - radius`.
- **Rationale**: IQM is Z-up, so the vertical extent is Z. Keeping the existing `CapsuleCollider` shape (Jolt's `CapsuleShape(halfHeight, radius)` convention) minimises the diff.
- **Alternatives considered**: Storing full `Height` instead of cylinder half-height; rejected because `CapsuleShape` and the debug draw both already speak half-height.

## 3. Capsule placement

- **Decision**: Add `capsuleCenter` that offsets the entity position up by `HalfHeight + Radius` (so the capsule base sits at the feet), and use it in both the physics body and the debug draw.
- **Rationale**: Jolt's `CapsuleShape` is centred on the body position, but the entity position is the feet; the offset is the distance from feet to capsule centre. Sharing one helper keeps physics and visualization in agreement.
- **Alternatives considered**: Passing the feet position to Jolt with an explicit offset at the call site only; rejected because the debug draw then risks drifting from the body.

## 4. Capsule visualization

- **Decision**: Add a per-actor `ShowCollider : bool` field, expose it as an inspector checkbox, and draw the capsule as a line wireframe through `DebugDraw`.
- **Rationale**: The inspector is the single editing surface for the selection; a `DebugDraw` primitive reuses the existing line accumulator and is depth-tested against the scene like the other editor markers.
- **Alternatives considered**: A global "show all colliders" toggle (like the environment prop colliders in `odin-game`); rejected for the first cut because the request is specifically per-actor.

## 5. ImGui checkbox binding

- **Decision**: Add a minimal `igCheckbox` P/Invoke to the existing custom `ImGui` wrapper.
- **Rationale**: The wrapper is intentionally small and grown as widgets are needed; this is the first boolean-toggle widget the editor requires.
- **Alternatives considered**: A custom native `cimgui_*` helper; rejected because `igCheckbox` is already exported by cimgui and the P/Invoke is one line.
