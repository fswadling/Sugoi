module Assets

// Asset manager: scans actors/environments/props subdirectories for JSON
// manifests and returns the discovered assets, keyed by id.

open System.IO
open System.Text.Json
open Entity

type Asset =
    { Id : string
      Name : string
      Kind : EntityKind }

// The three renderable asset kinds and their on-disk subdirectory names.
let private kindDirs =
    [ "actors", EntityKind.Actor
      "environments", EntityKind.Environment
      "props", EntityKind.Prop ]

// Parse one JSON manifest. Raises on malformed JSON; a missing id/name falls
// back to the file stem.
let private readManifest (path: string) (kind: EntityKind) : Asset =
    use stream = File.OpenRead(path)
    use doc = JsonDocument.Parse(stream)
    let root = doc.RootElement
    let id =
        match root.TryGetProperty("id") with
        | true, p -> p.GetString()
        | _ -> Path.GetFileNameWithoutExtension(path)
    let name =
        match root.TryGetProperty("name") with
        | true, p -> p.GetString()
        | _ -> id
    { Id = id; Name = name; Kind = kind }

let scan (root: string) : Asset array =
    [| for (dir, kind) in kindDirs do
        let full = Path.Combine(root, dir)
        if Directory.Exists(full) then
            for file in Directory.EnumerateFiles(full, "*.json") do
                yield readManifest file kind |]
