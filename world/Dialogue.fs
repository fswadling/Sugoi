module Dialogue

// The dialogue state and its vocabulary: what the screen is showing, as data. Pure —
// no GPU, no asset, no presentation. Held by `GameplayState` (the screen's, not a
// zone's, not an entity's: spec 038 FR-007), written field by field by the editor
// panel, and read by the view (`world/render/DialogueRender.fs`).
//
// Compiled before `world/assets/Assets.fs` and `world/Systems.fs` (which read it), per
// `specs/038-dialogue-display/contracts/dialogue-display.md`. The speaker is a plain string
// (`specs/074`): the actor's `Name`, resolved to her asset by the view; this module names no roster.

/// A character's expression. The closed set every declared portrait set must cover, so
/// "the portrait is derived from (speaker, expression)" is a checkable claim rather
/// than a hopeful one (FR-006). The reference's five.
///
/// A union rather than a string so it is matched exhaustively and a misspelling is a
/// compile error. It needs no `[<SymbolicExpansion>]`: `PortraitDialogue` folds it in
/// positionally, so Prime serializes a cue carrying one without the attribute (spec 095).
type Emotion =
    | Neutral
    | Happy
    | Sad
    | Angry
    | Surprised

/// The renderable name of an expression — matched over the cases rather than formatted, so
/// adding a case is an edit here and the compiler reports it.
let emotionName (emotion: Emotion) : string =
    match emotion with
    | Neutral -> nameof Neutral
    | Happy -> nameof Happy
    | Sad -> nameof Sad
    | Angry -> nameof Angry
    | Surprised -> nameof Surprised

/// Every expression, in declaration order — what the editor's expression combo walks.
let emotions : Emotion list = [ Neutral; Happy; Sad; Angry; Surprised ]

/// How a line is presented: the reference's three kinds, with the speaker and the
/// expression folded in where they apply, so a portrait line always carries both and a
/// speaker-less kind never carries an expression (research Decision 2).
///
/// `Dialogue` and `Thought` are the reference's two speaker-less kinds; `Thought` draws
/// no portrait and no name plate and its panel is drawn inverted (FR-002).
type Kind =
    | PortraitDialogue of speaker: string * Emotion
    | Dialogue
    | Thought

/// One runtime condition that may end a shown line (spec 097). The cases are data-free so a real
/// `Set<Ending>` works; the timeout's duration is not part of `ByTimeout` — it lives in
/// `Dialogue.Timeout` and is meaningful only when `ByTimeout` is in the set.
///
/// Closing a line from a cue is not a member: a hide or a replacement always works, whatever the set
/// says. The empty set therefore means the cue owns the line's end, not that the line cannot close.
type Ending =
    | ByPlayer
    | ByTimeout

/// What the dialogue layer is showing. Runtime screen state on `GameplayState`: never
/// authored into a manifest, never persisted, never carried by an entity's captured
/// state. `Shown` is the only field that decides visibility, so there is nothing that
/// can disagree with it.
[<Struct>]
type Dialogue =
    { /// On screen. The player or a cue clears it; whatever delivers lines waits for that.
      mutable Shown : bool
      /// The line. `Text` is the whole line — there is no reveal progress and no timer.
      mutable Kind : Kind
      mutable Text : string
      /// The runtime conditions that may end the line. Either, both, or neither; the empty set
      /// means the cue owns the line's end and a hide or replacement always works.
      mutable Endings : Set<Ending>
      /// The duration when `ByTimeout` is in `Endings`; `ValueNone` otherwise.
      mutable Timeout : float voption
      /// Play time at which the line was shown; the timeout is measured from here.
      mutable ShownAt : float }

/// Nothing shown — the default, so a fresh screen draws exactly what it drew before this
/// feature existed (FR-003). A line shown by hand stays dismissible, as it is today.
let empty : Dialogue =
    { Shown = false
      Kind = Kind.Dialogue
      Text = ""
      Endings = Set.singleton ByPlayer
      Timeout = ValueNone
      ShownAt = 0.0 }

// ── ending conditions ──

/// Whether the player may end the line with the dismiss control.
let playerCloses (endings: Set<Ending>) : bool = Set.contains ByPlayer endings

/// Whether a timeout is one of the line's conditions.
let timed (endings: Set<Ending>) : bool = Set.contains ByTimeout endings

/// The set with the player condition added or removed.
let withPlayer (on: bool) (endings: Set<Ending>) : Set<Ending> =
    if on then Set.add ByPlayer endings else Set.remove ByPlayer endings

/// The set with the timeout condition added or removed. The duration lives in `Dialogue.Timeout`.
let withTimeout (on: bool) (endings: Set<Ending>) : Set<Ending> =
    if on then Set.add ByTimeout endings else Set.remove ByTimeout endings

/// A short author-facing summary: "cue" for the empty set (cue-managed), otherwise the present
/// runtime labels joined with " or" — "player", "3.5s", "player or 3.5s".
let summary (endings: Set<Ending>) (timeout: float voption) : string =
    let parts =
        [ if playerCloses endings then yield "player"
          match timeout with
          | ValueSome seconds when timed endings -> yield sprintf "%gs" seconds
          | _ -> () ]
    if List.isEmpty parts then "cue" else String.concat " or " parts
