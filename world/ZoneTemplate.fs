module ZoneTemplate

// One registered zone: the environment asset to draw, the environment's own settings, the zone's own
// light, and its parts as the author left them. This is what a `world/ZoneRegistry.fs` entry holds
// (spec 034).
//
// It is a copy taken at a moment rather than a link to the zone it came from, which is what lets a
// registration outlive the zone it was registered from. A part is recorded as a `PropState`: the
// authored value `Zone.capturePart` answers and `PartCreate`/`PartDelete` already carry, so a
// registered part is not a fourth spelling of the same four fields.
//
// Listed before `world/Zone.fs` in the compile list, because the loader there takes one as its
// argument.
//
// `ZoneTemplate` is the ref-free half of a zone's authored state (a *recipe*), where `ZoneContents` in
// `world/Zone.fs` is the identity-preserving snapshot; `docs/ZONE_MODEL_DECISION.md` records the relationship and
// recommends not growing this toward the snapshot without deciding the direction first.

open System.Numerics
open Entity

/// One registered zone. `Base` and `Orientation` are the environment's *own* settings — its name and
/// where it sits — as opposed to a part's, and they are grouped apart from `Parts` so that a further
/// setting joins in one place (`docs/ARCHITECTURE.md`'s `settings`).
///
/// `Parts` and `Props` are different things and are kept in different fields: a part is a piece of the
/// environment asset and has no existence without its environment, where a prop is an asset of its own
/// standing in the zone. Both are `PropState`s, and which one an entry is is said by its `AssetId`
/// (`Some` for a prop, `None` for a part) — the same field the renderer already reads to tell them
/// apart.
type ZoneTemplate =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      // The zone's own light (spec 043): a sun pitch, sun yaw, colour and ambient, carried with the
      // environment so a registration is reproducible. Unlike `Base` and `Orientation`, this is the
      // *zone's* setting rather than the environment's — an environment has no light of its own.
      Light : Texture.LightSettings
      Parts : PropState array
      Props : PropState array }

/// The record of a loaded environment together with the props standing in the zone that holds it, part
/// for part and prop for prop, in the order each map holds them. Reading the live maps is what makes a
/// registration the zone as the author left it: a part or prop she deleted is absent from the array,
/// and one she duplicated appears twice.
///
/// The props are an argument rather than something read here, because a prop belongs to the zone and
/// this file is compiled above `world/Zone.fs` — which is also what lets a record be built in a test
/// without a scene.
let ofEnvironment (environment: Environment) (props: PropState array) (light: Texture.LightSettings) : ZoneTemplate =
    let parts = ResizeArray<PropState>(int environment.Parts.Count)
    environment.Parts.IteriAlive (fun _ _ part -> parts.Add part.State)
    { AssetId = environment.AssetId
      Base = environment.Base
      Orientation = environment.Orientation
      Light = light
      Parts = parts.ToArray()
      Props = props }
