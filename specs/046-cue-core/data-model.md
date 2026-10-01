# Data Model: Cue Core

## `world/Cue.fs` — new

### One type: the cue *and* its run

```fsharp
type Cue =
    // the terminal, and the fixed point: advancing it is itself
    | Fin
    // control flow. `current` is the op in flight (or the next to run); `rest` is the queue not
    // yet started, held as cue values. The running op is held OUT of the deque on purpose — see
    // "Why current is not in the deque" below.
    | Sequence of current: Cue * rest: Cue FDeque
    | Parallel of left: Cue * right: Cue   // `Fin` on a side means that side is done
    | Fork of inner: Cue                   // "run beside the rest of my sequence"; only legal at a
                                           //   Sequence's `current`
    // the first cut of leaves (research D1). Each proves one shape, and a timed leaf carries its
    // run state in its own `…State` case:
    | Wait of seconds: float                       // progress is a deadline
    | WaitState of seconds: float * endsAt: float  //   — in flight, with the deadline it computed
    | Exposit of text: string                      // no progress of its own
    | SwitchZone of zone: string                   // changes the game's place
    | MoveCamera of target: CameraPose * duration: float                   // more than a deadline
    | MoveCameraState of from: CameraPose * target: CameraPose * duration: float * endsAt: float
```

- **`CameraPose`** is `{ Position: Vector3; Target: Vector3 }` — a struct, and deliberately *not*
  `render/Camera.fs`'s `Camera`. A leaf carries the op's own numbers; naming the renderer's type here would make
  the vocabulary depend on the render path for no gain, and the engine maps a pose onto the camera in one line.
- **A `…State` case appears exactly where a leaf's run form holds numbers the authored form does not** — the
  `My Game 3` naming convention (`wait` / `waitstate`), and the same rule as FR-017's "a leaf op and its own state
  case". `Wait` → `WaitState` (a deadline); `MoveCamera` → `MoveCameraState` (where it started). `Exposit`,
  `SwitchZone` and `Fin` have one case each, because their in-flight form holds nothing extra.
- **The control-flow ops have no `…State` sibling.** A running sequence's state *is* its structure (`current` +
  `rest`), and `Parallel`/`Fork` are the same shape running as authored. Giving a sequence a separate
  `SequenceState` would be a template/runtime split in miniature — the very split this model collapses. A
  running parallel does show `Fin` on a side that has finished; that is the same case, not a second one.
- **`Fin` is a value, not an absence.** "Nothing left to run" and "not yet anything" are the same value, and no
  flag (`CueStarted`, `Run : Frame voption`) is needed to tell "begun" from "finished": begun is "the cue is no
  longer the value that was set", finished is "the cue is `Fin`".
- **Invariants**: the value is immutable — advancing it returns a new value and modifies nothing, so a holder can
  run it again from the same value (FR-001); a `Sequence`'s `current` is never `Fin` in a *stored* value (the walk
  drops a finished front and pulls the next in the same step), so `Sequence(Fin, rest)` exists only within a step.

### Why `current` is not in the deque

The most literal reading of "the cue is the run" is `Sequence of children: Cue FDeque`, with the running op at the
front of the deque. It was drawn, implemented and **measured**: it costs **72 bytes per idle tick at every level
of nesting** (144000 B over 2000 ticks flat, 432000 B three deep). Stepping the front means `FDeque.uncons` on the
*idle* path, and `uncons` allocates the tail it returns — a cost paid every frame whether or not anything moved.
Holding the op in flight out of the deque removes it: the idle path then touches no deque at all and reports
nothing to change, and the suite measures 0 bytes.

## `world/CueEngine.fs` — new

```fsharp
val advance  : shared: SharedState.SharedState -> state: byref<GameplayState.GameplayState>
               -> now: float -> Cue -> Cue voption
val tick     : shared: SharedState.SharedState -> state: byref<GameplayState.GameplayState>
               -> now: float -> unit
val forkInto : joined: Cue -> current: Cue -> Cue
```

- **`advance` is the whole engine**: one cue in, the cue as it now stands out — or nothing to change —
  performing each op's effect where the walk
  reaches it (a `SwitchZone` reuses `GameplayState.switchZone`, which is why it needs `shared`; an `Exposit`
  writes `state.Dialogue`; a `MoveCamera` writes `state.Camera`). That coupling is why the module is compiled
  **after** `state/GameplayState.fs` (research D3).
- **`tick` lifts the state's cue, steps it, and writes the answer back only when there is one** — and nothing
  else. There is no start: setting the cue is the beginning. It is the only function the play chain calls, and it
  owns the state's field so no caller can get the step wrong.
