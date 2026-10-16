module Cue

// The cue, and therefore the run (spec 046). There is no separate authored cue and no run of frames
// beside it: a cue *is* its own execution state, an immutable value that shrinks as it is stepped
// down to `Fin` — what is left when a cue finishes, and where it then rolls.
//
// The ops with no progress of their own (`Exposit`, `Print`, `SetZone`, `AddZone`, `RemoveZone`, `PreloadZone`,
// `AddActor`, `RemoveActor`, `EnablePlayer`, `DisablePlayer`, `EnableTrigger`, `DisableTrigger`, `SetCamera`,
// `AddAdvent`, `RemoveAdvent`, `PlaySound`) hold no record; a leaf that spans time carries its numbers in its own `…State` case
// (`WaitState`'s deadline, `FlyCameraState`'s resolved path, the fade states' captured level, `BlendState`'s
// captured weights, `MoveActorState`'s spline, `RotateActorState`'s turn). So a case suffixed `State` appears
// exactly where the run form holds something the authored form does not — the convention the reference lineage
// uses (`wait` / `waitstate`), and FR-017's "a leaf op and its own state case". (`AwaitState` is the one
// exception: it carries the same milestone name, existing only to mark a blocking op as begun — spec 056.)
//
// Neither names a zone, a screen, an asset or a device: this module depends on `Prime` (for
// `FDeque`), `System.Numerics`, `Dialogue` (the closed presentation union) and `SlotMap` (its
// `Handle`, which the actor `…State` cases carry) only, so it loads on its own. A milestone and a
// scenario are plain strings (specs 082, 083). The engine
// that walks it (`state/CueEngine.fs`) is compiled *after* `state/GameplayState.fs`, because the ops perform
// their effects on the gameplay state where the walk reaches them (research D3).

