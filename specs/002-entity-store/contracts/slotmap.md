# Contract: SlotMap + Entity

The public surface `core/SlotMap.fs` and `core/Entity.fs` expose. Signatures are
illustrative F#, not the implementation.

## SlotMap (generic)

```fsharp
// A slot map stores payloads of type 'T (value types only) in a fixed-size
// inline array. The container and its slots are immutable structs; updates use
// copy-and-update (`with`), so no byref mutation is needed. Indices are uint32,
// with UInt32.MaxValue as the "no slot" sentinel.
type Handle = { Index : uint32; Generation : uint32 }

// Allocate the slot array and free list.
create : capacity:uint -> SlotMap<'T when 'T : struct>

// Returns the updated map and a handle to the new payload, reusing a freed
// slot if available. Raises when full.
add : SlotMap<'T when 'T : struct> -> 'T -> SlotMap<'T when 'T : struct> * Handle

// Returns the payload for a live handle, or raises on stale/invalid handles.
get : SlotMap<'T when 'T : struct> -> Handle -> 'T

// Returns the updated map with the slot freed, its generation bumped, and the
// slot returned to the free list. Raises on stale/invalid handles.
free : SlotMap<'T when 'T : struct> -> Handle -> SlotMap<'T when 'T : struct>

// Number of live slots.
count : SlotMap<'T when 'T : struct> -> int
```

## Entity model

```fsharp
type EntityKind = Actor | Environment | Prop | Waypoint | Trigger

type EntityRef = { Kind : EntityKind; Index : int }   // + NO_ENTITY sentinel

type Entity =
    { Name : string
      Transform : Transform
      Enabled : bool
      Visible : bool
      Selectable : bool
      Draggable : bool }

// Actor / Environment / Prop embed a nested `Entity` base; Waypoint / Trigger
// are standalone.
```

## Rules

- `add`/`get`/`free` are O(1) and do not allocate per call.
- Out-of-capacity `add` and invalid/stale `get`/`free` raise (fail loudly).
- Iteration is a direct scan over `Slots`; no iterator abstraction.
- `NO_ENTITY` is distinguishable from every valid `EntityRef`.
