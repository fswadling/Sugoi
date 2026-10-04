module Cue

// The cue, and therefore the run (spec 046). There is no separate authored cue and no run of frames
// beside it: a cue *is* its own execution state, an immutable value that shrinks as it is stepped
// down to `Fin` — what is left when a cue finishes, and where it then rolls.
//
// The ops with no progress of their own (`Exposit`, `SetZone`, `SetCamera`, `AddAdvent`,
// `RemoveAdvent`) hold no record; a leaf that spans time carries its numbers in its own `…State` case
// (`WaitState`'s deadline, `FlyCameraState`'s resolved path, the fade states' captured level). So a case suffixed `State` appears exactly
// where the run form holds something the authored form does not — the convention the reference lineage
// uses (`wait` / `waitstate`), and FR-017's "a leaf op and its own state case". (`AwaitState` is the one
// exception: it carries the same `Advent`, existing only to mark a blocking op as begun — spec 056.)
//
// Neither names a zone, a screen, an asset or a device: this module depends on `Prime` (for
// `FDeque`), `System.Numerics`, `Advent` (the closed milestone union) and `Dialogue` (the closed
// presentation union, which names `Roster`) only, so it loads on its own. The engine that walks it
// (`world/CueEngine.fs`) is compiled *after* `state/GameplayState.fs`, because the ops perform
// their effects on the gameplay state where the walk reaches them (research D3).

open System.Numerics
open Prime
open Advent
open Place

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
    // The screen fade (spec 060): drive `GameplayState.Fade` toward an endpoint over a duration. A `…State`
    // captures the level where the op was reached — so a fade from part-way is correct — and the deadline.
    | FadeOut of duration: float
    | FadeIn of duration: float
    | FadeOutState of initial: float32 * duration: float * endsAt: float
    | FadeInState of initial: float32 * duration: float * endsAt: float
    // The line and how it is presented (spec 057): a plain dialogue line, a thought, or a portrait line
    // with a speaker and an expression — the closed `Dialogue.Kind`, so a mis-spelled speaker or
    // expression is unrepresentable and a portrait line always carries both. The view resolves the
    // portrait at draw time (`world/DialogueRender.fs`); the op names a character, not an asset.
    | Exposit of text: string * kind: Dialogue.Kind
    | SetZone of place: Place
    | SetCamera of waypoint: string
    | FlyCamera of path: string * speed: float
    | FlyCameraState of path: string * points: Vector3[] * rotations: Quaternion[] * duration: float * endsAt: float
    | AddAdvent of advent: Advent
    | RemoveAdvent of advent: Advent
    | Await of advent: Advent
    | AwaitState of advent: Advent
    | If of advent: Advent * thenCue: Cue * elseCue: Cue

// ── Forking ──

/// Run an arriving cue beside whatever is running: `Fin` becomes the cue, anything else becomes a
/// parallel with it. The running cue is not rebuilt, so nothing in flight is disturbed (FR-009). It is pure
/// and device-free, so it lives here beside the type rather than in the engine; a trigger fires its authored
/// cue with this (spec 058).
let forkInto (joined: Cue) (current: Cue) : Cue =
    match current with
    | Fin -> joined
    | _ -> Parallel(current, joined)
