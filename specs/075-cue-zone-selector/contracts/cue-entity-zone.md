# Contract: An Explicit Zone on Every Entity Cue Op

What a cue says about the zone of an entity-naming op (`specs/075`), and what the editor offers for it.

## The ops — `world/Cue.fs`

```fsharp
// gained a zone (first argument)
| RemoveActor of place: Place * actor: string
| Animate of place: Place * actor: string * clip: string * weight: float32 * duration: float
| CrossFade of place: Place * actor: string * fromClip: string * toClip: string * weight: float32 * duration: float
| Morph of place: Place * actor: string * morph: string * weight: float32 * duration: float
| CrossMorph of place: Place * actor: string * fromMorph: string * toMorph: string * weight: float32 * duration: float
| EnablePlayer of place: Place * actor: string
| DisablePlayer of place: Place * actor: string
| SetCamera of place: Place * waypoint: string
| FlyCamera of place: Place * path: string * speed: float

// unchanged — already name a place, or are zone-implicit by design
| AddActor of place: Place * actor: string * asset: string * waypoint: string
| EnableTrigger of place: Place * trigger: string | DisableTrigger of place: Place * trigger: string
| SetZone of place: Place | AddZone of place: Place | RemoveZone of place: Place | PreloadZone of place: Place
| MoveActor of actor: string * path: string * speed: float * movingClip: string * idleClip: string
| RotateActor of actor: string * waypoint: string * duration: float
// ... and every …State case (BlendState/MoveActorState/RotateActorState already carry place; FlyCameraState captures its points)
```

## Resolution — in the named place

- **Actor ops** (`RemoveActor`, the four blends, the player toggles): the first live actor whose `Name`
  equals the op's string **in the op's place's zone** (`Zone.tryActorByName`). An absent place, or a name not
  in it, ⇒ the op performs nothing and the run continues.
- **Camera ops** (`SetCamera`, `FlyCamera`): the named waypoint/path in the op's place's zone. A miss, or an
  absent place, ⇒ **raises** (`specs/054` FR-009).
- **`MoveActor`/`RotateActor`**: no zone; the actor of that name in the **active zone**; a miss ⇒ nothing.
- **`AddActor` / trigger toggles / zone ops**: unchanged.

There is no longer a scan of the present places: an op's zone is the zone it names.

## The editor

- **Zone selector**: each of the nine ops shows a **place combo** (the closed `Place.all` set, matching the
  zone/trigger ops' combo), writing the op's place as one step on the one undo history. A newly-created op's
  place defaults to the active zone.
- **Entity choosers**: the actor chooser, the waypoint chooser and the clip/morph choosers read the **op's
  place's zone** (matching the run-time resolution). When the place is absent the chooser is not drawn (the
  name it holds is preserved); the clip/morph fields fall back to typing.
- **`MoveActor`/`RotateActor`**: no zone selector; their choosers read the active zone.
- **Labels**: an entity op's row shows its place (`Camera: balcony in Island`).

## Postconditions

- **Unambiguous target**: an op acts on the entity of its name **in its own zone** (spec SC-001).
- **Default is the current zone**: a new op's place is the active zone (spec SC-002).
- **One step**: changing an op's zone records exactly one undo step (spec SC-003).
- **In-flight stability**: a begun op keeps the place it began in (spec SC-004).
- **Motion unchanged**: `MoveActor`/`RotateActor` remain active-zone-only (spec FR-009).

## Explicitly unchanged / out of scope

- **`AddActor`, the trigger toggles and the zone ops** — already name a place.
- **The cue engine's structure** (`advance`/`tick`, the `…State` in-flight cases) — unchanged in shape.
- **The command/undo model** — cue ops are runtime-only; no `Command` case changes.
- **No op added or removed** — only nine ops' argument lists change and one resolver leaves `GameplayState`.
