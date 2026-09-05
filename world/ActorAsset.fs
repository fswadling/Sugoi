module ActorAsset

// Shared per-actor asset: manifest metadata plus its loaded runtime data (parsed
// IQM model + uploaded GPU mesh). Mirrors odin-game's `Actor_Asset`. Split out of
// `Assets` (the manager) so `ActorRender` can depend on this type without a cycle
// back into the manager.

type ActorAsset =
    { Id : string
      Name : string
      ModelPath : string
      Mesh : Mesh.Mesh
      Model : Actor.ActorModel }
