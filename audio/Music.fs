module Music

// The music model and the deck rack (spec 087). The authored half — `MixRow`/`Mix` and their pure edits
// — is device-free and asserted headlessly. The runtime half — `Deck`/`Rack` — wraps `Audio`'s deck ops
// as one streaming deck per row. `Music` names `Audio` only: it never names `Assets` or `SharedState`, so
// the callers resolve each row's path.

open System
open System.Collections.Generic

// ── the authored mix (spec FR-010) ──

/// One song in the mix: its asset id, its balance, and whether it loops (FR-001, FR-006, FR-009).
type MixRow =
    { Asset : string
      Weight : float32
      Loop : bool }

/// The music bed: the rows plus one master volume.
type Mix =
    { Rows : MixRow list
      Master : float32 }

/// What pressing play does to a row (research D5): continue a held position, or start from the top.
type Start =
    | Resume
    | Restart

/// A row's audible level: `master × weight` (FR-006/FR-007).
let gainOf (master: float32) (weight: float32) : float32 =
    master * weight

/// The empty mix: no rows, full master.
let empty : Mix =
    { Rows = []; Master = 1.0f }

/// Whether the asset is already a row.
let contains (asset: string) (mix: Mix) : bool =
    mix.Rows |> List.exists (fun row -> row.Asset = asset)

/// The row for an asset, if present.
let rowOf (asset: string) (mix: Mix) : MixRow option =
    mix.Rows |> List.tryFind (fun row -> row.Asset = asset)

/// Add a song; a no-op when it is already a row (FR-002).
let addRow (asset: string) (mix: Mix) : Mix =
    if contains asset mix then mix
    else { mix with Rows = mix.Rows @ [ { Asset = asset; Weight = 1.0f; Loop = false } ] }

/// Remove a song; a no-op when it is not a row.
let removeRow (asset: string) (mix: Mix) : Mix =
    { mix with Rows = mix.Rows |> List.filter (fun row -> row.Asset <> asset) }

/// Set a row's weight; a no-op when it is not a row.
let setWeight (asset: string) (weight: float32) (mix: Mix) : Mix =
    { mix with Rows = mix.Rows |> List.map (fun row -> if row.Asset = asset then { row with Weight = weight } else row) }

/// Set a row's loop flag; a no-op when it is not a row.
let setLoop (asset: string) (loop: bool) (mix: Mix) : Mix =
    { mix with Rows = mix.Rows |> List.map (fun row -> if row.Asset = asset then { row with Loop = loop } else row) }

/// Set the master volume.
let setMaster (master: float32) (mix: Mix) : Mix =
    { mix with Master = master }

/// The start rule (FR-003): resume a held row, else restart any other from 0.
let startOf (paused: bool) : Start =
    if paused then Resume else Restart

// ── the runtime rack (spec FR-011) ──

/// One playing row: a streaming audio and the track it plays on. Opaque SDL handles.
type Deck =
    { Stream : Audio.AudioHandle
      Track : Audio.TrackHandle }

/// The runtime decks, keyed by asset id. Never authored, never persisted.
type Rack = Dictionary<string, Deck>

/// A fresh, empty rack.
let newRack () : Rack =
    Dictionary()

// ── device ops ──

/// Apply a row's authored gain (`master × weight`) and loop flag to a live deck.
let private applyToDeck (mix: Mix) (asset: string) (deck: Deck) : unit =
    match rowOf asset mix with
    | None -> ()
    | Some row ->
    do Audio.setGain deck.Track (gainOf mix.Master row.Weight)
    do Audio.setLoops deck.Track row.Loop

/// Apply one row's gain and loop to its live deck, if it has one. This is the targeted edit a
/// caller that changed a single row uses: unlike `reconcile`, it does not walk the whole rack or
/// free a row that left the mix.
let applyRow (mix: Mix) (asset: string) (rack: Rack) : unit =
    match rack.TryGetValue asset with
    | true, deck -> applyToDeck mix asset deck
    | false, _ -> ()

