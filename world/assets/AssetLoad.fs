module AssetLoad

// Async environment-load substrate (spec 053). Three pieces, all CPU-only — nothing here touches an
// SDL_gpu device, a command buffer or the swapchain:
//   * the decoded payload a worker hands to the render thread (`DecodedEnvironment` / `LoadOutcome`);
//   * the bounded, de-duplicated, most-recent-first pending set (`LoadQueue`), guarded by one lock;
//   * the worker pool (`LoadPool`) that claims, decodes and posts.
// The render thread owns `LoadQueue`/`LoadPool` and every device call; the workers only read files,
// parse glTF and decode images. See specs/053-async-asset-loading (research D1–D6, D13, D14).

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Channels
open Lru

// ── keys (documentation-only string aliases; F# erases them) ──

type AssetId = string
type ModelPath = string
type TextureKey = string   // an absolute texture path (the render-side cache key)
type TextureUri = string    // a glTF material's texture URI, relative to the model's directory

// ── constants ──

// The pending-set bound (a count of outstanding requests, unrelated to decode concurrency).
[<Literal>]
let LoadQueueCapacity = 8

// The default concurrent-decode count — a *memory* bound, not a GPU one (a decoded ArtRoom is ~1.3 GB
// of pixels). Overridable by `FFS_LOAD_WORKERS`, clamped to ≥ 1.
let private defaultLoadWorkers = 2

let loadWorkers : int =
    match Environment.GetEnvironmentVariable "FFS_LOAD_WORKERS" with
    | null -> defaultLoadWorkers
    | v ->
    match Int32.TryParse v with
    | true, n -> max 1 n
    | _ -> defaultLoadWorkers

// ── the decoded payload ──

// The CPU half of one environment load: the parsed model and every distinct texture it references,
// decoded to compacted RGBA8. Handed from a worker to the render thread, which turns it into GPU
// resources and then drops it. `DecodeNs` is worker-measured so no worker writes a process-global.
type DecodedEnvironment =
    { AssetId : AssetId
      Model : Gltf.Model
      Images : Dictionary<TextureKey, Texture.DecodedImage>
      ParseNs : int64
      DecodeNs : int64
      QueueNs : int64 }   // request → claim; set by the worker (0 in `decodeEnvironment`)

// What a worker posts back. A failure is a value, never a throw across the thread boundary.
type LoadOutcome =
    | Decoded of DecodedEnvironment
    | LoadFailed of AssetId : AssetId * Message : string

// Parse the model and decode every distinct texture any material references. CPU-only; a missing or
// corrupt file is caught and returned as `LoadFailed`. The worker has no view of the render thread's
// texture cache, so it decodes every distinct path — accepted redundancy (research D14).
let decodeEnvironment (assetId: AssetId) (modelPath: ModelPath) : LoadOutcome =
    try
        let t0 = Stopwatch.GetTimestamp()
        let model = Gltf.load modelPath
        let parseNs = Stopwatch.GetTimestamp() - t0
        let gltfDir = IO.Path.GetDirectoryName modelPath
        let images = Dictionary<TextureKey, Texture.DecodedImage>()
        let mutable decodeNs = 0L
        let decodeOnce (uri: TextureUri) : unit =
            let path = IO.Path.Combine(gltfDir, uri)
            if not (images.ContainsKey path) then
                let image = Texture.decode path
                decodeNs <- decodeNs + image.DecodeNs
                images.[path] <- image
        for m in model.Materials do
            match m.AlbedoTexture with Some r -> decodeOnce r.Uri | None -> ()
            match m.NormalTexture with Some r -> decodeOnce r.Uri | None -> ()
            match m.MetallicRoughnessTexture with Some r -> decodeOnce r.Uri | None -> ()
            match m.EmissiveTexture with Some r -> decodeOnce r.Uri | None -> ()
        Decoded { AssetId = assetId; Model = model; Images = images; ParseNs = parseNs; DecodeNs = decodeNs; QueueNs = 0L }
    with e -> LoadFailed(assetId, e.Message)

// ── the pending set ──

// The bounded, de-duplicated, most-recent-first set of outstanding requests, guarded by one lock. It
// also holds what each worker has claimed, the pin set, the single active upload, and the id→path
// table the claim reads — so everything the pool needs is under one gate.
type LoadQueue =
    { Gate : obj
      Pending : Lru                    // head = most recently requested; queued + decoding + uploading
      Claimed : HashSet<AssetId>       // ids claimed by a worker, or being uploaded, or awaiting upload
      Pinned : HashSet<AssetId>        // the active environment: never dropped, never evicted
      Paths : Dictionary<AssetId, ModelPath>
      RequestTicks : Dictionary<AssetId, int64>   // when each request arrived (for the queue-wait timing)
      mutable Uploading : AssetId option // the id whose GPU work is in progress (at most one)
      Capacity : int }

