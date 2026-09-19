module NarrativeTestSupport

// T027: the MVP rule set, assembled from the units. It lives here rather than in the
// library because the rule set is data the caller supplies — the game will supply its own
// units — so the engine embeds no content (contracts/engine-api.md § Harness).

open Narrative

let aiko = Aiko
let akane = Akane
let shinji = Shinji

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
