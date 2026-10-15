module Sound

// The one-shot sound playback pool (spec 089). A **sound** is a short effect the asset manager decodes
// into memory (spec 086); playing it is a **one-shot**: it sounds once and stops, and re-playing it
// starts a new instance that may overlap the last. `Voices` is a fixed set of playback tracks reused
// round-robin — because the cursor only advances, the voice taken is the least-recently-used, i.e. the
// oldest-started, so a reuse *is* "the oldest is cut when the pool is full" (FR-003).
//
// This module names `Audio` (the track ops) and `AudioAsset` (the `SoundHandle`) only: the panel
// resolves each sound's resident handle and passes it in, so `Sound` never names `Assets` or
// `SharedState` (the `Music`/`SongPanel` split).

open AudioAsset

/// The bound on simultaneous one-shots — the pool's fixed capacity (FR-003). Not authored, not exposed.
[<Literal>]
let capacity = 16

/// The one-shot playback pool: a fixed set of tracks reused round-robin.
type Voices =
    { Tracks : Audio.TrackHandle array
      /// The asset id each voice is playing, or `""` when idle or its one-shot has finished. A per-sound
      /// Stop matches on this; the pool never asks SDL whether a voice is still sounding.
      Playing : string array
      /// The round-robin cursor: the next voice to use. Only advances, so the voice chosen is the
      /// least-recently-used — the oldest-started (FR-003).
      mutable Next : int }

/// Create the pool: one track per capacity slot, an all-`""` record, cursor at 0. Every track is `0n`
/// when the mixer is unavailable, which makes each op a quiet no-op (FR-008).
let newVoices (context: Audio.Context) : Voices =
    { Tracks = Array.init capacity (fun _ -> Audio.createTrack context)
      Playing = Array.create capacity ""
      Next = 0 }

/// Play a resident sound one-shot: take the next voice (round-robin; the oldest is reused), point it at
/// the decoded buffer and start it from the top, and record the asset id for a later per-sound Stop.
/// Allocates nothing per play.
let play (voices: Voices) (assetId: string) (sound: SoundHandle) : unit =
    let i = voices.Next
    voices.Next <- (i + 1) % voices.Tracks.Length
    do Audio.setAudio voices.Tracks.[i] sound
    do Audio.play voices.Tracks.[i]
    voices.Playing.[i] <- assetId

/// Stop every voice currently recorded as playing `assetId`, leaving other sounds sounding (FR-004).
let stop (voices: Voices) (assetId: string) : unit =
    for i in 0 .. voices.Tracks.Length - 1 do
        if voices.Playing.[i] = assetId then
            do Audio.stopTrack voices.Tracks.[i]
            voices.Playing.[i] <- ""

/// Stop every voice (FR-005).
let stopAll (voices: Voices) : unit =
    for i in 0 .. voices.Tracks.Length - 1 do
        do Audio.stopTrack voices.Tracks.[i]
        voices.Playing.[i] <- ""

/// Free the pool: stop then destroy every track (FR-009). Called before the mixer is released.
let release (context: Audio.Context) (voices: Voices) : unit =
    for track in voices.Tracks do
        do Audio.stopTrack track
        do Audio.destroyTrack context track
    Array.fill voices.Playing 0 voices.Playing.Length ""
