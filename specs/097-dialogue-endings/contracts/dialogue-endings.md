# Contract: Dialogue Endings

This feature has no external API; its contract is the cue vocabulary, the runtime state and system
functions, and the shared editor control. The signatures below are the seams the tests and the plan
rely on.

## `world/Dialogue.fs`

```fsharp
type Ending =
    | ByPlayer
    | ByTimeout

[<Struct>]
type Dialogue =
    { mutable Shown : bool
      mutable Kind : Kind
      mutable Text : string
      mutable Endings : Set<Ending>
      mutable Timeout : float voption
      mutable ShownAt : float }

val empty : Dialogue

val playerCloses : Set<Ending> -> bool
val timed : Set<Ending> -> bool

val withPlayer : bool -> Set<Ending> -> Set<Ending>
val withTimeout : bool -> Set<Ending> -> Set<Ending>

val summary : Set<Ending> -> float voption -> string
```

Guarantees:

- The conditions are data-free; a `Set<Ending>` is exact.
- Cue closing is universal; the empty set means the cue owns the line's end, not that the line cannot
  be closed.
- `ByTimeout ∈ Endings ⇔ Timeout.IsSome` is maintained by the editor and `exposit`.
- `summary` returns `"cue"` for the empty set, or the `" or "` join of the present labels
  (`"player"`, `"3.5s"`, and their combination).
- `empty` is not shown; its set is `{ ByPlayer }` and its timeout is `ValueNone`, so a line shown by
  hand stays dismissible.

## `world/Cue.fs`

```fsharp
| Exposit of text: string * kind: Dialogue.Kind
| ExpositWith of text: string * kind: Dialogue.Kind * endings: Set<Dialogue.Ending> * timeout: float voption
| ExpositWait of text: string * kind: Dialogue.Kind * endings: Set<Dialogue.Ending> * timeout: float voption
| ExpositWaitState of text: string * kind: Dialogue.Kind * endings: Set<Dialogue.Ending> * timeout: float voption
| CloseExposition
```

- `Exposit` is the existing empty-set, non-waiting form and stays two-argument.
- `ExpositWith` is the general non-waiting form; `ExpositWith(..., Set.empty, ValueNone)` is
  equivalent to `Exposit`.
- `ExpositWait` / `ExpositWaitState` are the waiting leaf/state pair.
- `CloseExposition` is a no-progress leaf and always clears a shown line.
- No other cue op changes.

## `state/CueEngine.fs`

`advance` performs:

| Cue value | Effect | Answer |
|---|---|---|
| `Exposit (text, kind)` | writes line with the empty set, `ValueNone`, `ShownAt = now` | `ValueSome Fin` |
| `ExpositWith (text, kind, endings, timeout)` | writes those values, `ShownAt = now` | `ValueSome Fin` |
| `ExpositWait (text, kind, endings, timeout)` | writes those values, `ShownAt = now` | `ValueSome (ExpositWaitState(...))` |
| `ExpositWaitState _` | reads `state.Dialogue.Shown` | `ValueNone` while shown; `ValueSome Fin` when clear |
| `CloseExposition` | `state.Dialogue.Shown <- false` | `ValueSome Fin` |

Guarantees:

- The waiting state does not allocate while waiting and does not inspect the ending set or timeout.
- A waiting cue advances in the same tick when its own step observes the clear. A clear produced by a
  parallel branch advanced after it is observed on the next tick; either way it resumes within one play
  frame of the screen becoming clear.
- A non-waiting display op costs one cue step.
- The timer is not owned by the cue: a non-waiting timed line clears itself even when the cue is `Fin`.

## `world/Systems.fs`

```fsharp
val updateDialogue :
    pressed: bool -> textFocused: bool -> now: float ->
    byref<Dialogue.Dialogue> -> unit
```

Guarantees:

- No-op when `Shown` is false.
- Ends the line when:
  - `ByPlayer ∈ Endings` and `pressed && not textFocused`;
  - `ByTimeout ∈ Endings` and `Timeout = ValueSome seconds` and `now - ShownAt >= seconds`.
- The cue is not a per-frame condition; it closes or replaces the line.
- The empty set never ends here.
- A timeout is unaffected by text focus.
- The implementation allocates nothing per call.
- `dismissDialogue` is removed; no other system changes.

