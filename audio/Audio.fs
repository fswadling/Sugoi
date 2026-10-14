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
