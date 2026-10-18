# Assets

Every asset is a directory holding a small s-syntax **manifest** (a `*.manifest` file). The asset
manager scans `assets/actors`, `assets/props`, `assets/environments` and `assets/audio`, reads each
manifest it finds, and loads the asset under its `Id`.

The `assets/` tree is **not tracked by git** (`assets/.gitignore`): payloads and their manifests are
local, copied or generated from the source distribution, so the repo carries code and specs rather
than large binaries. Editors that hide gitignored files can be told to show this tree via
`file_scan_inclusions` in `.zed/settings.json`.

## Actor manifests

An actor manifest names the model and, optionally, a morph sidecar and a portrait set:

```text
[[Id "akane"] [Name "Akane"] [Model "actors/Akane/Akane.iqm"] [Morphs "actors/Akane/akane.morphs"]
 [Portraits [[Neutral "actors/Akane/portraits/AkaneNeutral.png"]
             [Happy   "actors/Akane/portraits/AkaneHappy.png"]
             [Sad     "actors/Akane/portraits/AkaneSad.png"]
             [Angry   "actors/Akane/portraits/AkaneAngry.png"]
             [Surprised "actors/Akane/portraits/AkaneSurprised.png"]]]]
```

| Field | Meaning |
|-------|---------|
| `Id` | The asset's id; defaults to the file stem. Used where an asset is named **directly** — the cue `AddActor` op, the Asset Browser. |
| `Name` | The actor's name; defaults to `Id`. **This is the name a person uses**, and the name a cue and a portrait line address her by. |
| `Model` | The IQM model path (required). |
| `Morphs` | Optional morph sidecar path. |
| `Portraits` | Optional — one image path per expression (`Neutral`, `Happy`, `Sad`, `Angry`, `Surprised`). |

### The portrait contract

A portrait line (`Exposit`, kind *Portrait*) names a **speaker** and an expression, and the speaker is
resolved to an actor asset **by its `Name`**, matched exactly. So, for a character to speak a portrait
line:

1. give her actor asset a `Name` equal to the speaker string (`[Name "Akane"]`), and
2. declare its `Portraits`, one per expression.

Three consequences follow, and they are intentional:

- A character need not exist in the scene, or be bound to anything, to speak a portrait line — the
  speaker resolves against the discovered assets, not the world.
- Declaring **no** `[Portraits …]` block is legal: the actor simply cannot speak a portrait line.
- **A partial** set (some expressions, not all) is a broken asset and fails at load.
- If no asset is named for the speaker, the line is left undrawn and the Dialogue panel explains it —
  an expected authoring state, never a failure.

## Building IQM actor models

Sugoi loads IQM v2 models. The authoring tooling lives in `tools/`:

- `tools/iqm_export_with_morphs.py` — a Blender 4.1+ add-on that exports an IQM
  and, when the model has shape keys, `morphs.json` + `morphs.bin` sidecars.
- `tools/gltf_to_iqm.py` — converts a skinned `.gltf` to an IQM without Blender
  (geometry, joints and animations).
- `tools/extract_gltf_morphs.py` — extracts shape-key deltas from a `.gltf` when
  the morph data already exists in glTF form.
- `tools/morphs_to_sugoi.py` — converts the exporter's JSON/BIN sidecars into
  Sugoi's s-expression `*.morphs` manifest and packed `*.morphs.bin`.

A glTF can be converted directly, without Blender:

```sh
python3 tools/gltf_to_iqm.py assets/actors/Shinji/Shinji.gltf \
    -o assets/actors/Shinji/Shinji.iqm
```

The converter maps glTF's Y-up axes to IQM's Z-up axes, so extract the morph
deltas from the same glTF with `--z-up`:

```sh
python3 tools/extract_gltf_morphs.py assets/actors/Shinji/Shinji.gltf --z-up \
    --json-out assets/actors/Shinji/morphs.json \
    --bin-out assets/actors/Shinji/morphs.bin
python3 tools/morphs_to_sugoi.py assets/actors/Shinji/morphs.json \
    assets/actors/Shinji/Shinji.iqm \
    --morphs Fcl_ALL_Neutral,Fcl_ALL_Joy,Fcl_ALL_Angry,Fcl_ALL_Sorrow,Fcl_ALL_Surprised,Fcl_ALL_Fun,Fcl_EYE_Close,Fcl_MTH_A \
    -o assets/actors/Shinji/shinji.morphs
```

With the Blender exporter, convert and choose up to eight morphs, in the order
they should appear:

```sh
python3 tools/morphs_to_sugoi.py \
    assets/actors/Akane/morphs.json \
    assets/actors/Akane/Akane.iqm \
    --morphs Fcl_ALL_Neutral,Fcl_ALL_Joy,Fcl_ALL_Angry,Fcl_ALL_Sorrow,Fcl_ALL_Surprised,Fcl_ALL_Fun,Fcl_EYE_Close,Fcl_MTH_A \
    -o assets/actors/Akane/akane.morphs
```

The actor manifest names the model and, optionally, its converted morph manifest:

```text
[[Id "akane"] [Name "Akane"] [Model "actors/Akane/Akane.iqm"]
 [Morphs "actors/Akane/akane.morphs"]]
```

`--list-morphs` lists what the exporter produced. Without `--morphs`, every
morph is used, which fails if there are more than eight (the shader's cap).
A model with no morphs needs only the `.iqm`. The exporter's `morphs.json` and
`morphs.bin` are intermediates; `morphs.json` is ignored by git. The engine's
`<name>.morphs.bin` is a payload too, so force-add it (`git add -f`) if you
version the actor's model.

## Audio manifests

An audio manifest names its source file and its **class**. Sounds live under `assets/audio/sounds`,
music under `assets/audio/music`; the manager scans `assets/audio` recursively and loads each
manifest under its `Id`.

```text
[[Name "Door Open"]  [File "audio/sounds/door-open.wav"]  [Kind "Sound"]]
[[Name "Town Theme"] [File "audio/music/town-theme.ogg"] [Kind "Music"]]
```

| Field | Meaning |
|-------|---------|
| `Id` | The asset's id; defaults to the manifest file stem. |
| `Name` | The asset's name; defaults to `Id`. |
| `File` | The source file, relative to `assets/` (required). |
| `Kind` | `Sound` or `Music` (required). |

A **Sound** is decoded and held in memory at startup, so it is ready to play; a **Music** track is
left on disk and only probed once, so a long track costs no memory until it is played (spec 086).
All of SDL_mixer's formats are accepted without per-format handling — WAV, OGG Vorbis, Opus, FLAC,
MP3 and the module/MIDI formats.
