module EnvironmentAsset

// Shared static asset for environments and props: manifest metadata plus its
// loaded runtime data (the parsed glTF submeshes, uploaded as static GPU
// buffers). Mirrors odin-game's `Environment_Asset`/`Prop_Asset`. One type serves
// both kinds; a prop asset is a static model with identity orientation and no
// parts, while an environment also carries its parts (one per glTF node with a
// mesh), which `Zone.setEnvironment` turns into props.

open System.Numerics
open StaticMesh

// One static GPU submesh plus its parsed material base color.
type StaticSubmesh =
    { Mesh : StaticMesh
      BaseColor : Vector4 }

// One glTF node with a mesh, ready to become a prop. `World` is the node's
// accumulated transform (applied as the prop's entity transform); `SubmeshIndex`/
// `SubmeshCount` slice the shared `Submeshes` array.
type EnvironmentPart =
    { Name : string
      HierarchyPath : string
      World : Matrix4x4
      SubmeshIndex : int
      SubmeshCount : int }

type EnvironmentAsset =
    { Id : string
      Name : string
      ModelPath : string
      Orientation : Quaternion
      Submeshes : StaticSubmesh array
      Parts : EnvironmentPart array }
