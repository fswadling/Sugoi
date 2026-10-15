# Phase 1 — Data Model: Sound Cues — play a sound from the story

The whole feature is a single value: one case on the closed cue vocabulary. Nothing is authored,
persisted, or held across frames, and no existing type changes.

## The op — one closed-vocabulary case

```fsharp
type Cue =
    | ...
    | PlaySound of sound: string   // NEW (spec 090): play a named sound one-shot where the run reaches it
```

- **`sound`**: the asset **id** of a sound asset (spec 086's `Kind = Sound`) — the same addressing a
  music op uses for its song. An id no manifest declared, a music-class asset, or a failed asset
  resolves to nothing (validation below).
- **No `…State` case.** The vocabulary's convention is that a `…State` case exists exactly where the run
  form holds something the authored form does not (a deadline, captured weights, a resolved handle). A
  one-shot is not held: `PlaySound` performs its effect and the cue advances in the same step, so there
  is nothing to capture — `PlaySound` sits with `AddActor`/`PlayMusic` among the no-progress leaves.

## Validation rules

- **Resolve a resident sound.** The engine looks the id up in the asset manager and requires
  `Class = Sound` **and** `isResident` (spec 086/089). Absent, non-sound or failed resolves to `None`.
- **A no-op is reported.** An unresolvable id is a reported no-op (`cue: PlaySound(<id>) performs
  nothing: its asset is unavailable`), never a crash — the music-op rule (spec 088 FR-010).
- **The handle is read only where it is present.** `AudioAsset.sound` (which raises on a non-resident
  asset) is called only on the `Some` branch, so the op can never read a freed handle.
- **Nothing is authored.** Reaching the op writes no command, no undo step, no zone and no editor state
  (FR-004); its whole effect is the `Sound.play` call.

## The runtime it drives — unchanged

`PlaySound` composes spec 089 as it stands: `Sound.play shared.Voices sound handle` takes the next
voice in the pool's round-robin (the oldest reused), points it at the resident buffer, plays it from the
top, and records the asset id. Repeated reaches overlap up to the pool's bound (`Sound.capacity`), the
oldest cut — nothing queues and nothing blocks (FR-003/FR-007).

## No other state

There is no palette, no per-sound level, no `SharedState` field and no persistence. The op is a leaf of
the run and a call into an existing module; the only new data is the `sound: string` it carries.

## The editor's view (derived, not stored)

The cue editor derives the op's row from the vocabulary, as it does for every op (Constitution N-III):

| Surface | Value |
|---|---|
| `CueTree.label` | `Play sound: <id>` (`Play sound` when the id is empty) |
| `CueTree.kindName` / `defaultOfKind` | `"Play sound"` → `PlaySound ""` |
| `CueTree.children` | `[]` (a leaf) |
| `CueTree.executing` | `false` (it never holds) |
| `CueTree.placeOf` | `ValueNone` (a sound is global, like a song — it falls to the `| _` wildcard, so no arm is needed) |
| `CueTree.withSong` | the op's one-name setter: `PlaySound id` |
| `CueEditor.drawNodeText` | a `Play sound` pick-combo over the discovered sound ids |
