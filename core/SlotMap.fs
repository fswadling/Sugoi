module SlotMap

// Generational slot map: fixed-capacity, contiguous storage of value-type
// payloads (`'T : struct`) with O(1) add/get/free and stale-handle detection.
// A sealed class mutated in place, so a reference is the only map and no copy
// can desync from the shared storage; `Get` returns a `byref` into the payload.

open System

[<Struct>]
type Slot<'T> =
    { mutable Value : 'T
      Generation : uint32
      Alive : bool
      mutable NextFree : uint32 }

[<Struct>]
type Handle =
    { Index : uint32
      Generation : uint32 }

// "No slot" sentinel for free-list links and the free-list head.
let private noneIndex = UInt32.MaxValue

[<Sealed>]
type SlotMap<'T when 'T : struct>(capacity: uint) =

    let slotCount = int capacity

    // Pre-allocate the slots and link the free list: 0 -> 1 -> ... -> (n - 1).
    let slots =
        Array.init slotCount (fun i ->
            { Value = Unchecked.defaultof<'T>
              Generation = 0u
              Alive = false
              NextFree = if i < slotCount - 1 then uint32 (i + 1) else noneIndex })

    let mutable activeCount = 0u
    let mutable freeHead = if slotCount > 0 then 0u else noneIndex

    // The backing slots, for systems that scan directly. `Value` and `NextFree` are the
    // mutable fields: a scan can write a payload in place through `&slots.[i]`, and
    // `Occupy` can relink a dead slot without copying its payload. `Generation` and
    // `Alive` stay immutable so a scan cannot desync the free list. Replacing a whole
    // slot (`slots.[i] <- { ... }`) is still possible and still wrong; that is what
    // `Add`/`Free`/`Occupy` are for.
    member _.Slots : Span<Slot<'T>> = Span<Slot<'T>>(slots)

    member _.Count : uint32 = activeCount

    // The fixed capacity the map was built with. Free room is exactly `Capacity - Count`,
    // because every non-alive slot is on the free list — what a fork's capacity pre-check reads.
    member _.Capacity : uint32 = uint32 slotCount

    member _.Add(value: 'T) : Handle =
        if freeHead = noneIndex then
            invalidOp "slot map is full"

        let index = int freeHead
        let slot = slots.[index]
        slots.[index] <- { slot with Value = value; Alive = true; NextFree = noneIndex }
        activeCount <- activeCount + 1u
        freeHead <- slot.NextFree
        { Index = uint32 index; Generation = slot.Generation }

    // Validate a handle and return its array index; raises on out-of-range or stale.
    member private _.CheckedSlot(handle: Handle) : int =
        if handle.Index >= uint32 slots.Length then
            invalidOp "slot map handle is out of range"

        let i = int handle.Index
        let slot = slots.[i]
        if not slot.Alive || slot.Generation <> handle.Generation then
            invalidOp "slot map handle is stale"

        i

    member this.Get(handle: Handle) : byref<'T> =
        &slots.[this.CheckedSlot(handle)].Value

    member this.Free(handle: Handle) : unit =
        let index = this.CheckedSlot(handle)
        let slot = slots.[index]

        // bump the generation so old handles go stale, then push onto the free list
        slots.[index] <- { slot with Generation = slot.Generation + 1u; Alive = false; NextFree = freeHead }
        activeCount <- activeCount - 1u
        freeHead <- handle.Index

    // Scan the alive slots. `[<InlineIfLambda>]` inlines a statically-known lambda
    // (no closure or indirect call). The value is passed by copy, so a loop that
    // mutates a payload in place scans `Slots` itself instead.
    member inline this.IteriAlive([<InlineIfLambda>] f: uint32 -> uint32 -> 'T -> unit) : unit =
        let slots = this.Slots
        for i in 0 .. slots.Length - 1 do
            let slot = &slots.[i]
            if slot.Alive then f (uint32 i) slot.Generation slot.Value

    // ── Editor-facing access ──

    // Every accessor names a `Handle` (index + generation), so none can read or
    // write a slot that has since been freed and reused.

    // Whether the slot named by `handle` is alive.
    member _.IsAlive(handle: Handle) : bool =
        if handle.Index >= uint32 slots.Length then false else
        let slot = slots.[int handle.Index]
        slot.Alive && slot.Generation = handle.Generation

    // Whether the slot holds any entity at all, regardless of generation. The
    // precondition `Occupy` needs: a slot reused by another entity is occupied but
    // not alive at the original generation.
    member _.IsOccupied(index: uint32) : bool =
        index < uint32 slots.Length && slots.[int index].Alive

    // Re-occupy the free slot named by `handle` and restore its generation, so the
    // caller's ref resolves again — how undo restores a deleted entity. Rebuilds the
    // free list (O(capacity); undo path only) and raises if the slot is already
    // alive, so a slot recycled by an untracked spawn fails loudly rather than being
    // clobbered.
    member _.Occupy(handle: Handle, value: 'T) : unit =
        if handle.Index >= uint32 slots.Length then
            invalidOp "slot map index is out of range"

        let i = int handle.Index
        let slot = slots.[i]
        if slot.Alive then
            invalidOp (sprintf "slot map slot %d is already alive (generation %d, occupy asked for %d)" i slot.Generation handle.Generation)

        slots.[i] <- { slot with Value = value; Generation = handle.Generation; Alive = true; NextFree = noneIndex }
        let mutable head = noneIndex
        for j = slots.Length - 1 downto 0 do
            if not slots.[j].Alive then
                slots.[j].NextFree <- head
                head <- uint32 j
        activeCount <- activeCount + 1u
        freeHead <- head

    // Free the slot named by `handle`, running `cleanup` on the value first. Raises
    // on an out-of-range or stale/dead slot.
    member this.Remove(handle: Handle, cleanup: ('T -> unit) option) : unit =
        let i = this.CheckedSlot(handle)
        let slot = slots.[i]

        match cleanup with
        | Some f -> f slot.Value
        | None -> ()

        slots.[i] <- { slot with Generation = slot.Generation + 1u; Alive = false; NextFree = freeHead }
        activeCount <- activeCount - 1u
        freeHead <- handle.Index
