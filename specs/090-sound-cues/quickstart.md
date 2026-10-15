# Quickstart: Play a sound from the story

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Contract**: [contracts/sound-cue-op.md](./contracts/sound-cue-op.md)

How to prove the feature works: the headless suite (which asserts the op's *logic* — it plays the named
sound on the voice pool, reaches `Fin`, records no step, and no-ops on an unresolvable id), then a device
walkthrough (which shows the sound sounding where the run reaches the op). Audio output cannot be
asserted headlessly, so it is heard here — the spec-086 split.

## Prerequisites

- `dotnet build` is clean.
- A working audio device (or `SDL_AUDIODRIVER=dummy` to run the walkthrough silently).
- At least one sound asset in `assets/audio/sounds` (spec 086), so the cue editor's chooser has a sound
  to pick.

## 1. Headless validation (no device)

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~CueTests"   # the cue engine, including the new sound op
dotnet test                                           # the whole suite stays green
```

Expected:

- A cue holding `PlaySound "beep"`, with a **resident** sound asset named `beep`, ticks to `Fin` and
  leaves `shared.Voices.Playing.[0] = "beep"` — the pool recorded the one-shot (FR-001/FR-002).
- The same tick adds **0** steps to `state.Editor.Undo.Applied` (FR-004/SC-002).
- `PlaySound` naming an id no manifest declared, a **music-class** asset, or a **failed** sound ticks to
  `Fin` with nothing recorded and nothing raised (FR-005/US3).
- `CueTree` derives the op: `label` is `Play sound: beep` (`Play sound` when unnamed), `kindName` is
  `"Play sound"`, `defaultOfKind "Island" "Play sound"` is `PlaySound ""`, `withSong "x"` replaces the
  id, and `placeOf` is `None` (a sound is global) — FR-006/SC-004.
- The existing suite (including specs 086–089's audio tests) stays green.

## 2. Walkthrough A — author and hear a sound (US1/US2)

```bash
dotnet run
# press ` (backtick) to enter the editor; open the "Cue" panel
```

- In the cue editor, add an op and choose **Play sound** from the kind list → a `Play sound` row appears
  with a chooser of the discovered sounds (US2/AC1).
- Pick a sound from the chooser → the row reads `Play sound: <name>` and re-opening the cue shows it
  still selected (US2/AC2).
- Press play (or trigger the cue) → the sound **sounds once** where the run reaches the row, and the cue
  **carries on at once** — it does not pause waiting (US1/AC1, US1/AC2, FR-003).
- Re-run the cue → the sound sounds again as a fresh one-shot (US1/AC3), overlapping a still-sounding
  earlier instance up to the pool's bound (spec 089).
- Watch the Edit menu → Undo stays disabled: the op recorded **no** history step (US1/AC4, FR-004).

## 3. Walkthrough B — failure and unavailability (US3)

- Author a `Play sound` op naming a sound whose manifest points at a missing file (or corrupt it), or an
  id that does not exist, and run with `FFS_AUDIO_DEBUG=1 dotnet run` → the console reports
  `cue: PlaySound(<id>) performs nothing: its asset is unavailable`; the cue continues (FR-005).
- Run with the audio subsystem unavailable:

  ```bash
  SDL_AUDIODRIVER=does-not-exist dotnet run
  ```

  → audio reports unavailable, every sound is `failed`, a `Play sound` op is a no-op, and the story
  still runs (US3/AC2).
- The positive counterpart is `SDL_AUDIODRIVER=dummy`: audio starts on a silent device and Walkthrough A
  passes with nothing audible.

## Cleanup

Nothing to clean: the walkthrough uses the checked-in (gitignored) `assets/audio` library, and no
manifest is edited except the deliberate corruption in section 3 (revert it, or `git checkout` the file).

## What this does not cover (deferred)

A sound palette, per-sound volume, looping, start/end points, cross-fade, ducking and spatialisation —
and a stop form (a cue silencing a sound) — remain out of scope (spec FR-008/FR-009). The Sounds panel's
Play/Stop/Stop all (spec 089) remain the manual way to audition and silence a sound.