/// Open a row's deck on first use (D10). A file that will not open is reported and yields `None`, so
/// playing an unavailable asset is a no-op rather than a crash (FR-013).
let private openDeck (context: Audio.Context) (rack: Rack) (asset: string) (path: string) : Deck option =
    match rack.TryGetValue asset with
    | true, deck -> Some deck
    | false, _ ->
    try
        let stream = Audio.openStream context path
        let track = Audio.createTrack context
        do Audio.setAudio track stream
        let deck = { Stream = stream; Track = track }
        rack.[asset] <- deck
        Some deck
    with e ->
        do Diagnostics.reportf "music: cannot open '%s': %s" asset e.Message
        None

/// Open the row's deck if needed, then play: resume a held row, else restart it from 0 (FR-003, D5/D10).
let play (context: Audio.Context) (mix: Mix) (rack: Rack) (asset: string) (path: string) : unit =
    if context.Mixer = IntPtr.Zero then () else
    if not (contains asset mix) then invalidOp (sprintf "music: '%s' is not in the mix" asset) else

    match openDeck context rack asset path with
    | None -> ()
    | Some deck ->

    do applyToDeck mix asset deck
    match startOf (Audio.isPaused deck.Track) with
    | Resume -> Audio.resume deck.Track
    | Restart ->

    do Audio.seek deck.Track 0.0f
    do Audio.play deck.Track

/// Hold a row's position (FR-003).
let pause (rack: Rack) (asset: string) : unit =
    match rack.TryGetValue asset with
    | true, deck -> Audio.pauseTrack deck.Track
    | false, _ -> ()

/// Halt a row and rewind it to the start (FR-003).
let stop (rack: Rack) (asset: string) : unit =
    match rack.TryGetValue asset with
    | false, _ -> ()
    | true, deck ->
    do Audio.stopTrack deck.Track
    do Audio.seek deck.Track 0.0f


/// A row's position in seconds (0 when it has no deck) — the scrub's value (FR-008).
let position (rack: Rack) (asset: string) : float32 =
    match rack.TryGetValue asset with
    | true, deck -> Audio.position deck.Track
    | false, _ -> 0.0f

/// A row's duration in seconds (0 when it has no deck) — the scrub's range (FR-008).
let duration (rack: Rack) (asset: string) : float32 =
    match rack.TryGetValue asset with
    | true, deck -> Audio.duration deck.Track deck.Stream
    | false, _ -> 0.0f

/// Whether a row is playing.
let isPlaying (rack: Rack) (asset: string) : bool =
    match rack.TryGetValue asset with
    | true, deck -> Audio.isPlaying deck.Track
    | false, _ -> false

/// Seek a row to a position in seconds (FR-008). A no-op when it has no deck.
let seek (rack: Rack) (asset: string) (seconds: float32) : unit =
    match rack.TryGetValue asset with
    | true, deck -> Audio.seek deck.Track seconds
    | false, _ -> ()

/// Free a deck (stop, destroy the track, close the stream).
let private freeDeck (context: Audio.Context) (deck: Deck) : unit =
    do Audio.stopTrack deck.Track
    do Audio.destroyTrack context deck.Track
    do Audio.closeStream context deck.Stream

/// Bring the rack in line with the authored mix: free a deck whose row has left the mix, re-apply a
/// survivor's gain and loop flag. Called after every `SetMix` (D9).
let reconcile (context: Audio.Context) (rack: Rack) (mix: Mix) : unit =
    // Collect first: a dictionary cannot be mutated while it is enumerated.
    let removed = [ for KeyValue (asset, _) in rack do if not (contains asset mix) then asset ]
    for asset in removed do
        let deck = rack.[asset]
        do freeDeck context deck
        rack.Remove asset |> ignore
    for KeyValue (asset, deck) in rack do
        applyToDeck mix asset deck

/// Free every deck. Called before the mixer is released (FR-014).
let release (context: Audio.Context) (rack: Rack) : unit =
    for KeyValue (_, deck) in rack do freeDeck context deck
    rack.Clear()
