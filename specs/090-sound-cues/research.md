# Phase 0 — Research: Sound Cues — play a sound from the story

Decisions settled before design. Each is a choice with its rationale and the alternatives weighed.

## D1 — The op: `PlaySound of sound: string`, a no-progress leaf with no `…State`

**Decision**: add exactly one case to the closed vocabulary — `| PlaySound of sound: string` — a
**no-progress leaf** (it begins and finishes in the same step). There is no `PlaySoundState`.

**Rationale**: the vocabulary's convention (`world/Cue.fs`) is that a case suffixed `State` appears
exactly where the run form holds something the authored form does not. A one-shot sound is not held —
the cue plays it and moves on (FR-003) — so nothing is captured and no state case is needed, exactly as
`AddActor`/`PlayMusic` hold nothing. The sound is named by its asset id, as a music op names its song.

**Alternatives considered**: a `PlaySoundState` holding the voice/handle to await the sound (rejected —
a one-shot is fire-and-forget, FR-003; awaiting it would need a per-frame playing check and would stall
the story); a palette of sounds like the music mix (rejected — a sound has no membership/level; spec 089
is a bare pool); a `StopSound` family (rejected — spec clarified `PlaySound` only).

## D2 — The engine: resolve the resident sound, then `Sound.play`

**Decision**: in `world/CueEngine.fs`, add `trySoundAsset` (the asset whose `Class = Sound` and
`isResident` holds, else `None`) and `playSound`:

```fsharp
let private playSound (op: string) (shared: SharedState.SharedState) (sound: string) : unit =
    match trySoundAsset shared sound with
    | None -> do noOpSound op sound "its asset is unavailable"
    | Some asset -> do Sound.play shared.Voices sound (AudioAsset.sound asset)
```

and the `advance` case `| PlaySound sound -> do playSound "PlaySound" shared sound; ValueSome Fin`.

**Rationale**: this is `playMusic`'s shape (spec 088) minus the palette check (a sound has none) —
resolve the asset, call the existing playback module as-is. `Sound.play` records the asset id on the
voice it takes, so the effect is a pure data move plus two SDL calls, and it no-ops on a `0n` mixer.

**Alternatives considered**: decoding the sound here (rejected — `Sound.play` takes the resident
handle, spec 086/089); resolving by name (rejected — assets are addressed by id, the music rule).

## D3 — A sound-specific report, mirroring the music one

**Decision**: add `noOpSound (op: string) (sound: string) (reason: string)` beside `noOpMusic`
(`Diagnostics.reportf "cue: %s(%s) performs nothing: %s" …`), and report
`"its asset is unavailable"` for an absent, music-class or failed sound.

**Rationale**: every cue op reports why it performed nothing (spec 080's visibility rule), and the music
ops already have the exact form; a parallel helper keeps the message shape identical and greppable. A
resident sound never reports.

**Alternatives considered**: reusing `noOpMusic` (rejected — it is named for music and its message reads
"song"); reporting the failure reason from the asset (rejected — the resolver already collapses
absent/failed/non-sound to one user-facing reason, matching the music rule).

## D4 — The chooser: generalise the cue editor's pick-only combo

**Decision**: the cue editor's `drawSongCombo` becomes `drawPickCombo`, taking its candidate list and a
label instead of reading `Assets.musicNames` itself. Music callers pass `Assets.musicNames assets`
("Song"); the sound op passes the discovered sound ids ("Sound").

**Rationale**: the control is "pick one name from a discovered-asset list" — identical for a song and a
sound. Generalising it adds no control and reuses the proven pick-only behaviour (an op naming an
undiscovered asset could only no-op, so there is no typed fallback, spec 088 D6).

**Alternatives considered**: a separate `drawSoundCombo` (rejected — near-duplicate code); a new
`Assets.soundNames` helper (rejected — the editor can map `Assets.soundAssets` to ids, so no new
`Assets` API is needed).

## D5 — Closed vocabulary: update every exhaustive match

**Decision**: adding the case forces an update at each exhaustive match, and they are all updated:
`editor/CueTree.fs` (`executing`, `label`, `children`, `kindName`, `defaultOfKind`, `withSong`,
`drawEditor`'s no-numbers list), `editor/CueEditor.fs` (`drawNodeText`), and the two test `describe`
functions (`test/CueTests.fs`, `test/CuePanelTests.fs`).

**Rationale**: Constitution N-III makes the vocabulary closed precisely so a missed case is a **compile
error**, not a silent gap — the compiler enumerates the work. `withSong` is reused as the op's single
name setter (its doc is broadened); renaming it would touch the music call sites for no behaviour.

**Alternatives considered**: renaming `withSong` → `withName` (rejected — churn across the music ops for
a label; the helper's contract is "set the op's one name").

## D6 — Testing: a fabricated resident sound, ticked headlessly

**Decision**: `test/CueTests.fs` builds a `SharedState`, inserts one **resident** sound asset into
`shared.Assets.AudioAssets`, ticks a cue holding `PlaySound "beep"`, and asserts
`shared.Voices.Playing.[0] = "beep"` and `state.Cue = Fin` and `state.Editor.Undo.Applied` is empty;
plus an unknown id and a music-class asset are no-ops (cue still reaches `Fin`, nothing recorded). The
editor behaviour is asserted over `CueTree` (`label`, `kindName`, `defaultOfKind`, `withSong`,
`placeOf`), as `test/MusicCueTests.fs` asserts the music ops.

**Rationale**: `Sound.play` records each voice's asset id as a plain data move, so "the sound was
played" is a headless assertion needing no device — far stronger than "the cue reached `Fin`". The
fixture is built by mutating the manager's mutable `AudioAssets` dictionary, so no `TestSupport` change.

**Alternatives considered**: asserting only that the cue reached `Fin` (rejected — an op that no-ops for
the wrong reason would pass); an audible test (rejected — the suite is headless).
