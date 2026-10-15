# Contract — the surfaces that change

The feature's interface is internal (a native desktop game has no external API), so this documents the
module boundaries that change, what each promises, and what explicitly does not move. Paths are
project-relative; compile order is `fsharp-game.fsproj` list order (unchanged by this feature).

## Changed — `world/Cue.fs` (the closed vocabulary)

```fsharp
type Cue =
    | ...
    // The sound cue (spec 090): play a named sound one-shot where the run reaches it — a no-progress
    // leaf (a one-shot is not held), the runtime counterpart of the Sounds panel's Play. A runtime
    // effect that never lands on the one history, the music-op rule.
    | PlaySound of sound: string
```

**Promises**: it names a sound asset by id; it holds no state (no `…State`); the module doc's
no-progress-op list gains it.

## Changed — `world/CueEngine.fs`

```fsharp
/// A playable sound asset (a resident `Kind = Sound`), or `None`.
val private trySoundAsset : SharedState.SharedState -> sound:string -> AudioAsset.AudioAsset option

/// Play a named sound one-shot; report and skip an unresolvable id (FR-005).
val private playSound : op:string -> SharedState.SharedState -> sound:string -> unit
```

and in `advance`:

```fsharp
| PlaySound sound ->
    do playSound "PlaySound" shared sound
    ValueSome Fin
```

**Promises**: a no-progress leaf (`ValueSome Fin`); never raises; a resident sound is played via
`Sound.play shared.Voices sound (AudioAsset.sound asset)` (so it obeys the pool's overlap bound);
an absent/music-class/failed id is a reported no-op.

## Changed — `editor/CueTree.fs`

- `executing`: `PlaySound _ -> false`.
- `label`: `| PlaySound sound -> namedLabel "Play sound" sound`.
- `children`: `PlaySound _` in the `[]` group.
- `kindName`: `PlaySound _ -> "Play sound"`.
- `defaultOfKind`: `| "Play sound" -> PlaySound ""`.
- `withSong`: `| PlaySound _ -> PlaySound sound` (reused as the op's one-name setter; its doc broadened).
- `drawEditor`'s no-numbers list: `PlaySound _` joins the ops that carry no number.

(`placeOf` needs no arm: it ends in `| _ -> ValueNone`, so a sound already reads as naming no place.)

**Promises**: the op renders, defaults, carries no place and holds nothing — every derived surface.

## Changed — `editor/CueEditor.fs`

- `drawSongCombo` → `drawPickCombo`: takes `names: string list` and a `label: string` instead of reading
  `Assets.musicNames`; the music callers pass `Assets.musicNames assets` and `"Song"`.
- `drawNodeText`: `| PlaySound sound -> drawPickCombo (soundIds assets) "Sound" …` where
  `soundIds assets = Assets.soundAssets assets |> List.map (fun a -> a.Id)`.

**Promises**: the chooser is pick-only over the discovered sound ids (a sound op naming an undiscovered
asset could only no-op), the `drawSongCombo` behaviour for the music ops is unchanged, and choosing a
sound commits `CueTree.withSong`, as choosing a song does.

## Changed — tests

- `test/CueTests.fs`: `describe` gains `PlaySound`; new headless tests (records the id on the pool, the
  cue reaches `Fin`, 0 history steps, unknown/music-class asset is a no-op; the derived label/kind/
  default/`withSong`/`placeOf`).
- `test/CuePanelTests.fs`: `describe` gains `PlaySound`.

## Explicitly unchanged

- `audio/Sound.fs`, `audio/AudioAsset.fs`, `audio/Music.fs`, `audio/Audio.fs` — composed as-is.
- `state/SharedState.fs`, `state/Command.fs`, `state/History.fs` — no new state, no new command; the op
  is a runtime effect (FR-004).
- `editor/SoundPanel.fs` — the audition surface is untouched; a cue play is just another voice.
- `fsharp-game.fsproj`, `native/`, every shader and GPU resource — nothing new is compiled or docked.
