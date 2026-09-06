# Contract: PBR + Lighting

Public surfaces added or changed by this feature. Signatures are illustrative F#,
matching the data model.

## glTF — `core/Gltf.fs`

```fsharp
type TextureRef = { Uri : string }

type Material =
    { BaseColor : Vector4
      Metallic : float32
      Roughness : float32
      Emissive : Vector3
      AlbedoTexture : TextureRef option
      MetallicRoughnessTexture : TextureRef option
      NormalTexture : TextureRef option
      EmissiveTexture : TextureRef option }

type Model =
    { Submeshes : Submesh array
      Parts : Part array
      Materials : Material array
      Bounds : Bounds }

val load : path:string -> Model
```

## Texture — `render/Texture.fs`

```fsharp
type Texture =
    { Device : Gpu.Device
      Handle : Gpu.Texture
      Sampler : Gpu.Sampler }

val load : device:Gpu.Device -> path:string -> Texture
val solid : device:Gpu.Device -> r:byte -> g:byte -> b:byte -> a:byte -> Texture
```

## Material + Light — `render/Texture.fs`

```fsharp
type Material =
    { BaseColor : Vector4
      Metallic : float32
      Roughness : float32
      Emissive : Vector3
      HasAlbedo : bool
      HasNormal : bool
      HasMetallicRoughness : bool
      HasEmissive : bool
      Bindings : SDL.GPUTextureSamplerBinding array }

val makeMaterial : white:Texture -> baseColor:Vector4 -> metallic:float32 ->
  roughness:float32 -> emissive:Vector3 -> albedo:Texture option ->
  normal:Texture option -> metallicRoughness:Texture option ->
  emissiveTex:Texture option -> Material

val defaultMaterial : white:Texture -> baseColor:Vector4 -> Material

[<Struct>]
type Light =
    { Direction : Vector3
      Color : Vector3
      Ambient : Vector3 }
```

## Rules

- `Gltf.load` parses `pbrMetallicRoughness`/`emissive`/texture refs with glTF
  defaults; embedded images (`bufferView` and `data:` URI) raise; a material with
  no texture ref is valid.
- `Texture.load` decodes via `SDL3.Image.LoadGPUTexture` into `R8G8B8A8Unorm` and
  creates a linear/repeat sampler; `Assets` caches by URI.
- The static and skinned fragment shaders implement the same GGX BRDF
  (`D_GGX`/`F_Schlick`/`G_Smith`) with sRGB decode on albedo/emissive and a single
  gamma encode at output; normal/roughness maps are linear.
- `metallicRoughnessTexture` is sampled raw: roughness = `roughnessFactor × .g`,
  metallic = `metallicFactor × .b`.
- Normals use a per-draw inverse-transpose `mat3`; normal maps use a derivative
  tangent frame.
- A missing/unsupported referenced image fails loudly; a texture-less material
  falls back to its factors.
- The actor shades dielectric under the same light; each IQM mesh names an albedo
  texture (its `Material` string) that is sampled when present, with a default
  dielectric material as fallback.
