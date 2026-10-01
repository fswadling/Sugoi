# Contract: Cue Core

The surfaces this feature adds or changes, and what explicitly does not change.

## `world/Cue.fs` — NEW

```fsharp
type Cue =
    | Fin
    | Sequence of current: Cue * rest: Cue FDeque
    | Parallel of left: Cue * right: Cue
    | Fork of inner: Cue
    | Wait of seconds: float
    | WaitState of seconds: float * endsAt: float
    | Exposit of text: string
    | SwitchZone of zone: string
    | MoveCamera of target: CameraPose * duration: float
    | MoveCameraState of from: CameraPose * target: CameraPose * duration: float * endsAt: float

[<Struct>] type CameraPose = { Position: Vector3; Target: Vector3 }
```

**Contract**: the cue is the run — one type, and a step reports the cue as it now stands, or nothing to change. The module depends on
`Prime` (for `FDeque`) and `System.Numerics` only — **no zone, screen, asset, render or device type appears in
it**, so it stays loadable on its own even though the engine below does not. A `…State` case appears exactly where
a leaf's run form holds numbers the authored form does not.

## `world/CueEngine.fs` — NEW

```fsharp
val advance  : shared: SharedState.SharedState -> state: byref<GameplayState.GameplayState>
               -> now: float -> Cue -> Cue voption
val tick     : shared: SharedState.SharedState -> state: byref<GameplayState.GameplayState>
               -> now: float -> unit
val forkInto : joined: Cue -> current: Cue -> Cue
```

The answer is a `Cue voption`: `ValueSome cue` is the cue as it now stands — which may be `Fin`, a *value* rather
than an absence — and `ValueNone` is "still working on what you were given, keep it". The pull of a finished
front's successor is in **tail position**, so a sequence of arbitrarily many no-progress ops is consumed without
growing the stack (FR-011).

**Contract**: one cue in, the cue as it now stands out (or nothing to change), with each op's effect performed
where the walk reaches it — an exposition writes `Dialogue`, a zone switch reuses `switchZone`, a camera move
writes `Camera` — so this module is compiled **after** `state/GameplayState.fs` and names it. A step that changes
nothing reports `ValueNone` (the caller keeps the cue it gave, not a rebuild), so an idle tick allocates nothing at
any depth; a step that completes an op allocates a bounded amount (one rebuilt spine per level). There is **no
start**: setting the cue is the beginning, and `tick` lifts `state.Cue`, steps it, and writes the answer back only
when there is one. `forkInto` is `Fin -> joined | _ -> Parallel(current, joined)`. Both are asserted in the suite.

## `state/GameplayState.fs` — CHANGED

```fsharp
mutable Cue : Cue.Cue        // NEW — the running cue; `Fin` when nothing is running
mutable PlayTime : float     // NEW
```

**Contract**: there is no `setCue` and no separate run — a cue is begun, replaced and ended by assigning `Cue`
(FR-013). An op's effect reaches this state through the engine (below), reusing `switchZone` unchanged — including
its raise for an unknown zone. `PlayTime` advances only where the play chain runs (below).

## `screen/GameplayScreen.fs` — CHANGED

Inside the existing `if not state.Editing` block, after `Systems.animate` and before the frame is drawn:

```fsharp
state.PlayTime <- state.PlayTime + float frame.Dt
CueEngine.tick shared &state state.PlayTime
```

**Contract**: two lines. `tick` owns the state's cue — it lifts `state.Cue`, steps it, and writes the answer back
only when the step reports a change (so a waiting cue costs the frame nothing, not even a write). The cue runs
**last**, so an op's effect is what the next frame's systems see (research D5). There is no start call: the first
play tick begins the cue.

## `test/CueTests.fs` — NEW

**Contract**: the three stories, the spec's edge cases, and the two allocation properties, asserted against cues
and the state they changed, with no device. A cue's *walk* is asserted on the value (`advance`); its *effects*
against a `GameplayState` built headlessly the way `test/GameplayStateTests.fs` already builds one
(`Unchecked.defaultof` for the GPU pieces), so a zone switch really switched and an exposition really wrote
`Dialogue`.

## `fsharp-game.fsproj` — CHANGED

Two `Compile` entries. `world/Cue.fs` goes before `state/GameplayState.fs`, which names `Cue`. `world/CueEngine.fs`
goes *after* `state/GameplayState.fs`, because it performs the ops' effects on that state, and before
`screen/GameplayScreen.fs`, which calls it. This is the project's one deliberate ordering inversion (a `world/`
file below `state/`), and the list says why.

## `docs/` — CHANGED

| File | Change |
|---|---|
| `ARCHITECTURE.md` | the state diagram: the cue moves out of `Shared_State` onto the gameplay screen; the "Scenario & Cue" section's `{ template_root, state_root, state_map }` / `CueState` description is replaced by the cue that *is* its run, shrinking to `Fin` |
| `EDITOR_DESIGN.md` | D-010: there is no template/runtime split and no separate run — the cue is an immutable value replaced as it runs; the cue's stack is its own history, not a zone's |
| `ROADMAP.md` | phase 9's deliverables and its "done when" (the engine here, the editor next); the next-step line |
| `PROJECT_STRUCTURE.md` | `world/Cue.fs` and `world/CueEngine.fs` listed for what they are |
| `CUE_MODEL_DECISION.md` | Status corrected: it records the *first* model (an authored cue beside a run of frames) and is kept for its measurements and the route, not as the design |

## Explicitly unchanged

- **`world/Systems.fs`** — the play systems stay as they are; the cue runs last, beside them, as its own call in
  the play chain rather than a named system (research D5).
- **`world/Zone.fs`, `world/Actor.fs`, `world/Dialogue.fs`, `world/Entity.fs`** — no new field and no new
  behaviour. `Dialogue`, `Zone` and `Camera` are *written by* the engine's ops at the point the walk reaches them,
  through their existing shapes.
- **The editor** (`editor/*`) — no panel, no command, no `EditorState` field, no key.
- **`state/Command.fs`, `state/EditorState.fs`, the undo stacks** — the cue's undo history is the editor feature's.
- **The render path, the physics world, `SharedState`, and the op/asset family** — untouched.
- **The rest of the vocabulary** — no camera paths, warps, fades, emotes, enable/disable sets, advent flags or
  scenario binds (research D1). Adding one adds a `Cue` case and, only if its progress needs numbers the authored
  form does not carry, its `…State` case; it changes neither the shape of the run nor the walk (FR-017).
