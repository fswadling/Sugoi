# Quickstart: Play one-shot sounds and audition them from a sound panel

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Contract**: [contracts/sound-panel.md](./contracts/sound-panel.md)

How to prove the feature works: the headless suite (which asserts the voice pool's *logic* — round-robin
reuse, the per-asset Stop, Stop all, and the readout), then a device walkthrough (which shows the real
one-shots playing, overlapping and stopping). Audio output cannot be asserted headlessly, so it is heard
here — the spec-086 split.

## Prerequisites

- `dotnet build` is clean.
- A working audio device (or `SDL_AUDIODRIVER=dummy` to run the walkthrough silently).
- The shipped sound library: `assets/audio/sounds` (spec 086), each file with a manifest beside it, so
  the manager discovers and decodes it at startup.

## 1. Headless validation (no device)

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~SoundTests"   # the new, device-free pool tests
dotnet test                                             # the whole suite stays green
```

Expected:

- `SoundTests` pass, against a `Voices` built on `Audio.unavailable` (no device opened):
  - the pool has `capacity` tracks; a fresh pool has every record `""` and cursor `0`.
  - `play voices "door" handle` records `"door"` on voice 0 and moves the cursor to 1; playing four
    different sounds fills four voices in order (round-robin) — FR-001, D3.
  - playing more than `capacity` sounds reuses the least-recently-used voice (the cursor wraps), so the
    pool never grows — FR-003/SC-003.
  - `stop voices "door"` clears only the voices recorded `"door"`, leaving other sounds recorded —
    FR-004/SC-007.
  - `stopAll voices` clears every record — FR-005.
  - `release context voices` clears every record; with `Audio.unavailable` it raises nothing.
  - `Assets.soundAssets` returns only `Kind = Sound` assets, sorted by name, with their residency, from a
    temp root and a recording fake context (as `AudioTests` does) — FR-004/FR-007.
- The existing suite (actor/prop/environment/editor/gameplay, and specs 086/087's audio tests) is
  unchanged and green apart from the `SharedState` fixtures gaining `Voices` — the pool is additive.

## 2. Walkthrough A — audition a sound (US1)

The editor's `Sounds` panel is a bottom-docked tab, beside `Songs`, the Asset Browser and the Dialogue.
The lines below are what you *do*; the plain lines are what you should *hear/see*.

```bash
dotnet run
# press ` (backtick) to enter the editor; click the "Sounds" tab in the bottom dock
```

- In the panel: each discovered sound is listed by name, with a `Play` and a `Stop` control → the list
  is every `Kind = Sound` asset (US1/AC1).
- Press a sound's **Play** → it sounds once from its beginning (US1/AC2).
- Press that sound's **Stop** → it is silenced (US1/AC3).
- Press the panel's **Stop all** → every sounding one-shot is silenced (US1/AC4).
- With no sounds discovered (`assets/audio/sounds` empty), the panel shows an empty placeholder and the
  game runs (US1/AC5).

## 3. Walkthrough B — one-shot semantics (US2)

- Press a short sound's **Play** → it stops on its own, with no further input (US2/AC1, FR-001).
- Press **Play** again immediately, before it finishes → the second instance **overlaps** the first
  (US2/AC2, FR-003).
- Press one sound's **Play** many times very fast → instances overlap up to the pool's capacity; past it
  the oldest is cut and the newest sounds, and nothing crashes or queues (US2/AC3, SC-003).
- **Stop** one sound while another is still sounding → only that sound is silenced; the other keeps
  sounding (SC-007).
- Play a long sound effect → it still sounds once and **stops at its end**: there is no loop and no
  scrub (a long, loopable thing is music — FR-010).

## 4. Walkthrough C — the panel is runtime-only (FR-006)

- Play and stop several sounds, then **Edit → Undo** → it is disabled: the panel records **no** step on
  the one history (SC-004). (If a mix weight was changed first, Undo undoes *that*, not any audition.)
- Audition a sound, then press **Escape** to the main menu and re-enter → the panel is exactly as it
  was; nothing was persisted (there is nothing to persist — FR-006).

## 5. Walkthrough D — failure and unavailability (FR-007/FR-008)

- Point a sound's manifest `File` at a missing path (or corrupt the file) and restart with
  `FFS_AUDIO_DEBUG=1 dotnet run` → that asset reports `failed`; it is still listed, marked, and pressing
  its **Play** is a no-op (reported, no crash) — FR-007.
- Run with the audio subsystem unavailable:

  ```bash
  SDL_AUDIODRIVER=does-not-exist dotnet run
  ```

  → audio reports unavailable, every sound is `failed`, the `Sounds` panel still draws and the game
  runs; every Play/Stop is a no-op — FR-008, SC-005.
- The positive counterpart is `SDL_AUDIODRIVER=dummy`: audio starts on a silent device, the panel
  behaves normally, and Walkthroughs A–C pass with nothing audible.

## Cleanup

Nothing to clean: the walkthrough uses the checked-in (gitignored) `assets/audio` library, and no
manifest is edited except the deliberate corruption in section 5 (revert it, or `git checkout` the file).

## What this does not cover (later features)

Triggering a sound from the story (a sound **cue op**, the analogue of spec 088's music ops), looping a
sound, start/end points, groups/tags/buses, ducking, crossfade and spatialisation are out of scope
(spec FR-010). This phase plays a one-shot from the panel (and provides the facility the game will
call); it does not play a sound from a cue, and it persists nothing.
