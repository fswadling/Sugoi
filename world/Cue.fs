module Cue

// The cue, and therefore the run (spec 046). There is no separate authored cue and no run of frames
// beside it: a cue *is* its own execution state, an immutable value that shrinks as it is stepped
// down to `Fin` — what is left when a cue finishes, and where it then rolls.
//
// The ops with no progress of their own (`Exposit`, `SetZone`, `AddZone`, `RemoveZone`, `PreloadZone`,
// `SetActor`, `RemoveActor`, `EnablePlayer`, `DisablePlayer`, `SetCamera`, `AddAdvent`,
// `RemoveAdvent`) hold no record; a leaf that spans time carries its numbers in its own `…State` case
// (`WaitState`'s deadline, `FlyCameraState`'s resolved path, the fade states' captured level, `BlendState`'s
// captured weights, `MoveActorState`'s spline, `RotateActorState`'s turn). So a case suffixed `State` appears
// exactly where the run form holds something the authored form does not — the convention the reference lineage
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
    // The rest of the zone vocabulary (spec 062): a cue makes a declared place present (streaming its
    // template in) without entering it, makes a present place absent, and preloads a present zone's
    // environment so the later switch is instant. Each is a no-progress leaf, and each is a no-op on a
    // place it cannot act on, so a cue is safe to run more than once.
    | AddZone of place: Place
    | RemoveZone of place: Place
    | PreloadZone of place: Place
    // The actor lifecycle (spec 064): a cue brings a member into a named place and places her at a waypoint
    // there (`SetActor` — one op for a first placement and a move, unifying odin's add and warp), or takes her
    // out (`RemoveActor`). Placement names a place and a character; removal names the character alone, because
    // the runtime cast index (`specs/063`) already knows where she is and she is unique across all zones. Only
    // the waypoint is a name in the place's zone. Each is a no-progress leaf, and each is a no-op on state it
    // cannot act on, so a cue re-runs safely.
    | SetActor of place: Place * character: Roster.Character * look: Casting.Look * waypoint: string
    | RemoveActor of character: Roster.Character
    // The actor's animation/morph blends (spec 066): drive a slot's weight over a duration. `Animate`/`Morph`
    // move one slot from its current weight to the target; `CrossFade`/`CrossMorph` take one out while another
    // comes in. They resolve the actor through the index and act in her own zone, active or not — slot weights
    // are renderer state, not part of the simulation. `BlendState` is the in-flight form, holding the captured
    // start weights so a blend that begins part-way is correct.
    | Animate of character: Roster.Character * clip: string * weight: float32 * duration: float
    | CrossFade of character: Roster.Character * fromClip: string * toClip: string * weight: float32 * duration: float
    | Morph of character: Roster.Character * morph: string * weight: float32 * duration: float
    | CrossMorph of character: Roster.Character * fromMorph: string * toMorph: string * weight: float32 * duration: float
    | BlendState of character: Roster.Character * morph: bool * fromName: string * toName: string * initialFrom: float32 * initialTo: float32 * target: float32 * duration: float * endsAt: float
    // The actor's movement (spec 066): drive her body along a waypoint chain (`MoveActor`, turning to face the
    // path and blending a moving clip to an idle clip as she arrives), or turn her in place to face a waypoint
    // (`RotateActor`). Both are **physics-driven** and act only while her zone is the **active** one, so an
    // obstructed move stops where the collision leaves it. Each `…State` is the in-flight form.
    | MoveActor of character: Roster.Character * path: string * speed: float * movingClip: string * idleClip: string
    | MoveActorState of character: Roster.Character * points: Vector3[] * speed: float * duration: float * endsAt: float * initialRotation: Quaternion * finalRotation: Quaternion * blend: float * movingClip: string * idleClip: string
    | RotateActor of character: Roster.Character * waypoint: string * duration: float
    | RotateActorState of character: Roster.Character * initialRotation: Quaternion * targetRotation: Quaternion * duration: float * endsAt: float
    // The player toggle (spec 066): bind her as the zone's player — the runtime counterpart of the editor's
    // "Set as Player", so the play-mode input drives her and the camera follows — or clear it when she is the
    // player. No progress of their own, and a no-op on state they cannot act on.
    | EnablePlayer of character: Roster.Character
    | DisablePlayer of character: Roster.Character
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
