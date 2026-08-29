module SlotMap

// Generational slot map: fixed-capacity, contiguous, inline storage of
// value-type payloads (`'T : struct`) with O(1) add/get/free and stale-handle
// detection via a per-slot generation. Slots and the container are immutable;
// updates use copy-and-update (`with`). Indices are `uint32`, with
// `UInt32.MaxValue` as the "no slot" sentinel.

open System

[<Struct>]
type Slot<'T> =
    { Value : 'T
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
      Count : int
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
      Count = 0
      FreeHead = if n > 0 then 0u else noneIndex }

let add (map: SlotMap<'T>) (value: 'T) : SlotMap<'T> * Handle =
    if map.FreeHead = noneIndex then
        invalidOp "slot map is full"

    let index = int map.FreeHead
    let slot = map.Slots.[index]
    map.Slots.[index] <- { slot with Value = value; Alive = true; NextFree = noneIndex }
    { map with Count = map.Count + 1; FreeHead = slot.NextFree },
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
    { map with Count = map.Count - 1; FreeHead = handle.Index }

let count (map: SlotMap<'T>) : int =
    map.Count
