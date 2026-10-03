module Cue

// The cue, and therefore the run (spec 046). There is no separate authored cue and no run of frames
// beside it: a cue *is* its own execution state, an immutable value that shrinks as it is stepped
// down to `Fin` — what is left when a cue finishes, and where it then rolls.
//
// The ops with no progress of their own (`Exposit`, `SwitchZone`, `SetCamera`) hold no record; a leaf
// that spans time carries its numbers in its own `…State` case (`WaitState`'s deadline,
// `FlyCameraState`'s resolved path). So a case suffixed `State` appears exactly where the run form
// holds something the authored form does not — the convention the reference lineage uses
// (`wait` / `waitstate`), and FR-017's "a leaf op and its own state case".
//
// Neither names a zone, a screen, an asset or a device: this module depends on `Prime` (for
// `FDeque`) and `System.Numerics` only, so it loads on its own. The engine that walks it
// (`world/CueEngine.fs`) is compiled *after* `state/GameplayState.fs`, because the ops perform
// their effects on the gameplay state where the walk reaches them (research D3).

open System.Numerics
open Prime

// ── The cue — and therefore the run ──

/// A cue *is* its own execution state. `Fin` is the terminal and the fixed point — advancing it is
/// itself — which is what makes "nothing left to run" a *value* rather than an absence, so begun and
/// finished cannot be confused and no flag is needed to tell them apart.
///
/// A `Sequence`'s `current` is the op in flight (or the next to run) and `rest` is the queue not yet
/// started, held as cue values. `current` is deliberately held *out* of the deque: stepping the
/// front of a deque means `uncons`, which allocates the tail it returns, and on the idle path that
/// is a cost every frame (measured — see research D2). Held out, an idle step touches no deque.
type Cue =
    | Fin
    | Sequence of current: Cue * rest: Cue FDeque
    | Parallel of left: Cue * right: Cue
    | Fork of inner: Cue
    | Wait of seconds: float
    | WaitState of seconds: float * endsAt: float
    | Exposit of text: string
    | SwitchZone of zone: string
    | SetCamera of waypoint: string
    | FlyCamera of path: string * speed: float
    | FlyCameraState of path: string * points: Vector3[] * rotations: Quaternion[] * duration: float * endsAt: float
