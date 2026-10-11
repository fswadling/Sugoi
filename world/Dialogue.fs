module Dialogue

// The dialogue state and its vocabulary: what the screen is showing, as data. Pure —
// no GPU, no asset, no presentation. Held by `GameplayState` (the screen's, not a
// zone's, not an entity's: spec 038 FR-007), written field by field by the editor
// panel, and read by the view (`world/DialogueRender.fs`).
//
// Compiled before `world/Assets.fs` and `world/Systems.fs` (which read it), per
// `specs/038-dialogue-display/contracts/dialogue-display.md`. The speaker is a plain string
// (`specs/074`): the actor's `Name`, resolved to her asset by the view; this module names no roster.

/// A character's expression. The closed set every declared portrait set must cover, so
/// "the portrait is derived from (speaker, expression)" is a checkable claim rather
/// than a hopeful one (FR-006). The reference's five.
///
/// A union rather than a string so it is matched exhaustively and a misspelling is a
/// compile error. It gains `[<SymbolicExpansion>]` when a cue or a scenario is first
/// serialized (roadmap phase 15): the cue's `Exposit` op now authors an expression
/// (spec 057), but nothing writes a cue to disk yet, so the attribute would still be a
/// just-in-case.
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

/// What the dialogue layer is showing. Runtime screen state on `GameplayState`: never
/// authored into a manifest, never persisted, never carried by an entity's captured
/// state. `Shown` is the only field that decides visibility, so there is nothing that
/// can disagree with it.
[<Struct>]
type Dialogue =
    { /// On screen. The player clears it; whatever delivers lines waits for that.
      mutable Shown : bool
      /// The line. `Text` is the whole line — there is no reveal progress and no timer.
      mutable Kind : Kind
      mutable Text : string }

/// Nothing shown — the default, so a fresh screen draws exactly what it drew before this
/// feature existed (FR-003).
let empty : Dialogue = { Shown = false; Kind = Kind.Dialogue; Text = "" }
