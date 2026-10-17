module NarrativeTestSupport

// T027: the MVP rule set, assembled from the units. It lives here rather than in the
// library because the rule set is data the caller supplies — the game will supply its own
// units — so the engine embeds no content (contracts/engine-api.md § Harness).

open Narrative
open Roster

let aiko = Aiko
let akane = Akane
let shinji = Shinji

// T008/F-79: the mechanism tests share one set of test-only projections. They are deliberately
// separate from the units: the mechanism suites must not depend on the production rule set, but
// they do not each need their own copy of the same four declarations.

let befriended : Projection =
    Rules.projection
        "befriended"
        { EstablishedBy =
            function
            | FriendshipStarted (a, b) -> ValueSome(struct (a, b))
            | _ -> ValueNone
          WithdrawnBy =
            Some(
                function
                | FriendshipEnded (a, b) -> ValueSome(struct (a, b))
                | _ -> ValueNone
            )
          Yield = fun (struct (a, b)) -> Fact.Friends(a, b) }

let attraction : Projection =
    Rules.projection
        "crush-started"
        { EstablishedBy =
            function
            | CrushStarted (a, b) -> ValueSome(struct (a, b))
            | _ -> ValueNone
          WithdrawnBy = None
          Yield = fun (struct (a, b)) -> Fact.AttractedTo(a, b) }

let relationship : Projection =
    Rules.projection
        "together"
        { EstablishedBy =
            function
            | RelationshipStarted (a, b) -> ValueSome(struct (a, b))
            | _ -> ValueNone
          WithdrawnBy = None
          Yield = fun (struct (a, b)) -> Fact.InRelationship(a, b) }

let traitAssigned : Projection =
    Rules.projection
        "trait-assigned"
        { EstablishedBy =
            function
            | TraitAssigned (a, ascribed) -> ValueSome(struct (a, ascribed))
            | _ -> ValueNone
          WithdrawnBy = None
          Yield = fun (struct (a, ascribed)) -> Fact.HasTrait(a, ascribed) }

let private units = [ Cast.rules; Romance.rules; Friendship.rules ]

/// Every unit's rules as one rule set. Where they sit relative to each other is not
/// observable when it runs (FR-019).
let mvpRules : RuleSet = Rules.combine units

/// The headline situation (quickstart § 1) as pre-existing events, each character
/// arriving first (FR-003).
let headline : (GameTime * Event) list =
    [ (GameTime -1000, Arrived aiko)
      (GameTime -1000, Arrived akane)
      (GameTime -1000, Arrived shinji)
      (GameTime -900, TraitAssigned(aiko, Shy))
      (GameTime -800, FriendshipStarted(aiko, akane))
      (GameTime -300, CrushStarted(aiko, shinji))
      (GameTime -200, CrushStarted(shinji, akane))
      (GameTime -100, RelationshipStarted(shinji, akane)) ]

let headlineEngine () : Engine.Engine = List.fold Engine.commit (Engine.create mvpRules) headline
