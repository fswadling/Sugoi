module EnvironmentAsset

// Shared static asset for environments and props: manifest metadata plus its
// loaded runtime data (the parsed glTF submeshes, uploaded as static GPU
// buffers). Mirrors odin-game's `Environment_Asset`/`Prop_Asset`. One type serves
// both kinds; a prop is a static model with identity orientation.

open System.Numerics
open StaticMesh

// One static GPU submesh plus its parsed material base color.
type StaticSubmesh =
    { Mesh : StaticMesh
      BaseColor : Vector4 }

type EnvironmentAsset =
    { Id : string
      Name : string
      ModelPath : string
      Orientation : Quaternion
      Submeshes : StaticSubmesh array }
