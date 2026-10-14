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