## `screen/GameplayScreen.fs`

The play block computes `playTime = state.PlayTime + float frame.Dt` once, passes it to
`Systems.updateDialogue`, and assigns it to `state.PlayTime` before `CueEngine.tick`. The dialogue and
the cue therefore share one clock. The call remains inside `if not state.Editing` and keeps the
`ImGui.wantTextInput()` focus gate.

## `editor/CueTree.fs`

```fsharp
val withEndings : Set<Dialogue.Ending> -> float voption -> Cue -> Cue
val endingControls : string -> Set<Dialogue.Ending> -> float voption ->
    Set<Dialogue.Ending> * float voption
```

Guarantees:

- `withEndings` handles `Exposit`, `ExpositWith` and `ExpositWait`; it normalises the empty set with
  `ValueNone` to `Exposit` for the non-waiting pair and raises on a non-display node.
- `endingControls` returns the next set and timeout; it does not commit. It draws "Player can close"
  and "Closes after" with a seconds field, using a unique id suffix. A newly enabled timeout starts at
  3.0 seconds. The cue's ability to close or replace a line is stated as unconditional.
- `drawEditor` calls `endingControls` for both display forms and commits through `withEndings`, so one
  accepted edit is one `SetCue` step.
- `textOf`/`withText` handle `Exposit`, `ExpositWith` and `ExpositWait`; `ExpositWaitState` is excluded
  so a running op has no editable text field.
- `withExpositKind` handles the same three display forms.
- `kindName`: `Exposit` for `Exposit` and `ExpositWith`; `Exposit (wait)` for `ExpositWait` and
  `ExpositWaitState`; `Close exposition` for `CloseExposition`.
- `label`: includes `Dialogue.summary`, e.g. `Exposit: text [cue]`,
  `Exposit (wait): text [player or 3.5s]`, `Close exposition`.
- `executing`: true for `ExpositWaitState`; false for `Exposit`, `ExpositWith`, `ExpositWait`,
  `CloseExposition`.
- `children`: `[]` for all new leaves.
- `defaultOfKind`: `Exposit` → `Exposit("", Kind.Dialogue)`; `Exposit (wait)` →
  `ExpositWait("", Kind.Dialogue, Set.singleton ByPlayer, ValueNone)`; `Close exposition` → `CloseExposition`.
- A waiting line with the empty set remains selectable; no warning or disable is added.

## `editor/DialoguePanel.fs`

- The panel draws the same ending controls through `CueTree.endingControls "dialogue"`.
- Ticking `Shown` from off to on sets `dialogue.ShownAt <- state.PlayTime`.
- A newly enabled timeout starts at 3.0 seconds.
- Editing the ending set does not reset `ShownAt`; a timeout retimes from the original show time.
- The panel writes `state.Dialogue` directly: no command, no undo step, no persistence.

## `editor/CueEditor.fs`

No structural change: `drawNodeText` calls `CueTree.textOf`/`withText`, which now accept the waiting
display form. The screen Cue panel and the trigger's inline cue editor both use `CueTree.drawEditor`
and therefore both get the ending controls. The Scenario panel only captures the screen's cue and
needs no change.

## `world/Project.fs`

No DTO change. The cue is carried as-is, so `Exposit` keeps decoding and the new cases encode/decode
through Prime's symbolic converter. The project version stays `1`. The round-trip contract is
asserted in `test/ProjectTests.fs`; it must cover `Set<Ending>` and `float voption`.

## Tests

- `DialogueTests.fs`: `updateDialogue` input/timeout rules, focus gate, empty set, retime, a
  `ByTimeout` with `Timeout = ValueNone`, portrait miss, helpers, zero-byte ticks.
- `CueTests.fs`: non-waiting/waiting/close behaviour, an empty-set waiting line, replacement, a parallel
  close before and after the waiting branch, `CloseExposition` on `{ ByPlayer }` and `{ ByTimeout }` lines,
  timer independence, zero-byte idle ticks and no-line calls.
- `CuePanelTests.fs`: kinds/defaults/labels/executing/children, `withEndings` round-trips, and the
  one-step commit of an ending edit.
- `ProjectTests.fs`: old `Exposit` decodes as the empty set; new cases and the set/voption round-trip.
