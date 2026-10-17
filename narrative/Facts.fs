namespace Narrative

// The fact set is a set of facts: each fact is its own identity, and the reasons it holds are
// carried with it (research D33, D34). A fact is put in canonical participant order as it is stored,
// so the two ways of writing a mutual fact are one value rather than two, and nothing has to compare
// text to find out. Everything this file orders or identifies is the value itself — F#'s structural
// comparison, which is ordinal on the strings involved, so FR-021 holds in a locale nobody tested in.
// Sorting for a reader is the harness's job, not this file's.

open System
open Roster

/// What a reason applied to. A projection applies an event; a derivation applies facts
/// (research D6).
type Premise =
    | FromEvent of Event
    | FromFact of Fact

/// Whether an event or a rule put a reason on a fact. A projected reason is invalidated by a
/// withdrawing event; an inferred reason by its premises ceasing to hold, which recomputation
/// discovers. It is carried on the reason rather than being a second fact set, so a fact is one
/// fact whichever put it there (FR-011, research D7, D19).
type Origin =
    | Projected
    | Inferred

/// One reason a fact holds: the rule, the premises that rule applied to, and whether an event or a
/// rule put it there (FR-010).
type Reason =
    { Rule : string
      Premises : Premise list
      Origin : Origin }

module Facts =

    /// Current facts, each with the reasons it holds, ordered by the fact's own value.
    type Set = Map<Fact, Reason list>

    let empty : Set = Map.empty

    /// What a reason is identified by: the rule, and the premises it applied to *as a set*. The order
    /// a rule's `let!` bindings happen to run in is an internal detail — a source's iteration order,
    /// which the fixpoint's internals decide — so it cannot be what makes two reasons the same or
    /// different (FR-010). Two reasons are the same when the same rule applied to the same premises.
    ///
    /// The order premises are *stored* in is left alone, so a report can still print them in the order
    /// the rule reads in.
    let private reasonKey (reason: Reason) : string * Premise list =
        reason.Rule, List.sort reason.Premises

    let private compareReasons (a: Reason) (b: Reason) : int =
        compare (reasonKey a) (reasonKey b)

    /// A fact's reasons as they are offered: each one once, in a canonical order. Ordering by the
    /// rule's name and then by the premises as a set is what makes permuting the rule set unable to
    /// permute what a fact is attributed to (FR-019, research D14).
    let canonical (reasons: Reason list) : Reason list =
        reasons |> List.distinctBy reasonKey |> List.sortWith compareReasons

    let premiseText (premise: Premise) : string =
        match premise with
        | FromEvent e -> Canonical.eventText e
        | FromFact f -> Canonical.factText f

    /// Add a reason for a fact, returning whether the fact itself was absent and the new set. A
    /// further reason for a fact already current enables no new match, so novelty — not reason
    /// insertion — is what a caller such as the fixpoint reacts to (FR-009, FR-045).
    ///
    /// The fact goes into canonical participant order first, so a rule that happens to write the two
    /// names the other way round adds a reason to the existing fact rather than a second fact. The
    /// insert is one `Map.change`, so determining novelty and inserting are one traversal.
    let add (fact: Fact) (reason: Reason) (set: Set) : bool * Set =
        let fact = Vocabulary.sortParticipants fact
        let mutable novel = false

        let updated =
            set
            |> Map.change fact (function
                | None ->
                    novel <- true
                    Some [ reason ]
                | Some reasons ->

                if List.exists (fun held -> compareReasons held reason = 0) reasons then Some reasons
                else Some(List.sortWith compareReasons (reason :: reasons)))

        novel, updated

    /// Whether a fact is currently recorded. The canonical participant order is applied first, so
    /// asking with either wording of a mutual fact gets the same answer (FR-009, research D22).
    let contains (fact: Fact) (set: Set) : bool =
        Map.containsKey (Vocabulary.sortParticipants fact) set

    /// Withdraw the reasons this rule put on this fact. A withdrawing event removes what its own
    /// projection established — the reasons of `Projected` origin — and leaves a reason another
    /// rule holds for the same fact alone. That is what keeps a fact that is also inferred, and it
    /// is FR-011 read at the level of reasons rather than facts (research D19). Withdrawing
    /// something that is not there is a no-op.
    /// The fact is removed when no reason is left, which the returned flag reports so a caller can
    /// tell a reason edit from a change to the fact set itself.
    let withdraw (rule: string) (fact: Fact) (set: Set) : bool * Set =
        let fact = Vocabulary.sortParticipants fact

        match Map.tryFind fact set with
        | None -> false, set
        | Some reasons ->

        let remaining =
            reasons
            |> List.filter (fun held ->
                not (held.Origin = Projected && String.Equals(held.Rule, rule, StringComparison.Ordinal)))

        if List.isEmpty remaining then true, Map.remove fact set
        else false, Map.add fact remaining set

    /// The set with every reason a rule put there removed: what the events amount to, with nothing
    /// the rules concluded. A fact left with no reasons goes, so a fact that was only inferred
    /// disappears here and the fixpoint concludes it again if it still follows (research D7).
    let dropInferred (set: Set) : Set =
        set
        |> Map.fold
            (fun kept fact reasons ->
                let projected = reasons |> List.filter (fun held -> held.Origin = Projected)
                if List.isEmpty projected then kept else Map.add fact projected kept)
            Map.empty

    /// Every reason currently supporting a fact. Empty when the fact is not current. This is what the
    /// running game reads when a player asks why a fact holds (FR-010, clarification 2).
    let reasonsFor (fact: Fact) (set: Set) : Reason list =
        match Map.tryFind (Vocabulary.sortParticipants fact) set with
        | Some reasons -> reasons
        | None -> []

    /// Every fact naming this character, in any position, unfiltered, in the order the set holds them.
    /// What is withheld from a player or a character is the caller's decision, not the engine's
    /// (FR-015).
    let ofCharacter (character: Character) (set: Set) : Fact list =
        let names = Vocabulary.foldParticipants (fun participant found -> found || participant = character) false

        [ for fact, _ in Map.toList set do
            if names fact then yield fact ]

    /// Every current fact, in the order the values have: by construct, then by the arguments. That is
    /// the order the set is already stored in, so this is a comprehension rather than a sort — and
    /// ordering by values rather than by their text is what keeps the engine from needing a rendering
    /// to be deterministic (research D34).
    let toList (set: Set) : Fact list = [ for fact, _ in Map.toList set -> fact ]

    /// A construct as a matcher: its kind, and the deconstruction that reads its arguments,
    /// declared together once so the two cannot be paired up wrongly at a call site. `Kind` is
    /// what a rule records as read and what the participant index is keyed by; `Deconstruct` is
    /// what binds the value's arguments. One per construct a rule starts from: the cost of
    /// keeping rules typed rather than turning every rule's bindings into `obj` (research D24).
    type Construct<'T> =
        { Kind : FactKind
          Deconstruct : Fact -> 'T option }

    /// The constructs rules start from, each declared once. Every source — plain or keyed — is
    /// built from one of these, so the kind a source is indexed by and the shape it binds cannot
    /// disagree.
    module Constructs =

        let attractedTo : Construct<struct (Character * Character)> =
            { Kind = FactKind.AttractedTo
              Deconstruct = function Fact.AttractedTo (a, b) -> Some(struct (a, b)) | _ -> None }

        let jealousOf : Construct<struct (Character * Character)> =
            { Kind = FactKind.JealousOf
              Deconstruct = function Fact.JealousOf (a, b) -> Some(struct (a, b)) | _ -> None }

        let friendshipThreatened : Construct<struct (Character * Character)> =
            { Kind = FactKind.FriendshipThreatened
              Deconstruct = function Fact.FriendshipThreatened (a, b) -> Some(struct (a, b)) | _ -> None }

        let inRelationship : Construct<struct (Character * Character)> =
            { Kind = FactKind.InRelationship
              Deconstruct = function Fact.InRelationship (a, b) -> Some(struct (a, b)) | _ -> None }

        let friends : Construct<struct (Character * Character)> =
            { Kind = FactKind.Friends
              Deconstruct = function Fact.Friends (a, b) -> Some(struct (a, b)) | _ -> None }

        let hasTrait : Construct<struct (Character * Trait)> =
            { Kind = FactKind.HasTrait
              Deconstruct = function Fact.HasTrait (a, ascribed) -> Some(struct (a, ascribed)) | _ -> None }

    /// A source enumerates the current facts of one construct, paired with the fact itself
    /// so a premise needs no reconstruction.
    type Source<'T> =
        { Kind : FactKind
          Enumerate : Set -> ParticipantIndex -> (Fact * 'T) list }

    /// A source over every current fact of the construct.
    let sourceOf (construct: Construct<'T>) : Source<'T> =
        { Kind = construct.Kind
          Enumerate =
            fun set _ ->
                [ for fact, _ in Map.toList set do
                    match construct.Deconstruct fact with
                    | Some v -> yield fact, v
                    | None -> () ] }

    /// A source restricted to the construct's facts naming a given participant, in either
    /// position. This is what lets a join use an argument it has already bound, and it is also
    /// what makes a premise match a mutual relation however its names happened to be ordered when
    /// it was recorded (research D22).
    let namedIn (construct: Construct<'T>) (participant: Character) : Source<'T> =
        { Kind = construct.Kind
          Enumerate =
            fun _ index ->
                [ for fact in ParticipantIndex.factsOf construct.Kind participant index do
                    match construct.Deconstruct fact with
                    | Some v -> yield fact, v
                    | None -> () ] }

    module Sources =

        let attractedTo : Source<struct (Character * Character)> = sourceOf Constructs.attractedTo

        let jealousOf : Source<struct (Character * Character)> = sourceOf Constructs.jealousOf

        let friendshipThreatened : Source<struct (Character * Character)> = sourceOf Constructs.friendshipThreatened

        // Keyed sources: a construct restricted to the facts naming a given character.
        // A rule uses these for the premise that a bound argument can settle.

        /// `partnersOf b` — the relationships naming b, in either position.
        let partnersOf (character: Character) : Source<struct (Character * Character)> =
            namedIn Constructs.inRelationship character

        /// `friendsOf a` — the friendships naming a, in either position.
        let friendsOf (character: Character) : Source<struct (Character * Character)> =
            namedIn Constructs.friends character

        /// `traitsOf a` — the traits recorded for a.
        let traitsOf (character: Character) : Source<struct (Character * Trait)> =
            namedIn Constructs.hasTrait character
