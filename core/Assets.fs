module Assets

// Asset manager: scans actors/environments/props subdirectories for s-syntax
// manifests (parsed via Prime's symbolic converter) and returns the discovered
// assets, keyed by id.

open System
open System.IO
open Prime
open Entity

type Asset =
    { Id : string
      Name : string
      Kind : EntityKind }

// The on-disk manifest shape. Kind is derived from the subdirectory, so it
// lives on Asset rather than here. SymbolicExpansion makes fields named
// (e.g. [[Id "akane"] [Name "Akane"]]), robust to reordering.
[<SymbolicExpansion>]
type private Manifest =
    { Id : string
      Name : string }

// The three renderable asset kinds and their on-disk subdirectory names.
let private kindDirs =
    [ "actors", EntityKind.Actor
      "environments", EntityKind.Environment
      "props", EntityKind.Prop ]

// Parse one s-syntax manifest. Raises on malformed syntax; a missing id/name
// falls back to the file stem.
let private readManifest (path: string) (kind: EntityKind) : Asset =
    let stem = Path.GetFileNameWithoutExtension(path)
    let manifest = File.ReadAllText(path) |> scvalue<Manifest>
    let id = if String.IsNullOrEmpty manifest.Id then stem else manifest.Id
    let name = if String.IsNullOrEmpty manifest.Name then id else manifest.Name
    { Id = id; Name = name; Kind = kind }

let scan (root: string) : Asset array =
    [| for (dir, kind) in kindDirs do
        let full = Path.Combine(root, dir)
        if Directory.Exists(full) then
            for file in Directory.EnumerateFiles(full, "*.manifest") do
                yield readManifest file kind |]
