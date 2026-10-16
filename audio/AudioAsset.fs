module AudioAsset

// The audio asset model (spec 086): what an `assets/audio/*.manifest` declares and what the asset
// manager holds for it. A **sound** is decoded into memory (resident, ready); a **music** track stays
// a reference to its compressed file on disk (decoded audio is uncompressed and large). Split from
// `Audio` (the mixer device ops) so `world/assets/Assets.fs` can name the record without depending on the
// device layer for a type.

open Prime

// A loaded sound's decoded buffer, owned by the mixer. Opaque (`MIX_Audio`).
type SoundHandle = nativeint

/// The one distinction this phase acts on: a long track referenced on disk, or a short effect held
/// in memory.
type Class =
    | Music
    | Sound

/// The on-disk manifest shape. Named fields, so the parser is robust to reordering; an absent field
/// parses to null.
[<SymbolicExpansion>]
type Manifest =
    { Id : string
      Name : string
      File : string
      Kind : string }

/// The asset's coarse lifecycle, single-writer on the render thread. A sound is `Resident` or
/// `Failed`; a music track is `Reference` or `Failed`. There is no deferred state: the scan loads
/// everything in one eager pass (spec 086 FR-005).
type Residency =
    | Resident of SoundHandle
    | Reference
    | Failed of Message : string

type AudioAsset =
    { Id : string
      Name : string
      Path : string          // absolute; resolved once at scan
      Class : Class
      mutable Residency : Residency }

// ── loaded accessors ──
//
// The unit is reached only where it must be present; a read over a not-resident asset is a bug that
// fails loudly rather than reading a freed handle (mirrors `EnvironmentAsset`).

let isResident (asset: AudioAsset) : bool =
    match asset.Residency with
    | Resident _ -> true
    | _ -> false

let sound (asset: AudioAsset) : SoundHandle =
    match asset.Residency with
    | Resident handle -> handle
    | _ -> invalidOp (sprintf "audio asset '%s' is not resident" asset.Id)

let isReference (asset: AudioAsset) : bool =
    match asset.Residency with
    | Reference -> true
    | _ -> false
