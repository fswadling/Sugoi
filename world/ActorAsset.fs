module ActorAsset

// Shared per-actor asset: manifest metadata plus its loaded runtime data (parsed
// IQM model + uploaded GPU mesh + its morph targets + its portraits). Mirrors
// odin-game's `Actor_Asset`. Split out of `Assets` (the manager) so `ActorRender`
// can depend on this type without a cycle back into the manager.

open Prime

// ── morph data (spec 037) ──

// The on-disk morph manifest: a list of [Field value] pairs, like every other manifest.
// Its `Morphs` is an array of morphs, each a set of targets over the vertex buffer.
[<SymbolicExpansion>]
type MorphTarget =
    { FirstVertex : int          // absolute index into the actor's combined vertex buffer
      VertexCount : int
      HasNormalDeltas : bool
      PositionOffset : int       // offset in float32s into the data file
      NormalOffset : int }

[<SymbolicExpansion>]
type MorphDefinition =
    { Name : string
      Targets : MorphTarget array }

[<SymbolicExpansion>]
type MorphManifest =
    { Version : int
      Data : string              // the data file, relative to the manifest
      Morphs : MorphDefinition array }

// One actor's morphs, loaded: the names (a morph's index is its array index) and the
// per-vertex offset buffer, uploaded once and shared by every instance of the asset.
type ActorMorphs =
    { Names : string array
      Count : int
      Buffer : Gpu.Buffer }

// ── portraits (spec 038) ──

/// The portrait paths an actor manifest declares, one per expression. Five **named** fields
/// rather than an emotion-keyed map, because this module is compiled before the dialogue one
/// (which owns `Emotion`) and because it is the reference's own shape. An absent field parses
/// to `null`.
[<SymbolicExpansion>]
type PortraitPaths =
    { Neutral : string
      Happy : string
      Sad : string
      Angry : string
      Surprised : string }

/// The portraits an actor declares, loaded once and shared by every instance of the asset.
type ActorPortraits =
    { Neutral : Texture.Texture
      Happy : Texture.Texture
      Sad : Texture.Texture
      Angry : Texture.Texture
      Surprised : Texture.Texture }

type ActorAsset =
    { Id : string
      Name : string
      ModelPath : string
      Bounds : Gltf.Bounds
      Mesh : Mesh.Mesh
      Model : Actor.ActorModel
      Morphs : ActorMorphs option
      /// The actor's portraits, one per expression, loaded once (spec 038). `None` when the
      /// manifest declares no `[Portraits …]` block, which is legal: such a character simply
      /// cannot speak a portrait line.
      Portraits : ActorPortraits option
      /// The actor's sway chains (spec 091), resolved against its skeleton once and shared by every
      /// instance. A chain that could not resolve was reported and dropped at load, so this holds only
      /// usable chains — `[||]` when the manifest declares none, which is the common case.
      SwayChains : Sway.SwayChainDef array }

// ── pure loaders ──

// The manifest version this loader understands.
[<Literal>]
let MorphManifestVersion = 1

/// The directory an actor's textures resolve from: the manifest's declared root-relative directory
/// when present, otherwise the model's own directory.
let textureDirectory (modelDirectory: string) (declared: string) : string =
    if System.String.IsNullOrEmpty declared then modelDirectory else declared

// Parse a morph manifest's text. Raises on a malformed document or an unknown version.
let parseMorphManifest (text: string) : MorphManifest =
    let manifest = scvalue<MorphManifest> text
    if manifest.Version <> MorphManifestVersion then
        failwithf "morph manifest: unsupported version %d (expected %d)" manifest.Version MorphManifestVersion
    manifest

// The morphs a manifest actually describes: a morph with no targets describes nothing to
// displace, so it is absent rather than held at a weight that does nothing (spec 037 edge case).
let usableMorphs (manifest: MorphManifest) : MorphDefinition array =
    manifest.Morphs |> Array.filter (fun m -> m.Targets.Length > 0)

// Resolve a morph set against its data file into the dense per-vertex layout the vertex shader
// reads: for each morph m and vertex v, two vec4 samples — a position offset and a normal offset —
// at float index (v * morphCount + m) * 8. A target with no normal offsets packs a zero normal.
// Raises on a range outside the mesh, a data file too short for the offsets named, or more morphs
// than the vertex stage can weigh (Gpu.MorphsMax).
let buildMorphDeltas (morphs: MorphDefinition array) (data: float32 array) (vertexCount: int) : float32 array =
    let morphCount = morphs.Length
    if morphCount > Gpu.MorphsMax then
        failwithf "morph manifest: %d morphs exceeds the maximum of %d" morphCount Gpu.MorphsMax

    let deltas = Array.zeroCreate (vertexCount * morphCount * 8)
    for m in 0 .. morphCount - 1 do
        let morph = morphs.[m]
        for target in morph.Targets do
            if target.FirstVertex < 0 || target.VertexCount < 0 || target.FirstVertex + target.VertexCount > vertexCount then
                failwithf "morph manifest: morph '%s' has a target range [%d, %d) outside the mesh's %d vertices" morph.Name target.FirstVertex (target.FirstVertex + target.VertexCount) vertexCount

            for i in 0 .. target.VertexCount - 1 do
                let floatBase = ((target.FirstVertex + i) * morphCount + m) * 8
                let p = target.PositionOffset + i * 3
                if p < 0 || p + 2 >= data.Length then
                    failwithf "morph manifest: morph '%s' position offsets run past the data file (%d floats)" morph.Name data.Length
                deltas.[floatBase] <- data.[p]
                deltas.[floatBase + 1] <- data.[p + 1]
                deltas.[floatBase + 2] <- data.[p + 2]

                if target.HasNormalDeltas then
                    let n = target.NormalOffset + i * 3
                    if n < 0 || n + 2 >= data.Length then
                        failwithf "morph manifest: morph '%s' normal offsets run past the data file (%d floats)" morph.Name data.Length
                    deltas.[floatBase + 4] <- data.[n]
                    deltas.[floatBase + 5] <- data.[n + 1]
                    deltas.[floatBase + 6] <- data.[n + 2]

    deltas

/// The five portrait fields, paired with their names, so a gap can be named in the failure.
let portraitValues (p: PortraitPaths) : (string * string) list =
    [ "Neutral", p.Neutral
      "Happy", p.Happy
      "Sad", p.Sad
      "Angry", p.Angry
      "Surprised", p.Surprised ]

/// A portrait set is all-or-nothing: a `[Portraits …]` block with some expressions declared and
/// others not is a broken asset, reported at load naming what is missing (FR-006, SC-008). A set
/// with no paths at all is not a failure — the caller treats it as "declares no portraits" — and
/// is not this function's concern.
let requireComplete (p: PortraitPaths) : unit =
    let values = portraitValues p
    let empty = values |> List.filter (fun (_, v) -> System.String.IsNullOrEmpty v)
    if empty.Length > 0 && empty.Length < values.Length then
        let missing = empty |> List.map fst |> String.concat ", "
        failwithf "actor portraits: the declared set is missing %s" missing
