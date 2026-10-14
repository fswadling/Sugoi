module Audio

// The mixer device seam (spec 086). `Context` bundles the `MIX_Mixer` handle with the three
// operations the asset layer needs — load a sound into memory, probe a music file, free a sound — so
// `world/Assets.fs` never names SDL and the suite can inject a fake. `create` brings the subsystem up
// and **never raises**: a machine with no audio device yields `unavailable`, and the game runs on
// (FR-009). SDL audio is initialised here, not in `Engine`'s all-or-nothing `SDL.Init`.

open System
open AudioAsset
open SDL3

/// The mixer handle. Opaque (`MIX_Mixer`); `0n` (null) means audio is unavailable.
type MixerHandle = nativeint

/// The mixer and the operations the asset layer needs. A record of a handle and closures, so `Assets`
/// never names SDL and a machine with no audio device is represented as a value (`Mixer = 0n`).
type Context =
    { Mixer : MixerHandle
      /// Decode a sound to a resident in-memory buffer. Raises on a missing/corrupt file.
      LoadSound : string -> SoundHandle
      /// Open the file through a decoder and discard it — validate without holding decoded audio.
      /// Raises on a missing/corrupt/undecodable file.
      Probe : string -> unit
      /// Free a resident sound.
      FreeSound : SoundHandle -> unit }

/// The degraded context: no mixer, every operation raises. Used when the audio subsystem cannot start
/// and by headless tests (which never open a device).
let unavailable : Context =
    { Mixer = IntPtr.Zero
      LoadSound = fun _ -> failwith "audio is unavailable (no device or SDL_mixer failed to start)"
      Probe = fun _ -> failwith "audio is unavailable (no device or SDL_mixer failed to start)"
      FreeSound = fun _ -> () }

/// Bring up SDL audio + SDL_mixer and a default playback mixer. Any failure is reported and yields
/// `unavailable`; it never raises (FR-009).
let create () : Context =
    if not (SDL.InitSubSystem(SDL.InitFlags.Audio)) then
        do Diagnostics.reportf "audio: SDL audio init failed: %s; audio unavailable" (SDL.GetError())
        unavailable
    else if not (Mixer.Init()) then
        do Diagnostics.reportf "audio: MIX_Init failed: %s; audio unavailable" (SDL.GetError())
        unavailable
    else

    let mixer = Mixer.CreateMixerDevice(SDL.AudioDeviceDefaultPlayback, IntPtr.Zero)
    if mixer = IntPtr.Zero then
        do Diagnostics.reportf "audio: no playback device: %s; audio unavailable" (SDL.GetError())
        do Mixer.Quit() |> ignore
        unavailable
    else

    // A streaming handle (predecode = false) is only meaningful while playing, so it is the playback
    // feature's concern; this phase pre-decodes sounds and only probes music.
    { Mixer = mixer
      LoadSound =
        fun path ->
            let handle = Mixer.LoadAudio(mixer, path, true)
            if handle = IntPtr.Zero then failwithf "audio: cannot load '%s': %s" path (SDL.GetError())
            handle
      Probe =
        fun path ->
            let decoder = Mixer.CreateAudioDecoder(path, 0u)
            if decoder = IntPtr.Zero then failwithf "audio: cannot decode '%s': %s" path (SDL.GetError())
            do Mixer.DestroyAudioDecoder decoder |> ignore
      FreeSound = fun handle -> Mixer.DestroyAudio handle |> ignore }

/// Destroy the mixer and quit SDL_mixer. Called after every resident sound has been freed.
let release (context: Context) : unit =
    if context.Mixer <> IntPtr.Zero then
        do Mixer.DestroyMixer context.Mixer |> ignore
        do Mixer.Quit() |> ignore

// ── playback decks (spec 087) ──
//
// The music-playback device ops: a streaming deck, its transport, gain, loop and seek, and its
// position. Thin wrappers over `SDL3.Mixer`, so `Music`, the panel and `History` never name SDL. A `0n`
// handle — an `unavailable` context (spec 086) or a missing deck — makes every op a quiet no-op, so an
// absent audio subsystem leaves the panel drawing and the game running (FR-013).

/// A streaming music handle (`MIX_Audio`, opened with `predecode = false`). Opaque.
type AudioHandle = nativeint