let createQueue (capacity: int) : LoadQueue =
    { Gate = obj ()
      Pending = Lru.create ()
      Claimed = HashSet<AssetId>()
      Pinned = HashSet<AssetId>()
      Paths = Dictionary<AssetId, ModelPath>()
      RequestTicks = Dictionary<AssetId, int64>()
      Uploading = None
      Capacity = capacity }

// Least-recently-requested first.
let private lruFirst (q: LoadQueue) : AssetId list = Lru.keys q.Pending |> List.rev

let private isDroppable (q: LoadQueue) (id: AssetId) : bool =
    not (q.Pinned.Contains id) && q.Uploading <> Some id

// An id is claimable only while it is wanted, not already held, and not already uploading. The claim
// is held from `claimLocked` until the id becomes resident, is discarded, or fails — so an asset being
// uploaded (a cursor in progress) is never decoded a second time.
let private isClaimable (q: LoadQueue) (id: AssetId) : bool =
    not (q.Claimed.Contains id) && q.Uploading <> Some id

let private hasUnclaimed (q: LoadQueue) : bool =
    Lru.keys q.Pending |> List.exists (isClaimable q)

// Request `assetId`, de-duplicated and moved to the most-recent end. Drops the least-recently-requested
// tails that are neither pinned nor uploading, returning their ids so the caller can reset residency.
let request (q: LoadQueue) (assetId: AssetId) (modelPath: ModelPath) : AssetId list =
    lock q.Gate (fun () ->
        q.Paths.[assetId] <- modelPath
        q.RequestTicks.[assetId] <- Stopwatch.GetTimestamp()
        Lru.touch q.Pending assetId
        let dropped = ResizeArray<AssetId>()
        let mutable over = Lru.count q.Pending > q.Capacity
        while over do
            match lruFirst q |> List.tryFind (isDroppable q) with
            | Some victim ->
                Lru.remove q.Pending victim
                dropped.Add victim
                over <- Lru.count q.Pending > q.Capacity
            | None -> over <- false
        Monitor.PulseAll q.Gate
        List.ofSeq dropped)

// Replace the pin set. The gameplay layer owns it and narrows it to the active environment every
// frame, so it never accumulates the ids of environments that are no longer in use.
let setPinned (q: LoadQueue) (pinnedIds: AssetId list) : unit =
    lock q.Gate (fun () ->
        q.Pinned.Clear()
        for p in pinnedIds do q.Pinned.Add p |> ignore)

// Claim the most-recently-requested claimable id (with its model path and request time), or None.
// Called under the worker's wait, so it is atomic with respect to `request`/`makeResident`.
let private claimLocked (q: LoadQueue) : (AssetId * ModelPath * int64) option =
    match Lru.keys q.Pending |> List.tryFind (isClaimable q) with
    | Some id when q.Paths.ContainsKey id ->
        q.Claimed.Add id |> ignore
        Some (id, q.Paths.[id], (match q.RequestTicks.TryGetValue id with true, t -> t | _ -> Stopwatch.GetTimestamp()))
    | _ -> None

// Claim the most-recently-requested claimable id (with its model path), or None. Used by the worker
// under its wait and by the tests.
let claimNext (q: LoadQueue) : (AssetId * ModelPath) option =
    lock q.Gate (fun () -> claimLocked q |> Option.map (fun (id, path, _) -> (id, path)))

let isPending (q: LoadQueue) (assetId: AssetId) : bool = lock q.Gate (fun () -> Lru.contains q.Pending assetId)
let isClaimed (q: LoadQueue) (assetId: AssetId) : bool = lock q.Gate (fun () -> q.Claimed.Contains assetId)
let claimedCount (q: LoadQueue) : int = lock q.Gate (fun () -> q.Claimed.Count)
let pins (q: LoadQueue) : AssetId list = lock q.Gate (fun () -> List.ofSeq q.Pinned)

// The id's GPU work is in progress (at most one, because the pump builds one cursor at a time). A
// claimed id that reaches this point stays claimed until it is resident, so it cannot be re-decoded.
let beginUpload (q: LoadQueue) (assetId: AssetId) : unit = lock q.Gate (fun () -> q.Uploading <- Some assetId)
let endUpload (q: LoadQueue) (assetId: AssetId) : unit =
    lock q.Gate (fun () -> if q.Uploading = Some assetId then q.Uploading <- None)

// The asset is resident (or discarded): it leaves the wanted set and the claim is released.
let makeResident (q: LoadQueue) (assetId: AssetId) : unit =
    lock q.Gate (fun () ->
        q.Claimed.Remove assetId |> ignore
        Lru.remove q.Pending assetId
        q.Paths.Remove assetId |> ignore
        q.RequestTicks.Remove assetId |> ignore
        if q.Uploading = Some assetId then q.Uploading <- None)

// ── the worker pool ──

// `Slots` bounds the payloads that are decoded-but-not-yet-uploaded to the worker count, so a fast
// decode cannot run ahead of the budgeted upload and pile up decoded pixels (research D6/D13). A slot
// is taken before a decode and released only when the payload's cursor settles, is discarded, or
// fails — so it covers the decode, the ready queue, and the cursor alike.
type LoadPool =
    { Queue : LoadQueue
      Ready : Channel<LoadOutcome>
      Workers : Thread array
      Stopping : bool ref
      Slots : SemaphoreSlim
      Stop : CancellationTokenSource
      ActiveDecodes : int ref }

