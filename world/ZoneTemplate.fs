module ZoneTemplate

// One registered zone: the environment asset to draw, the environment's own settings, and its parts
// as the author left them. This is what a `world/ZoneRegistry.fs` entry holds (spec 034).
//
// It is a copy taken at a moment rather than a link to the zone it came from, which is what lets a
// registration outlive the zone it was registered from. A part is recorded as a `PropState`: the
// authored value `Zone.capturePart` answers and `PartCreate`/`PartDelete` already carry, so a
// registered part is not a fourth spelling of the same four fields.
//
// Listed before `world/Zone.fs` in the compile list, because the loader there takes one as its
// argument.

open System.Numerics
open Entity

/// One registered zone. `Base` and `Orientation` are the environment's *own* settings — its name and
/// where it sits — as opposed to a part's, and they are grouped apart from `Parts` so that a further
/// setting joins in one place (`docs/ARCHITECTURE.md`'s `settings`).
type ZoneTemplate =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      Parts : PropState array }

/// The record of a loaded environment, taken part for part in the order the environment holds them.
/// Reading the live slot map is what makes a registration the zone as the author left it: a part she
/// deleted is absent from the array, and one she duplicated appears twice.
let ofEnvironment (environment: Environment) : ZoneTemplate =
    let parts = ResizeArray<PropState>(int environment.Parts.Count)
    environment.Parts.IteriAlive (fun _ _ part -> parts.Add part.State)
    { AssetId = environment.AssetId
      Base = environment.Base
      Orientation = environment.Orientation
      Parts = parts.ToArray() }