/// A playback track (`MIX_Track`). Opaque.
type TrackHandle = nativeint

/// Open a streaming audio for playback (not decoded — spec 086 keeps music on disk). May raise on a
/// missing/corrupt file; the caller treats that as the expected unavailable case (FR-013).
let openStream (context: Context) (path: string) : AudioHandle =
    if context.Mixer = IntPtr.Zero then IntPtr.Zero else

    let handle = Mixer.LoadAudio(context.Mixer, path, false)
    if handle = IntPtr.Zero then failwithf "audio: cannot open '%s': %s" path (SDL.GetError())
    handle

/// Free a streaming audio. No-op on a `0n` handle.
let closeStream (context: Context) (handle: AudioHandle) : unit =
    if context.Mixer <> IntPtr.Zero && handle <> IntPtr.Zero then Mixer.DestroyAudio handle |> ignore

/// Create a track on the mixer. `0n` when the mixer is unavailable.
let createTrack (context: Context) : TrackHandle =
    if context.Mixer = IntPtr.Zero then IntPtr.Zero else Mixer.CreateTrack context.Mixer

/// Destroy a track. No-op on a `0n` handle.
let destroyTrack (context: Context) (track: TrackHandle) : unit =
    if context.Mixer <> IntPtr.Zero && track <> IntPtr.Zero then Mixer.DestroyTrack track |> ignore

/// Point a track at its audio. No-op on a `0n` track.
let setAudio (track: TrackHandle) (audio: AudioHandle) : unit =
    if track <> IntPtr.Zero then Mixer.SetTrackAudio(track, audio) |> ignore

/// Start (or restart) mixing a track.
let play (track: TrackHandle) : unit =
    if track <> IntPtr.Zero then Mixer.PlayTrack(track, 0u) |> ignore

/// Hold a track's position (a pause, not a stop).
let pauseTrack (track: TrackHandle) : unit =
    if track <> IntPtr.Zero then Mixer.PauseTrack track |> ignore

/// Continue a held track.
let resume (track: TrackHandle) : unit =
    if track <> IntPtr.Zero then Mixer.ResumeTrack track |> ignore

/// Halt a track (no fade). Its position is left where it stopped; callers rewind separately.
let stopTrack (track: TrackHandle) : unit =
    if track <> IntPtr.Zero then Mixer.StopTrack(track, 0L) |> ignore

/// Set a track's gain (linear; the row's `master × weight`). Negative is illegal; callers clamp to 0..1.
let setGain (track: TrackHandle) (gain: float32) : unit =
    if track <> IntPtr.Zero then Mixer.SetTrackGain(track, gain) |> ignore

/// Loop forever (`true`) or play once (`false`).
let setLoops (track: TrackHandle) (loop: bool) : unit =
    if track <> IntPtr.Zero then Mixer.SetTrackLoops(track, (if loop then -1 else 0)) |> ignore

/// Seek a track to a position in seconds.
let seek (track: TrackHandle) (seconds: float32) : unit =
    if track <> IntPtr.Zero then
        let frames = Mixer.TrackMSToFrames(track, int64 (seconds * 1000.0f))
        Mixer.SetTrackPlaybackPosition(track, frames) |> ignore

/// A playing track's position in seconds (0 when there is no track).
let position (track: TrackHandle) : float32 =
    if track = IntPtr.Zero then 0.0f
    else float32 (Mixer.TrackFramesToMS(track, Mixer.GetTrackPlaybackPosition track)) / 1000.0f

/// A track input's duration in seconds (0 when there is no track or audio). Duration is reported in
/// sample frames, so it is converted through the track's format.
let duration (track: TrackHandle) (audio: AudioHandle) : float32 =
    if track = IntPtr.Zero || audio = IntPtr.Zero then 0.0f
    else float32 (Mixer.TrackFramesToMS(track, Mixer.GetAudioDuration audio)) / 1000.0f

/// Whether a track is currently contributing to the mix.
let isPlaying (track: TrackHandle) : bool =
    track <> IntPtr.Zero && Mixer.TrackPlaying track

/// Whether a track is held (paused, not stopped).
let isPaused (track: TrackHandle) : bool =
    track <> IntPtr.Zero && Mixer.TrackPaused track
