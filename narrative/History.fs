namespace Narrative

// History is append-only and immortal: each event carries the caller's game time and a
// dense index, and nothing ever rewrites one (FR-001, FR-002). Order comes from the
// caller, never from arrival (FR-024), and the dense index breaks ties between equal game
// times (FR-023).

open System

[<Struct>]
type GameTime = GameTime of int

type CommittedEvent =
    { Index : int
      Time : GameTime
      Event : Event }

/// Raised, rather than returned, because the engine can do nothing with it: the caller supplies
/// the order (FR-024), so an event whose time precedes the last committed one is one the engine
/// cannot place. This is the same posture as `DerivationBoundExceeded` — raising is safe because
/// a commit that raises has changed nothing, so the caller still holds a usable engine (FR-013).
exception TimeWentBackwards of time: GameTime * previous: GameTime
    with
        override this.Message =
            let (GameTime t) = this.time
            let (GameTime p) = this.previous
            sprintf "game time %d precedes the previous committed time %d; the caller supplies the order" t p

module History =

    /// Committed events in commit order, never rewritten. Held newest first — the head is the most
    /// recently committed event, and its index is how many have been committed, because indices are
    /// dense and 1-based — so an append is a cons per event: nothing already committed is ever
    /// copied, and an engine value keeps describing the history it described.
    ///
    /// An immutable array cannot be extended without copying it, and history may not be mutated in
    /// place. Chunks of arrays were tried for that, and they satisfy it by copying at most one
    /// chunk per append; a list copies nothing, and gives up only a contiguity that the read path —
    /// a save, a report, a test — is not hot enough to miss (research D29).
    type State = CommittedEvent list

    let empty : State = []

    /// The index the next committed event takes. Read from the newest event's own `Index` — which
    /// *is* the committed count, since indices are dense and 1-based — rather than from
    /// `List.length`, because an F# list does not carry its length: `List.length`, the `Length`
    /// property and `Enumerable.Count` all traverse it, at about 3 ns an element, which read once
    /// per commit is a walk per event (research D29).
    let private nextIndex (state: State) : int =
        match state with
        | [] -> 1
        | newest :: _ -> newest.Index + 1

    let lastTime (state: State) : GameTime option =
        match state with
        | [] -> None
        | newest :: _ -> Some newest.Time

    /// Oldest first, which is commit order: what a save writes, a report renders and a test reads.
    let toList (state: State) : CommittedEvent list = List.rev state

    /// Append one event. Its time must not precede the last committed one, or the append raises
    /// `TimeWentBackwards` before anything is written (FR-024, contracts/engine-api.md §
    /// Invariants). Committing a run is a fold of this, so an event that is refused leaves the
    /// events before it committed and nothing after it.
    let append (state: State) (time, event) : State =
        match lastTime state with
        | Some (GameTime p) when (let (GameTime t) = time in t < p) ->
            raise (TimeWentBackwards(time, GameTime p))
        | _ -> { Index = nextIndex state; Time = time; Event = event } :: state
