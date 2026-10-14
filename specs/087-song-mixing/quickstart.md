# Quickstart: Play and mix music from a song panel

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Contract**: [contracts/song-panel.md](./contracts/song-panel.md)

How to prove the feature works: the headless suite (which asserts the mix *logic* — gain, add/remove,
the start rule), then a device walkthrough (which shows the real SDL decks playing, mixing, scrubbing
and looping). Audio output cannot be asserted headlessly, so it is heard here — the spec-086 split.

## Prerequisites

- `dotnet build` is clean.
- A working audio device (or `SDL_AUDIODRIVER=dummy` to run the walkthrough silently).
- The shipped audio library: `assets/audio/music` (42 tracks, e.g. `Casual1.mp3`, `Dark1.mp3`) and
  `assets/audio/sounds` (spec 086). Each file has a manifest beside it, so the manager discovers them.

## 1. Headless validation (no device)

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~MusicTests"   # the new, device-free mix tests
dotnet test                                            # the whole suite stays green
```

Expected:

- `MusicTests` pass, against the pure `Music` model with no device opened:
  - `gainOf m w` is the product: `gainOf 1.0 0.5 = 0.5`, `gainOf 0.5 1.0 = 0.5`, `gainOf 0.0 1.0 = 0.0`.
  - `addRow` adds one row; adding the same asset again **does not** add a second (`empty |> addRow
    "dark1" |> addRow "dark1"` has one row) — FR-002.
  - `removeRow` drops the row; removing an absent asset leaves the mix unchanged.
  - `setWeight`/`setLoop`/`setMaster` change only the row (or the one master) they name, and leave the
    rest of the mix identity-equal.
  - `startOf true = Resume` and `startOf false = Restart` — the play rule (D5): a paused row resumes,
    any other restarts from 0.
- The existing suite (actor/prop/environment/editor/gameplay, and spec 086's `AudioTests`) is unchanged
  and green — the mix is additive and touches no existing load path.

## 2. Walkthrough A — play one song (US1)

The editor's `Songs` panel is a bottom-docked tab, beside the Asset Browser and the Dialogue. The lines
below are what you *do*; the plain lines are what you should *hear/see*.

```bash
dotnet run
# press ` (backtick) to enter the editor; click the "Songs" tab in the bottom dock
```

- In the panel: pick `Casual 1` from the song picker and press **Add** → one row appears, stopped. (The
  pick closes the dropdown, so **Add** is reachable.)
- Press the row's **Play** → the track plays from its beginning.
- Press **Pause** → it holds; the scrub stops advancing.
- Press **Play** again → it resumes from where it was held (not from the top) — US1/AC2.
- Press **Stop** → it halts and the scrub jumps back to 0 — US1/AC3.
- Press **Play** once more → it plays from the beginning (a fresh start, not a resume) — US1/AC1, D5.

## 3. Walkthrough B — mix several songs (US2)

- Add two very different tracks, e.g. `Casual 1` and `Dark 1`.
- Press each row's **Play** → both play **at once** (FR-005).
- Drag `Casual 1`'s **Weight** down → only `Casual 1` gets quieter; `Dark 1` is unchanged — FR-006/SC-002.
- Drag the **Master** slider down → **both** get quieter together — FR-007/SC-002.
- Set the Master to `0.00` → the mix is silent (0 audible above the master) — SC-002.
- Add `Casual 1` again → no second row appears — FR-002.
- Press the bed **Play all** (top of the panel) → every row resumes from its own position (not from 0);
  press **Hold all** → every row **holds** its position — FR-004, clarification.
- Undo / redo (Ctrl+Z / Ctrl+Y) a weight or master move → the level steps exactly one adjustment at a
  time, and a transport press or a scrub **never** creates a step — FR-010/FR-011/SC-004. Watched in the
  Edit menu: Undo/Redo enable only after an authored change, not after pressing play.
- With a song playing, press **Escape** to return to the main menu → the music **keeps playing** across the
  transition; re-enter gameplay and the panel still shows it playing. The mix is game-wide and
  screen-independent (spec assumption; research D7) — a music bed plays *over* transitions.

## 4. Walkthrough C — scrub and loop (US3)

- With a track playing, **drag its scrub bar** → the position jumps and playback continues from the new
  point; the bar keeps advancing afterwards — FR-008/SC-003. (The scrub appears once a row has a deck,
  i.e. after its first **Play** — the duration is unknown before then, so a fresh row shows `(play to scrub)`.)
- Pause the track, **drag the scrub**, then press **Play** → it plays from the scrubbed position (the pause
  held the new point) — edge case.
- On a short track, tick **Loop** → at the end it repeats from the beginning; untick it → at the end it
  stops (the scrub sits at the end and ▶ restarts it) — FR-009/SC-003.
- Toggle **Loop** on a *playing* row → it takes effect without restarting the track — FR-009.

## 5. Walkthrough D — removal and unavailability (FR-012/FR-013)

- Remove a **playing** row (**Remove**) → it stops and its playback resources are released; the other
  rows keep playing.
- Point a track's manifest `File` at a missing path (or corrupt the file) and restart with
  `FFS_AUDIO_DEBUG=1 dotnet run` → that asset reports `failed`; adding and playing it is a no-op
  (reported, no crash), and the panel still draws — FR-013, edge case.
- Run with the audio subsystem unavailable:

  ```bash
  SDL_AUDIODRIVER=does-not-exist dotnet run
  ```

  → audio reports unavailable, every audio asset is `failed`, the `Songs` panel still draws and the game
  runs; every transport/scrub op is a no-op — FR-013, SC-005.

- The positive counterpart is `SDL_AUDIODRIVER=dummy`: audio starts on a silent device, the panel behaves
  normally, and Walkthroughs A–C pass with nothing audible.

## Cleanup

Nothing to clean: the walkthrough uses the checked-in (gitignored) `assets/audio` library, and no
manifest is edited except the deliberate corruption in section 5 (revert it, or `git checkout` the file).

## What this does not cover (later features)

One-shot sounds and cue-triggered playback (`PlaySound`/`PlayMusic`), start points, loop points,
crossfade, ducking and spatialisation are out of scope (spec FR-015). This phase plays and mixes music
from the panel only; it does not play a sound from the story, and it does not persist the mix to disk
(that lands with the planned `world/Project.fs`).
