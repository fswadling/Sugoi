# Data Model: Entity Store

## Slot map (generic)

The generational slot map is generic over the stored payload type, which is
always a value type (`'T : struct`): a slot map of reference types would store
pointers, not contiguous data, defeating the locality goal.

- `Slot<'T>` — one storage cell:
  - `Value : 'T` — the stored payload.
  - `Generation : uint32` — bumped on free so old handles go stale.
  - `Alive : bool` — whether the slot is occupied.
  - `NextFree : uint32` — free-list link (valid when `Alive = false`).
- `SlotMap<'T when 'T : struct>` — the container (`[<Struct>]`, immutable; updated
  via copy-and-update `with`):
  - `Slots : Slot<'T> array` — fixed size, inline. The array is shared by
    reference across copies; only the small header (`Count`/`FreeHead`) copies,
    so per-call updates do not allocate.
  - `Count : int` — number of live slots.
  - `FreeHead : uint32` — head of the free list (`UInt32.MaxValue` when full).
- `Handle` — an O(1) lookup key:
  - `Index : uint32` — slot index.
  - `Generation : uint32` — the generation the handle was issued with.

Operations (pure; mutating ones return a new container):
`create : uint -> SlotMap<'T>` (allocates the slot array + free list),
`add : 'T -> SlotMap<'T> * Handle` (pops the free list or raises when full),
`get : Handle -> 'T` (validates index + generation, else raises),
`free : Handle -> SlotMap<'T>` (validates, marks dead, bumps generation, pushes
the free list). Scan loops iterate `Slots` directly, skipping non-`Alive` slots.

## Entity model

- `EntityKind` — discriminates the entity category:
  `Actor | Environment | Prop | Waypoint | Trigger`.
- `EntityRef` — a cross-kind reference (plus a `NO_ENTITY` sentinel):
  - `Kind : EntityKind` — which collection holds the entity.
  - `Index : int` — slot index within that collection.
- `Transform` — position/orientation/scale (via `System.Numerics`):
  - `Position : Vector3`
  - `Rotation : Quaternion`
  - `Scale : Vector3`
- `Entity` — the shared base:
  - `Name : string`
  - `Transform : Transform`
  - `Enabled : bool`, `Visible : bool`, `Selectable : bool`, `Draggable : bool`
- Specialised entities (compose the base via a nested field, no inheritance):
  - `Actor = { Base : Entity; … }`
  - `Environment = { Base : Entity; … }`
  - `Prop = { Base : Entity; … }`
  - `Waypoint` / `Trigger` — standalone (no `Entity` base).

## Relationships

- A `SlotMap<'T>` stores one payload type per collection; a `Zone` later owns one
  map per entity kind (phase 4).
- `EntityRef` selects a collection by `Kind` and an entity by `Index`; the
  generic `Handle` carries the `Generation` used for stale detection.
- `Entity` is the shared base embedded by `Actor`/`Environment`/`Prop`;
  `Waypoint`/`Trigger` are standalone.