open System.Numerics
open Prime
open SlotMap

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
    // portrait at draw time (`world/render/DialogueRender.fs`); the op names a character, not an asset.
    | Exposit of text: string * kind: Dialogue.Kind
    // Print (spec 067): a developer-facing line written to the console where the walk reaches it — a no-progress
    // leaf, distinct from `Exposit`'s player-facing dialogue (spec 057).
    | Print of text: string
    | SetZone of place: string
    // A cue makes a present zone absent (spec 062). Bringing a zone in **with its staging** is a scenario fork
    // now (spec 079), so `AddZone`/`PreloadZone` retired with the Zone Registry.
    | RemoveZone of place: string
    // The actor lifecycle (spec 073, spec 075): a cue brings an actor into a named place and places her at a
    // waypoint there (`AddActor` — always a new actor, named for the asset it is built from), or takes her out
    // of a named place (`RemoveActor`). Both name the actor by a **string** — her `Name` — and name the place
    // she is in, so the zone is explicit rather than implied by the current zone (spec 075); a name is a label,
    // not an identity. Placement also names an asset and a waypoint; only the asset and the waypoint are names,
    // in the asset manager and the place's zone. Both are no-progress leaves and are no-ops on state they cannot
    // act on (an absent place, a missing waypoint, an unknown asset, or an actor not in the named place), so a
    // re-run is safe — though `AddActor` spawns a fresh actor each time it is reached with valid state, since a
    // name is a label rather than an identity (spec 072 FR-008).
    | AddActor of place: string * actor: string * asset: string * waypoint: string
    | RemoveActor of place: string * actor: string
    // The actor's animation/morph blends (spec 066, spec 075): drive a slot's weight over a duration.
    // `Animate`/`Morph` move one slot from its current weight to the target; `CrossFadeAnimation`/`CrossFadeMorph`
    // take one out while another comes in (the suffixes added by spec 088 so the actor, morph and music
    // cross-fades read alike). They act in the **named place's** zone, active or not — slot weights are
    // renderer state, not part of the simulation. `BlendState` is the in-flight form, holding the captured
    // start weights so a blend that begins part-way is correct; it also carries the actor's resolved slot
    // (`place` + `handle`), so the step reuses her instead of scanning by name each tick (`specs/074`).
    | Animate of place: string * actor: string * clip: string * weight: float32 * duration: float
    | CrossFadeAnimation of place: string * actor: string * fromClip: string * toClip: string * weight: float32 * duration: float
    | Morph of place: string * actor: string * morph: string * weight: float32 * duration: float
    | CrossFadeMorph of place: string * actor: string * fromMorph: string * toMorph: string * weight: float32 * duration: float
    | BlendState of actor: string * place: string * handle: Handle * morph: bool * fromName: string * toName: string * initialFrom: float32 * initialTo: float32 * target: float32 * duration: float * endsAt: float
    // The actor's movement (spec 066): drive her body along a waypoint chain (`MoveActor`, turning to face the
    // path and blending a moving clip to an idle clip as she arrives), or turn her in place to face a waypoint
    // (`RotateActor`). Both are **physics-driven** and act only while her zone is the **active** one, so an
    // obstructed move stops where the collision leaves it. Each `…State` is the in-flight form, and each also
    // carries the actor's resolved slot (`place` + `handle`) so the step reuses her (`specs/074`).
    | MoveActor of actor: string * path: string * speed: float * movingClip: string * idleClip: string
    | MoveActorState of actor: string * place: string * handle: Handle * points: Vector3[] * speed: float * duration: float * endsAt: float * initialRotation: Quaternion * finalRotation: Quaternion * blend: float * movingClip: string * idleClip: string
    | RotateActor of actor: string * waypoint: string * duration: float
    | RotateActorState of actor: string * place: string * handle: Handle * initialRotation: Quaternion * targetRotation: Quaternion * duration: float * endsAt: float
    // The player toggle (spec 066, spec 075): bind her as the **named place's** player — the runtime
    // counterpart of the editor's "Set as Player", so the play-mode input drives her and the camera follows —
    // or clear it when she is the player. No progress of their own, and a no-op on state they cannot act on.
    | EnablePlayer of place: string * actor: string
    | DisablePlayer of place: string * actor: string
    // The trigger toggles (spec 067): arm or disarm a named trigger in a named place — the cross-zone
    // convention (the op names a place, resolved at run time), a no-progress leaf, and a no-op on a place that
    // is not present, a missing trigger, or a redundant toggle. The flag is authored state; the trigger system
    // recomputes the derived runtime state on the next play tick.
    | EnableTrigger of place: string * trigger: string
    | DisableTrigger of place: string * trigger: string
    // The camera ops (spec 054, spec 075): read the named place's waypoints, so an author can frame from a
    // waypoint in a zone other than the one they are standing in.
    | SetCamera of place: string * waypoint: string
    | FlyCamera of place: string * path: string * speed: float
    | FlyCameraState of path: string * points: Vector3[] * rotations: Quaternion[] * duration: float * endsAt: float
    | AddAdvent of advent: string
    | RemoveAdvent of advent: string
    | Await of advent: string
    | AwaitState of advent: string
    | If of advent: string * thenCue: Cue * elseCue: Cue
    // Fork a registered scenario into the scene (spec 078; a string name since spec 083): materialise its entities
    // and run its cue **beside** the rest, which is `Fork`'s construct. It names a scenario as a plain string — the
    // library that resolves it (`world/ScenarioRegistry.fs`) is compiled after this file, exactly as `SetZone` names
    // a place before `ZoneRegistry` exists — and it is a runtime effect, like every cue op.
    | ForkScenario of scenario: string
    // The music cues (spec 088): set up the palette (membership), start and stop it (transport), and shape it
    // (a level, the master, a cross-fade) as the run reaches them. The mix is the authored state `SharedState`
    // holds (spec 087); a cue mutates that same mix directly and never lands on the one history (FR-009), the
    // `AddAdvent` pattern. Membership and transport are no-progress leaves; a shaping op carries its numbers in
    // a `…State` (added with each shaping story), the `FadeOut`/`Animate` convention.
    | AddMusic of song: string
    | RemoveMusic of song: string
    | PlayMusic of song: string
    | StopMusic of song: string
    // The whole bed (FR-005): one op starts or stops every palette song, so a scene needs no song enumeration.
    | PlayBed
    | StopBed
    // Shaping (FR-006..FR-008): move a row's level, or the palette master, to a target over a duration (or at
    // once when it is zero). The captured level in each `…State` is what makes a ramp begun part-way correct
    // and a retarget continue from the current level (FR-012).
    | SetMusicLevel of song: string * level: float32 * duration: float
    | SetMusicMaster of level: float32 * duration: float
    | MusicLevelState of song: string * initial: float32 * target: float32 * duration: float * endsAt: float
    | MusicMasterState of initial: float32 * target: float32 * duration: float * endsAt: float
    // Swap one palette song for another with one op: `fromSong` falls to silence and `toSong` rises to full,
    // together over the duration (FR-008). The music counterpart of `CrossFadeAnimation`; the captured weights
    // make a cross-fade begun part-way correct. A side that is not a palette row is a reported no-op and the
    // other side still moves.
    | CrossFadeMusic of fromSong: string * toSong: string * duration: float
    | CrossFadeMusicState of fromSong: string * toSong: string * initialFrom: float32 * initialTo: float32 * duration: float * endsAt: float
    // The sound cue (spec 090): play a named sound one-shot where the run reaches it — the runtime counterpart
    // of the Sounds panel's Play (spec 089). A one-shot is not held, so it is a **no-progress leaf** (no state
    // case) that composes `audio/Sound.fs` as-is over the shared voice pool. Like every cue op it is a runtime
    // effect that never lands on the one history (FR-004), and a sound is global, so it names no place.
    | PlaySound of sound: string

// ── Forking ──

/// Run an arriving cue beside whatever is running: `Fin` becomes the cue, anything else becomes a
/// parallel with it. The running cue is not rebuilt, so nothing in flight is disturbed (FR-009). It is pure
/// and device-free, so it lives here beside the type rather than in the engine; a trigger fires its authored
/// cue with this (spec 058).
let forkInto (joined: Cue) (current: Cue) : Cue =
    match current with
    | Fin -> joined
    | _ -> Parallel(current, joined)
