# Data Model: Dialogue Endings

## `world/Dialogue.fs`

### `Ending`

```fsharp
/// One runtime condition that may end a shown line (spec 097). The cases are data-free so a real
/// `Set<Ending>` works. Cue closing is not a member: a close or replacement always works.
type Ending =
    | ByPlayer
    | ByTimeout
```

- `ByPlayer` — the dismiss control (`Space`) may end the line while playing and no text field has
  focus.
- `ByTimeout` — the line ends when `now - ShownAt >= Timeout` in play time.
- The cases are data-free, so structural equality is exact.

### `Dialogue`

```fsharp
[<Struct>]
type Dialogue =
    { /// On screen. The only visibility flag.
      mutable Shown : bool
      mutable Kind : Kind
      mutable Text : string
      /// The runtime conditions that may end the line. Either, both, or neither.
      /// Empty = the cue owns the line's end; a close or replacement always works.
      mutable Endings : Set<Ending>
      /// The duration when `ByTimeout` is in `Endings`; `ValueNone` otherwise.
      mutable Timeout : float voption
      /// Play time at which the line was shown. The timeout is measured from here.
      mutable ShownAt : float }
```

`Dialogue.empty`:

```fsharp
let empty : Dialogue =
    { Shown = false
      Kind = Kind.Dialogue
      Text = ""
      Endings = Set.singleton ByPlayer   // a hand-shown line stays dismissible
      Timeout = ValueNone
      ShownAt = 0.0 }
```

Invariant: the editor and `exposit` keep `ByTimeout ∈ Endings ⇔ Timeout.IsSome`. The runtime treats a
missing duration as no timeout, so an inconsistent hand-authored state cannot crash or fire early.

### Ending helpers

All pure and headlessly testable:

```fsharp
val playerCloses : Set<Ending> -> bool
val timed        : Set<Ending> -> bool

val withPlayer  : bool -> Set<Ending> -> Set<Ending>
val withTimeout : bool -> Set<Ending> -> Set<Ending>

/// "cue" for the empty set, or the " or " join of the present labels:
/// "cue", "player", "3.5s", "player or 3.5s".
val summary : Set<Ending> -> float voption -> string
```

Rules:

- Each `with…` adds or removes exactly one kind; a `Set` never duplicates.
- `withTimeout` changes set membership only; the caller sets `Dialogue.Timeout` to `ValueSome seconds`
  when enabling and `ValueNone` when disabling.
- `summary` includes `"3.5s"` only when `ByTimeout` is in the set and `Timeout = ValueSome 3.5`; it
  joins the parts with `" or "` and returns `"cue"` for the empty set.
- The helpers are editor/state helpers, not per-frame paths. The runtime check uses `Set.contains`
  directly.

## `world/Cue.fs`

```fsharp
/// The existing non-waiting display op. Kept two-argument so persisted cues decode; writes
/// the empty set with `Timeout = ValueNone` (cue-managed).
| Exposit of text: string * kind: Dialogue.Kind
/// A non-waiting display op carrying the ending set and timeout. The editor normalises the empty
/// set with `ValueNone` back to `Exposit`.
| ExpositWith of text: string * kind: Dialogue.Kind * endings: Set<Dialogue.Ending> * timeout: float voption
/// A waiting display op: writes the line and holds the cue until the screen has no line.
| ExpositWait of text: string * kind: Dialogue.Kind * endings: Set<Dialogue.Ending> * timeout: float voption
/// The in-flight form of ExpositWait. The fields are for the cue tree's label; the wait tests Shown.
| ExpositWaitState of text: string * kind: Dialogue.Kind * endings: Set<Dialogue.Ending> * timeout: float voption
/// Clears a shown line. A no-progress leaf.
| CloseExposition
```

Notes:

- `Exposit`, `ExpositWith` and `CloseExposition` hold no run state.
- `ExpositWait` and `ExpositWaitState` are a leaf/state pair, like `Wait`/`WaitState`.
- `ExpositWith(..., Set.empty, ValueNone)` is behaviourally identical to `Exposit`; the editor
  normalises to `Exposit` so the persisted text stays compact.
- `ExpositWait(..., Set.empty, ValueNone)` is the cue-managed waiting line; it holds until a close or a
  replacement's later close clears the screen.
- `CloseExposition` always works, whatever the line's set.

## `state/CueEngine.fs`

### `exposit`

```fsharp
let private exposit
    (now: float)
    (endings: Set<Dialogue.Ending>)
    (timeout: float voption)
    (state: byref<GameplayState.GameplayState>)
    (text: string)
    (kind: Dialogue.Kind) : unit =
    state.Dialogue.Shown <- true
    state.Dialogue.Kind <- kind
    state.Dialogue.Text <- text
    state.Dialogue.Endings <- endings
    state.Dialogue.Timeout <- timeout
    state.Dialogue.ShownAt <- now
```

### `advance`

```fsharp
| Exposit (text, kind) ->
    exposit now Set.empty ValueNone &state text kind
    ValueSome Fin

| ExpositWith (text, kind, endings, timeout) ->
    exposit now endings timeout &state text kind
    ValueSome Fin

| ExpositWait (text, kind, endings, timeout) ->
    exposit now endings timeout &state text kind
    ValueSome (ExpositWaitState(text, kind, endings, timeout))

| ExpositWaitState _ ->
    if state.Dialogue.Shown then ValueNone
    else ValueSome Fin

| CloseExposition ->
    state.Dialogue.Shown <- false
    ValueSome Fin
```

