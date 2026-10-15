# Quickstart: Music cues — play and shape music from the story

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Contract**: [contracts/cue-music.md](./contracts/cue-music.md)

How to prove the feature works: the headless suite (which asserts the cue ops' *effects* on the mix — palette
membership, the levels a ramp lands on, the no-op rules, the 0-step property), then a device walkthrough
(which *hears* the palettes, transports and fades the story drives). Audio output cannot be asserted
headlessly, so it is heard here — the spec-086/087 split, applied to the cue ops.

## Prerequisites

- `dotnet build` is clean.
- A working audio device (or `SDL_AUDIODRIVER=dummy` to run the walkthrough silently).
- The shipped audio library: `assets/audio/music` (42 tracks, e.g. `Casual1.mp3`, `Dark1.mp3`) and
  `assets/audio/sounds`. Each file has a manifest beside it, so the manager discovers them.
- The cue is authored and run in one place (spec 046): the **Cue** panel edits `state.Cue` itself, and the cue
  ticks only on play time — the clock is frozen while the editor is open. So: author the ops, close the editor
  (backtick), and the run reaches them on the next play ticks.

## 1. Headless validation (no device)

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~MusicCueTests"   # the new, device-free cue-op tests
dotnet test                                               # the whole suite stays green
```

Expected:

- `MusicCueTests` pass, against a device-free `SharedState` (audio `unavailable`), with no device opened:
  - `AddMusic` makes a row (and a re-add is a no-op); `RemoveMusic` drops it (and removing an absent song is a
    no-op); the mix the op leaves is the mix the Songs panel would show — FR-001/FR-002.
  - `PlayBed`/`StopBed` reach every row; `PlayMusic`/`StopMusic` reach one.
  - A level op with a duration lands on its target at the deadline (± one tick) and a duration of 0 lands in
    the same frame; the row's weight *is* the value — FR-006/SC-002.
  - A master op moves the mix's `Master` — FR-007.
  - A cross-fade lowers `from` to 0 and raises `to` to 1 together, landing exactly at the deadline; a missing
    side is a reported no-op and the other side still moves — FR-008, US3/AC3.
  - A retarget reached mid-ramp continues from the current level (no jump) — FR-012.
  - A song that is not a palette row, and a song whose asset is unavailable, are reported no-ops; nothing
    raises — FR-010.
  - **0 steps**: after a cue containing music ops, `state.Editor.Undo.Applied` is empty — while the equivalent
    editor action (a `SetMix`) is exactly one step — FR-009/SC-003.
- The existing suite (including `MusicTests`, `CueTests`, `CuePanelTests`) is green. The only existing lines
  changed are the two cue `describe` matches and the renamed cross-fade labels — no behaviour is loosened
  (FR-016).

## 2. Walkthrough A — a cue sets up the palette, plays, stops and removes (US1)

```bash
dotnet run
# press ` (backtick) to enter the editor; open the "Cue" tab in the bottom dock
```

Build a cue (the root is a `Sequence`; use the **Kind** dropdown's **Music** group and **Add Child**):

```text
Sequence
├── Add music: casual1
├── Play music: casual1
├── Wait 4
├── Stop music: casual1
└── Remove music: casual1
```

- With the editor open, pick `Casual 1` in **Add music**'s song chooser → the row reads `Add music: casual1`.
- Close the editor (backtick) to play.
- **Hear**: `casual1` starts from its beginning at the `Play music` op, plays for 4 seconds, then halts and
  rewinds at `Stop music`, and leaves the palette at `Remove music` — US1/AC1..AC4.
- Open the Songs panel (the editor) while it plays → the row appears and disappears as the cue adds and removes
  it; the two writers share one mix — US1/SC-005.

## 3. Walkthrough B — a cue shapes a level over time (US2)

```text
Sequence
├── Add music: dark1
├── Play music: dark1
├── Music level: dark1   (level 0.0, seconds 3)
├── Wait 1
├── Music level: dark1   (level 1.0, seconds 3)
└── Music master         (level 0.0, seconds 3)
```

- `dark1` starts at full, **fades out** over 3 seconds, comes back up over 3 seconds (a retarget reached while
  the fade-out was in flight — it continues from wherever the level was, no jump), then the whole bed fades out
  as the master ramps to 0 — US2/AC1, FR-012.
- Set a level op's **Seconds** to `0` → the level changes in the same frame (the instant form) — US2/AC2.
- Set a level *before* the song plays (a `Music level` op ahead of `Play music`) → the song sounds at that level
  when it starts — US2/AC3.

## 4. Walkthrough C — a cue cross-fades between two songs (US3)

```text
Sequence
├── Add music: casual1
├── Add music: dark1
├── Music level: dark1   (level 0.0, seconds 0)
├── Play music: casual1
├── Play music: dark1
├── Wait 2
└── Cross fade music: casual1 -> dark1   (seconds 4)
```

Both songs run from the start (the incoming one silently, at level 0). At the cross-fade op, `casual1` falls to
silence and `dark1` rises to full together over 4 seconds — a clean swap — US3/AC1.

- Set the cross-fade's **Seconds** to `0` → the swap happens at once — US3/AC2.
- Change the cross-fade's outgoing song to a name that is not in the palette → the run reports that side as a
  no-op on stderr and the incoming song still moves — US3/AC3.

## 5. Walkthrough D — a cue's change is not an undoable edit (FR-009/SC-003)

- Author a `Music master` op (or run Walkthrough B) and let it run. Open the editor's **Edit** menu → Undo is
  **disabled**: the cue recorded no step.
- Now move a **Weight** slider in the Songs panel → Undo enables for exactly one step; undo restores the prior
  weight — the editor's `SetMix` is still the authoring path (SC-003).
- Add a song from a cue, then press Ctrl+Z → the cue's row is **not** removed (it is not on the stack, like an
  advent a cue added) — the edge case.

## 6. Walkthrough E — unavailability (FR-010/SC-004)

- Point a track's manifest `File` at a missing path (or corrupt it) and restart with `FFS_AUDIO_DEBUG=1
  dotnet run` → that asset reports `failed`; a cue naming it does nothing (reported), the story runs, and the
  Songs panel still draws.
- Run with the audio subsystem unavailable:

  ```bash
  SDL_AUDIODRIVER=does-not-exist dotnet run
  ```

  → every music asset is `failed`, every cue music op is a no-op, and the run reaches `Fin` with nothing
  audible and no crash — FR-010/SC-004.

- The positive counterpart is `SDL_AUDIODRIVER=dummy`: audio starts on a silent device and Walkthroughs A–C
  run with nothing audible.

## Cleanup

Nothing to clean: the walkthrough uses the checked-in (gitignored) `assets/audio` library, and no manifest is
edited except the deliberate corruption in section 6 (revert it, or `git checkout` the file).

## What this does not cover (later features)

One-shot sounds (`PlaySound`), start points, loop points, ducking/sidechain, spatialisation and DSP are out of
scope (FR-015). This phase drives the music bed from the story; it does not play a sound from a cue, and it does
not persist a cue or the mix to disk (that lands with the planned `world/Project.fs`).
