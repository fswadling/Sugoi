module SlotMap

// Generational slot map: fixed-capacity, contiguous, inline storage of
// value-type payloads (`'T : struct`) with O(1) add/get/free and stale-handle
// detection via a per-slot generation. Slots and the container are immutable
// records (updates use copy-and-update), except `Value`, which is mutable so
// hot-path systems can advance it in place through a byref. Indices are
// `uint32`, with `UInt32.MaxValue` as the "no slot" sentinel.

open System

[<Struct>]
type Slot<'T> =
    { mutable Value : 'T
      Generation : uint32
      Alive : bool
      NextFree : uint32 }

[<Struct>]
type Handle =
    { Index : uint32
      Generation : uint32 }

[<Struct>]
type SlotMap<'T when 'T : struct> =
    { Slots : Slot<'T> array
      Count : uint32
      FreeHead : uint32 }

// "No slot" sentinel for free-list links and FreeHead.
let private noneIndex = UInt32.MaxValue

let create<'T when 'T : struct> (capacity: uint) : SlotMap<'T> =
    let n = int capacity

    let slots = Array.init n (fun _ ->
        { Value = Unchecked.defaultof<'T>
          Generation = 0u
          Alive = false
          NextFree = noneIndex })

    // link the free list: 0 -> 1 -> ... -> (n - 1) -> noneIndex
    for i in 0 .. n - 2 do
        slots.[i] <- { slots.[i] with NextFree = uint32 (i + 1) }

    { Slots = slots
      Count = 0u
      FreeHead = if n > 0 then 0u else noneIndex }

let add (map: SlotMap<'T>) (value: 'T) : SlotMap<'T> * Handle =
    if map.FreeHead = noneIndex then
        invalidOp "slot map is full"

    let index = int map.FreeHead
    let slot = map.Slots.[index]
    map.Slots.[index] <- { slot with Value = value; Alive = true; NextFree = noneIndex }
    { map with Count = map.Count + 1u; FreeHead = slot.NextFree },
    { Index = map.FreeHead; Generation = slot.Generation }

let get (map: SlotMap<'T>) (handle: Handle) : 'T =
    if handle.Index >= uint32 map.Slots.Length then
        invalidOp "slot map handle is out of range"

    let slot = map.Slots.[int handle.Index]
    if not slot.Alive || slot.Generation <> handle.Generation then
        invalidOp "slot map handle is stale"

    slot.Value

let free (map: SlotMap<'T>) (handle: Handle) : SlotMap<'T> =
    if handle.Index >= uint32 map.Slots.Length then
        invalidOp "slot map handle is out of range"

    let index = int handle.Index
    let slot = map.Slots.[index]
    if not slot.Alive || slot.Generation <> handle.Generation then
        invalidOp "slot map handle is stale"

    // bump the generation so old handles go stale, then push onto the free list
    map.Slots.[index] <- { slot with Generation = slot.Generation + 1u; Alive = false; NextFree = map.FreeHead }
    { map with Count = map.Count - 1u; FreeHead = handle.Index }

let count (map: SlotMap<'T>) : uint32 =
    map.Count

// Iterate the alive slots, applying `f` to each (index, value). A plain loop —
// no per-slot allocation. `[<InlineIfLambda>]` lets the compiler inline a
// statically-known lambda at the call site (no closure allocation or indirect
// call); it falls back to a normal closure when the lambda can't be inlined.
let inline iteriAlive (map: SlotMap<'T>) ([<InlineIfLambda>] f: uint32 -> 'T -> unit) : unit =
    let slots = map.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = slots.[i]
        if slot.Alive then f (uint32 i) slot.Value

// Fold over the alive slots, threading an accumulator. Mirrors `iteriAlive`:
// `[<InlineIfLambda>]` inlines a statically-known lambda (no closure or indirect
// call), falling back to a normal closure otherwise. The accumulator is passed
// as a value, so the lambda stays pure and inlinable (unlike capturing a
// `mutable` from the caller).
let inline foldiAlive (map: SlotMap<'T>) (state: 'State) ([<InlineIfLambda>] f: 'State -> uint32 -> 'T -> 'State) : 'State =
    let slots = map.Slots
    let mutable acc = state
    for i in 0 .. slots.Length - 1 do
        let slot = slots.[i]
        if slot.Alive then acc <- f acc (uint32 i) slot.Value
    acc

// ── Editor-facing, index-based access (no generation check) ──

// The editor addresses entities by `EntityRef { kind; index }` — an index with
// no generation. These accessors mirror that: they read/write a slot by index
// alone. Deletion bumps the generation, so a stale index could alias a reused
// slot; the editor clears selection on delete and undo stores the full value, so
// this is safe (and matches ocaml-game's index-based Slot_map.get/set/remove).

// Read a slot by index (alive only), or None. Returns None for an out-of-range
// or dead slot.
let tryGetByIndex (map: SlotMap<'T>) (index: uint32) : 'T option =
    if index >= uint32 map.Slots.Length then None else
    let slot = map.Slots.[int index]
    if slot.Alive then Some slot.Value else None

// Read a slot by index (alive only). Raises on an out-of-range or dead slot.
let getByIndex (map: SlotMap<'T>) (index: uint32) : 'T =
    if index >= uint32 map.Slots.Length then
        invalidOp "slot map index is out of range"
    let slot = map.Slots.[int index]
    if not slot.Alive then
        invalidOp "slot map slot is dead"
    slot.Value

// Write a value at an index. Overwrites an alive slot, or occupies a free slot
// (rebuilding the free list) — the latter is how undo restores a deleted entity
// at its original index. Raises on an out-of-range index.
let setByIndex (map: SlotMap<'T>) (index: uint32) (value: 'T) : SlotMap<'T> =
    if index >= uint32 map.Slots.Length then
        invalidOp "slot map index is out of range"

    let i = int index
    let slot = map.Slots.[i]
    if slot.Alive then
        map.Slots.[i] <- { slot with Value = value }
        map
    else

    // occupy this free slot, then rebuild the free list without it
    map.Slots.[i] <- { slot with Value = value; Alive = true; NextFree = noneIndex }
    let n = map.Slots.Length
    let mutable head = noneIndex
    for j = n - 1 downto 0 do
        if not map.Slots.[j].Alive then
            map.Slots.[j] <- { map.Slots.[j] with NextFree = head }
            head <- uint32 j
    { map with Count = map.Count + 1u; FreeHead = head }

// Free a slot by index (bump generation, push onto the free list), optionally
// running `cleanup` on the value first. The cleanup runs only when the slot is
// alive; an out-of-range index or an already-dead slot still raises.
let removeByIndex (map: SlotMap<'T>) (index: uint32) (cleanup: ('T -> unit) option) : SlotMap<'T> =
    if index >= uint32 map.Slots.Length then
        invalidOp "slot map index is out of range"

    let i = int index
    let slot = map.Slots.[i]
    if not slot.Alive then
        invalidOp "slot map slot is already dead"

    match cleanup with
    | Some f -> f slot.Value
    | None -> ()

    map.Slots.[i] <- { slot with Generation = slot.Generation + 1u; Alive = false; NextFree = map.FreeHead }
    { map with Count = map.Count - 1u; FreeHead = index }