let private runWorker
    (q: LoadQueue) (ready: Channel<LoadOutcome>) (slots: SemaphoreSlim) (stop: CancellationToken)
    (stopping: bool ref) (activeDecodes: int ref) (decode: AssetId -> ModelPath -> LoadOutcome) () : unit =
    let mutable working = true
    while working do
        let claimed =
            lock q.Gate (fun () ->
                while not stopping.Value && not (hasUnclaimed q) do
                    Monitor.Wait q.Gate |> ignore
                if stopping.Value then None else claimLocked q)
        match claimed with
        | None -> working <- false
        | Some (assetId, modelPath, requestTicks) ->
        // Take an outstanding-payload slot before spending a decode on it. The slot is held until
        // the payload's cursor settles (or the payload is discarded), so at most `workers`
        // decoded-but-not-yet-uploaded payloads exist at once (research D6/D13).
        let acquired =
            try slots.Wait stop; true
            with :? OperationCanceledException -> false
        if not acquired then
            working <- false
        elif not (isPending q assetId) then
            // Dropped while waiting for a slot: release the claim and the slot, decode nothing.
            do makeResident q assetId
            do slots.Release() |> ignore
        else

        do Interlocked.Increment activeDecodes |> ignore
        let started = Stopwatch.GetTimestamp()
        // The decode runs outside the lock; a per-id failure is a value, never a throw.
        let outcome =
            try decode assetId modelPath
            with e -> LoadFailed(assetId, e.Message)
        do Interlocked.Decrement activeDecodes |> ignore
        let outcome =
            match outcome with
            | Decoded payload -> Decoded { payload with QueueNs = started - requestTicks }
            | LoadFailed _ as failure -> failure
        do ready.Writer.TryWrite outcome |> ignore

let createPool (workers: int) (capacity: int) (decode: AssetId -> ModelPath -> LoadOutcome) : LoadPool =
    let q = createQueue capacity
    let ready = Channel.CreateUnbounded<LoadOutcome>()
    let slots = new SemaphoreSlim(workers, workers)
    let stop = new CancellationTokenSource()
    let stopping = ref false
    let activeDecodes = ref 0
    let threads =
        Array.init workers (fun i ->
            let t = Thread(runWorker q ready slots stop.Token stopping activeDecodes decode, 8 * 1024 * 1024)
            t.IsBackground <- true
            t.Name <- sprintf "asset-load-%d" i
            t.Start()
            t)
    { Queue = q; Ready = ready; Workers = threads; Stopping = stopping; Slots = slots; Stop = stop; ActiveDecodes = activeDecodes }

let requestLoad (pool: LoadPool) (assetId: AssetId) (modelPath: ModelPath) : AssetId list =
    request pool.Queue assetId modelPath

// Consume a posted outcome. The outstanding-payload slot is **not** released here: if the payload
// becomes a cursor it must stay bounded until the cursor settles (`releaseSlot`).
let tryTakeReady (pool: LoadPool) : LoadOutcome option =
    match pool.Ready.Reader.TryRead() with
    | true, outcome -> Some outcome
    | false, _ -> None

// Release one outstanding-payload slot: the payload has been discarded, or its cursor has settled,
// been dropped, or failed. Held from just before a decode until then, so `Slots` bounds decoded (or
// decoding) payloads — including cursors — to the worker count.
let releaseSlot (pool: LoadPool) : unit = pool.Slots.Release() |> ignore

// Decodes running right now (FR-009's bound). Not the claim count: a worker may hold a claim across
// the upload without decoding.
let inFlight (pool: LoadPool) : int = pool.ActiveDecodes.Value

let isPendingOutcome (pool: LoadPool) (assetId: AssetId) : bool = isPending pool.Queue assetId
let makeResidentOutcome (pool: LoadPool) (assetId: AssetId) : unit = makeResident pool.Queue assetId
let beginUploadOutcome (pool: LoadPool) (assetId: AssetId) : unit = beginUpload pool.Queue assetId
let endUploadOutcome (pool: LoadPool) (assetId: AssetId) : unit = endUpload pool.Queue assetId
let setPinnedPool (pool: LoadPool) (pinnedIds: AssetId list) : unit = setPinned pool.Queue pinnedIds

// Stop the workers: cancel any slot wait, wake any gate wait, then join with a short bounded timeout. A
// worker mid-decode is abandoned — it touches only managed memory, so its payload is simply dropped.
let stopPool (pool: LoadPool) : unit =
    do pool.Stop.Cancel()
    lock pool.Queue.Gate (fun () ->
        pool.Stopping.Value <- true
        Monitor.PulseAll pool.Queue.Gate)
    for t in pool.Workers do
        t.Join 500 |> ignore
