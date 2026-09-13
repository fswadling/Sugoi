# Research: Actor Draw Allocation

Phase 0 decisions. All unknowns in the spec are resolved here; no
`NEEDS CLARIFICATION` remain.

## D1 — Store the binding array on the renderer instance

**Decision**: Add `BoneStorageBuffers : nativeint array` to `ActorRender`, holding
`[| BoneBuffer |]`, created once in `ActorRender.create` and passed straight to
`BindGPUVertexStorageBuffers` in `draw`.

**Rationale**: It matches the established pattern in this codebase — `Mesh` owns
`VertexBinding`, `StaticMesh` owns `VertexBinding`, `DebugDraw` owns `Bindings` —
each created once and reused per frame. Consistency makes the allocation-free draw
style obvious.

**Alternatives considered**:

- *A single shared mutable array* (one module-level array whose element is set to
  the current actor's buffer before each bind) — works because the bind is recorded
  immediately, but risks aliasing bugs if the call order ever changes, and gives up
  the per-instance ownership the other renderers use. Rejected for one small array
  per actor.
- *A stack-allocated span* (`stackalloc`/`MemoryMarshal`) — avoids the field but is
  more code and diverges from the existing pattern for no measurable gain.

## D2 — Field type is `nativeint array`

**Decision**: The field is `nativeint array`, not a named SDL buffer type.

**Rationale**: The SDL3-CS binding exposes
`BindGPUVertexStorageBuffers(renderPass, firstSlot, storageBuffers: nativeint array,
numBindings)` (or `ReadOnlySpan<nativeint>`); there is no `SDL.GPUBuffer` type. A
`nativeint array` binds directly with no conversion.

## D3 — Create the array alongside the bone buffer

**Decision**: Build `[| boneBuffer |]` in `create`, where `BoneBuffer` is already
allocated.

**Rationale**: That is the registration point for per-instance GPU resources; the
array is the binding view of `BoneBuffer` and belongs with it.

## D4 — `Mesh.draw` needs no change

**Decision**: Leave `render/Mesh.fs` as-is.

**Rationale**: Inspection shows `Mesh.draw` already mutates a preallocated
`VertexBinding` array; it allocates nothing per frame.

## D5 — Verification: inspection + build/tests + a runtime look

**Decision**: Verify FR-001/FR-005 by inspecting the actor path for per-frame
array/list allocation; verify FR-002/FR-003 by build + the existing suite and a
runtime confirmation that an animated actor renders identically.

**Rationale**: `draw` receives an opaque render pass and command buffer, so the GPU
bind can't be exercised headlessly; the allocation claim is a code fact, and the
rendering claim is visual.

**Alternatives considered**: a reflection/IL-based "no allocation" unit test —
disproportionate for one field, and brittle against inlining.
