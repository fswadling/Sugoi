# Data Model: Zone Light Settings

The feature adds one authored record, one field on two existing records, one command variant and
one reused editor buffer. Nothing is persisted, and no authored file format changes.

## `LightSettings` — new, in `render/Texture.fs`

```fsharp
/// A zone's authored sun (spec 043): a pitch and yaw in degrees, a light colour and an ambient
/// intensity — the four values `odin-game` stores on a zone (`src/zone_registry.odin`).
[<Struct>]
type LightSettings =
    { Pitch : float32      // degrees above the horizon: 0 = horizon, 90 = zenith
      Yaw : float32        // compass degrees, 0–360
      Color : Vector3      // linear RGB, 0–1
      Ambient : float32 }  // a single intensity (neutral: expanded to (a, a, a))
```

### Default — reproduces today's hard-coded light (research D4)

```fsharp
let defaultLight : LightSettings =
    { Pitch = 54.7356f      // asin(0.8 / sqrt(0.96)) — the pitch of normalize(0.4, 0.8, 0.4)
      Yaw = 45.0f           // atan2(0.4, 0.4)
      Color = Vector3(1.0f, 1.0f, 1.0f)
      Ambient = 0.15f }
```

A zone created by `Zone.create` starts here, so an unedited scene lights identically to the
constant it replaces (FR-004, SC-002).

### Derivation — `toLight` (research D3)

```fsharp
/// The shader-facing light for a zone's authored settings: the sun direction is derived from the
/// pitch and yaw with `odin-game`'s convention (`src/environment_render.odin:240`), and the scalar
/// ambient expands to the port's neutral vector.
val toLight : settings:LightSettings -> Light
```

| Output field | Formula |
|---|---|
| `Direction` | `normalize(sin(yaw).cos(pitch), sin(pitch), cos(yaw).cos(pitch))` |
| `Color` | the authored colour |
| `Ambient` | `(a, a, a)` where `a` is the authored ambient |

Pitch 0 / yaw 0 gives `(0, 0, 1)`; pitch 90 gives `(0, 1, 0)`; yaw 90 gives `(1, 0, 0)`. The
result is always a unit vector (FR-008), so no degenerate state exists.

### Authored ranges (FR-009)

| Setting | Range | Source |
|---|---|---|
| Pitch | 10 – 90 | the reference's editor slider (`src/editor_properties.odin:714`) |
| Yaw | 0 – 360 | the reference's editor slider |
| Ambient | 0.1 – 1.5 | the reference's editor slider |
| Colour | 0 – 1 per channel | a colour picker |

The ranges are enforced by the controls' bounds; the type itself does not clamp (a value read from
a template is taken as authored).

## `Zone` — `world/Zone.fs`

One new field, `mutable` like the zone-level state `Zone` already carries:

```fsharp
type Zone =
    { Name : string
      Actors : SlotMap<Actor>
      mutable Environment : Environment option
      // The zone's authored light (spec 043): pitch, yaw, colour and ambient, applied to the
      // scene's one sun. A live zone-level setting — not an entity, not an undo step (FR-006).
      mutable Light : LightSettings
      Props : SlotMap<Prop>
      ... }
```

`Zone.create` seeds it with `defaultLight`. The field is written through the `SetLight` command — by
the Inspector's committed edits and by the instantiate batch (research D7, D8).

## `ZoneTemplate` — `world/ZoneTemplate.fs`

One new field, so a registered place keeps the light it was authored under (FR-007, research D8):

```fsharp
type ZoneTemplate =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      // The zone's light, carried with the environment so a registration is reproducible: the
      // same four settings `Zone_Settings` records in `odin-game` (spec 034's "one place" for a
      // further setting).
      Light : LightSettings
      Parts : PropState array
      Props : PropState array }
```

`ofEnvironment` gains a `light` argument (its single constructor; the field must come from
somewhere, and threading it is honest where a hidden default would not be). `Zone.captureTemplate`
passes the live zone's `Light`. The five `ZoneTemplate.ofEnvironment` call sites in
`test/ZoneRegistryTests.fs` pass a light.

## `Command.SetLight` — `state/Command.fs`

The route by which a light change is undoable. It has **two** callers: the Inspector's committed
edits (FR-006, research D7) and the instantiate batch (FR-007, research D8).

```fsharp
| SetLight of Previous : LightSettings * Next : LightSettings
```

| | apply | undo |
|---|---|---|
| `SetLight` | `zone.Light <- next` | `zone.Light <- previous` |

The Inspector pushes `SetLight (before, after)` when a control is released or left
(`isItemDeactivatedAfterEdit`), so one drag is one step and two committed changes are two steps
(SC-007). `Spawning.instantiateZone` includes `SetLight (previousLight, template.Light)` in its
batch, in the position of the environment swap, so the whole gesture stays **one** undo step and
undo restores the previous light together with the previous environment and props (SC-004).

Because the undo stack is per zone (`GameplayState.UndoByZone`), a light change is undone in the
zone it was made in.

## Editor colour buffer — `state/EditorState.fs`

One reused triple for `igColorEdit3` (research D9):

```fsharp
| LightColorBuffer : float32 array   // size 3; reused, seeded from the zone's colour
```

Held on `EditorState` (a struct) rather than as a module singleton, following the file's stated
convention. It is seeded from `zone.Light.Color` while the picker is not active, exactly as the
Inspector's text buffers are seeded while their fields are not live.

## Authored versus runtime boundary

| | Authored | Runtime |
|---|---|---|
| Pitch, yaw, colour, ambient (live) | no — a zone's session state | yes — `Zone.Light` |
| Pitch, yaw, colour, ambient (recorded) | no — never on disk (no persistence) | yes — `ZoneTemplate.Light` |
| `Texture.Light` | no | derived once per frame from the active zone |
| Editor colour scratch buffer | no | yes — reused on `EditorState` |

Nothing is written to disk; `docs/ROADMAP.md` phase 15 owns persistence.

## Validation rules

- **Defaults reproduce the replaced constant.** `toLight defaultLight` equals
  `{ Direction = normalize(0.4, 0.8, 0.4); Color = (1,1,1); Ambient = (0.15,0.15,0.15) }` to float
  precision (FR-004, SC-002) — asserted in `test/LightTests.fs`.
- **The derivation is total and unit.** For any pitch/yaw the direction is a unit vector; the
  control ranges (above) keep pitch above the horizon and yaw within a turn (FR-008, FR-009).
- **Zones are independent.** The settings are a field of each `Zone`; nothing is shared between
  zones, so editing one cannot touch another (FR-005, SC-003).
- **The recorded light round-trips.** `captureTemplate` then `instantiateZone` restores the zone's
  four values exactly, in the same `Batch` as the environment and props, and undo restores the
  previous light (FR-007, SC-004).
- **A light change is one undo step per commit.** `SetLight` sets and restores the light, and the
  Inspector pushes it once per released/left control (FR-006, SC-007).
- **The zone is the Inspector's fallback subject.** The lighting section is drawn for the active
  zone when `state.Editor.Selection` is `None`, and an entity's section when it is `Some`; the two
  cannot appear together (FR-003, research D6).
- **The face light is untouched.** `toLight` feeds only the scene light; spec 040's idealised face
  direction is derived separately in `ActorRender` and never reads it (FR-010, SC-005).
- **No per-frame allocation.** The frame builds one `LightSettings`-derived `Light` from the
  active zone; the picker reuses its triple (FR-013, SC-006).