- **A step that changes nothing reports `ValueNone`** — the caller keeps the cue it gave, rather than rebuilding
  it. That is what keeps an idle tick at 0 bytes (research D6). `ValueSome cue` is the cue as it now stands, which
  may be `Fin`: the ended value is a value, not an absence (so a `Fin` hole at a sequence's front is stepped past
  rather than read as "in flight, unmoved"). `ValueOption` is a struct, so saying this costs the idle path
  nothing.
- **The walk is tail-recursive where it must be.** Dropping a finished front and pulling the next child makes the
  pull the **tail call**, so a cue with 200,000 no-progress ops in a row is consumed by one `tick` without growing
  the stack (FR-011). The tail position is load-bearing: post-processing the pull's answer (a `match` that
  rewrites `ValueNone`) would put the frames back on the stack — which is how the regression would show up.
- **`forkInto`** runs an arriving cue beside the running one: `Fin` becomes the cue, anything else becomes
  `Parallel(current, joined)`. Nothing about `current` is rebuilt, so nothing in flight is disturbed (FR-009).
  It is what a trigger will call in phase 10.
- **Degenerate durations**: a timed op whose deadline has already passed when it is reached **is over before it
  begins** — `advance (Wait s)` with `now >= now + s` requests its final effect (a zero-length camera move puts
  the camera at its target) and returns `Fin`, so it holds no state and costs no step.

## `state/GameplayState.fs` — changed

Two fields join the screen's state, beside `Dialogue` — which is already screen-scoped "so a zone switch does not
clear it", the same reasoning this feature needs:

```fsharp
mutable Cue : Cue.Cue        // the running cue: the run itself. `Fin` when nothing is running.
mutable PlayTime : float     // play-mode time: advanced only while playing, so an edit freezes it
```

- **There is no `setCue` and no separate run.** A cue is begun, replaced and ended by assigning `Cue` (FR-013); the
  editor feature may wrap that in a helper, but there is nothing to keep in step.
- **`Cue` is created as `Fin`**, so a fresh screen has nothing to run and ticks cost nothing.
- **Not modelled here**: no new field on any zone, actor, op or asset; nothing is persisted; the editor reads this
  field but owns none of it (the editor spec adds the panel and the cue's stack).

## State transitions

| From | Event | To |
|---|---|---|
| `Fin` | a tick | `Fin` — it rolls where it is |
| an authored cue | the first play tick | its front op is begun (performed if it costs no time), or `Fin` if there is none |
| a cue in flight | a tick while playing | the cue with finished ops dropped and the front begun or moved |
| a cue in flight | Play pressed with the editor open, then closed | the same cue, advanced by the frames that were played — an edit advances `PlayTime` by nothing |
| a cue in flight | the last op finishes | `Fin` — nothing running (what the panel renders as `cue_end`) |
| a cue (any) | the state's `Cue` is assigned | the new cue (FR-013) |
| any | Play mode left | frozen: `PlayTime` and `Cue` are untouched while editing (FR-002) |

## Validation rules

| Rule | Where it is enforced |
|---|---|
| `Fork` outside a sequence's current op is malformed | `CueEngine`, where the walk reaches it — raises (FR-014) |
| A zone a cue switches to must exist | `GameplayState.switchZone`, unchanged: it already raises with the zone's name |
| A timed op's duration may be zero or negative | not an error: it is over before it begins (above) |
| A `Fin` appears in a sequence | legal: it is *skipped* — nothing to run, so the sequence carries on past it (FR-017 — the type system cannot say "terminal, and last") |
| An op is performed where the walk reaches it | the engine performs each op's effect at the node it reaches — no queue, no applier, no request type (research D3) |

## Deliberately not modelled

- **The editor's view and the cue's undo history** — the editor feature. The screen's cue *is* the view; there is
  no `live`/`current` derivation to build.
- **Joining and splicing** — retired in favour of the fork (FR-009). Composing cues into a new value is authoring
  (`Sequence`), not a run operation.
- **Persistence** — nothing about a cue is written to disk (phase 15).
- **The rest of the op family** — camera paths, actor and avatar warps, fades, emotes, enable/disable sets, advent
  flags, scenario binds (research D1).
- **Trigger → cue wiring, advents and the scenario library** — phases 10–11, where `forkInto` becomes a system
  rather than an engine capability.
- **An actor-facing op** — so no case exists yet for "the actor this names has gone"; the first op that names an
  actor brings that rule with it.