`ExpositWaitState` checks only `Shown`. A replacement that is still shown keeps it waiting; a
`CloseExposition` or a replacement that is later closed lets it continue when its own step observes the
clear. A clear produced by a parallel branch advanced after it is observed on the next tick; either
way the cue resumes within one play frame of the screen becoming clear.

## `world/Systems.fs`

```fsharp
/// End a shown line when one of its runtime conditions holds: `ByPlayer` on the dismiss edge
/// (focus-gated) or `ByTimeout` measured from `ShownAt` in play time. A cue closes or replaces
/// the line outside this tick. Allocation-free: `Set.contains` over at most two elements.
let updateDialogue
    (pressed: bool)
    (textFocused: bool)
    (now: float)
    (dialogue: byref<Dialogue.Dialogue>) : unit
```

Rule:

```fsharp
if dialogue.Shown then
    let elapsed = now - dialogue.ShownAt
    let playerEnds = pressed && not textFocused && Set.contains Dialogue.ByPlayer dialogue.Endings
    let timeoutEnds =
        Set.contains Dialogue.ByTimeout dialogue.Endings &&
        (match dialogue.Timeout with
         | ValueSome seconds -> elapsed >= seconds
         | ValueNone -> false)
    if playerEnds || timeoutEnds then
        dialogue.Shown <- false
```

- The cue is not checked here; it closes or replaces the line.
- `ByTimeout` fires regardless of text focus.
- The empty set never ends here.
- A non-positive timeout fires as soon as `elapsed >= seconds` (the next play tick after it is set;
  `ShownAt` is the show time, so `elapsed` is at least one frame).

## `screen/GameplayScreen.fs`

One play-time value feeds both the dialogue and the cue:

```fsharp
if not state.Editing then
    let playTime = state.PlayTime + float frame.Dt
    do Systems.updateDialogue
        (Input.isPressed Input.Key.Space frame.Input)
        (ImGui.wantTextInput())
        playTime
        &state.Dialogue
    // …player/physics/triggers/camera/animate…
    state.PlayTime <- playTime
    CueEngine.tick shared &state playTime
```

This keeps `ShownAt` (written by a cue's `exposit` with `playTime`) on the same clock as the next
frame's `updateDialogue` and the cue tick. The rest of the play chain and the `PlayTime` freeze while
editing are unchanged.

## State transitions

| Event | `Shown` | `ShownAt` | `Endings` / `Timeout` |
|---|---|---|---|
| `Exposit` / `ExpositWith` reached | `true` | `now` | authored set / voption |
| `ExpositWait` reached | `true` | `now` | authored set / voption |
| `CloseExposition` reached | `false` | unchanged | unchanged |
| `updateDialogue` sees `ByPlayer`/`ByTimeout` | `false` | unchanged | unchanged |
| Dialogue panel ticks Shown on | `true` | `state.PlayTime` | current panel values |
| Dialogue panel ticks Shown off | `false` | unchanged | current panel values |
| Dialogue panel edits an ending | unchanged | unchanged | new set / voption |
| A new display op replaces the line | `true` | `now` | new set / voption |

## Editor defaults and summaries

| Surface | New value | Default |
|---|---|---|
| Cue panel / trigger inline | `Exposit` / `ExpositWith` | empty set, `ValueNone` |
| Cue panel / trigger inline | `ExpositWait` | `{ ByPlayer }`, `ValueNone` |
| Dialogue panel | hand-shown line | `{ ByPlayer }`, `ValueNone` |
| Cue panel / Dialogue panel | timeout first enabled | `ByTimeout` added, `ValueSome 3.0` |

`Dialogue.summary`:

| Endings / timeout | Summary |
|---|---|
| `∅`, `ValueNone` | `cue` |
| `{ ByPlayer }`, `ValueNone` | `player` |
| `{ ByTimeout }`, `ValueSome 3.5` | `3.5s` |
| `{ ByPlayer; ByTimeout }`, `ValueSome 3.5` | `player or 3.5s` |

Cue row labels use the same summary, e.g. `Exposit: hello [cue]`,
`Exposit (wait): hello [player or 3.5s]`, and `Close exposition`.

## Validation rules

- A newly enabled timeout starts at 3.0 seconds; a non-positive value is the runtime's next-tick
  rule, not the editor's default.
- A display node must carry a text and a `Dialogue.Kind`; the existing `withText`/`withExpositKind`
  raise on a node that cannot carry them, and `withEndings` raises on a non-display node.
- `withPlayer`/`withTimeout` are total over ending sets; a `Set` never duplicates.
- The editor keeps `ByTimeout ∈ Endings ⇔ Timeout.IsSome`; the runtime ignores a `ByTimeout` with no
  duration.
- `ExpositWaitState` is never offered a text field (`textOf` returns `ValueNone`) and is read-only in
  the cue tree (`executing` is true).
- A waiting line with the empty set is legal; the editor does not block or warn.
- The Dialogue panel remains direct-write: no command, no undo step, no persistence.
