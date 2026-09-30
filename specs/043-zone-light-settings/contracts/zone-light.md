# Contract: Zone Light Settings

The interfaces this feature touches, and what explicitly does not change. The scene's single sun is
authored per zone and read at the two places it is already consumed; the shaders, the cel/outline
pipelines and the static path are untouched.

## `render/Texture.fs` — CHANGED

Adds the authored record beside `Light` (data-model.md, research D2):

```fsharp
[<Struct>]
type LightSettings =
    { Pitch : float32; Yaw : float32; Color : Vector3; Ambient : float32 }

val defaultLight : LightSettings

val toLight : settings:LightSettings -> Light
```

`Light` itself is unchanged (`Direction` *toward* the sun, `Color`, `Ambient`).

## `render/ImGui.fs` — CHANGED

One new binding (the only new widget; `docs/EDITOR_DESIGN.md` D-017 sanctions `ColorEdit`):

```fsharp
[<DllImport("cimgui", EntryPoint = "igColorEdit3", CallingConvention = CallingConvention.Cdecl)>]
extern bool colorEdit3(string label, float32[] col, int flags)
```

`float32[]` marshals as `float col[3]` (blittable, writable), exactly as `inputText` already takes
a `byte[]`. `igDragFloat` is already bound and is reused for the three scalars.

## `world/Zone.fs` — CHANGED

- `Zone` gains `mutable Light : LightSettings`; `create` seeds `defaultLight`.
- `captureTemplate` passes the zone's `Light` to `ZoneTemplate.ofEnvironment`:

  ```fsharp
  Some (ZoneTemplate.ofEnvironment environment props zone.Light)
  ```

  Nothing else in the module changes: the light is not part of `Environment`, is not touched by
  `setEnvironment`/`loadEnvironment*`, and is not captured or released with any entity.

## `world/ZoneTemplate.fs` — CHANGED

```fsharp
type ZoneTemplate =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      Light : LightSettings
      Parts : PropState array
      Props : PropState array }

val ofEnvironment : environment:Environment -> props:PropState array -> light:LightSettings -> ZoneTemplate
```

The record's doc comment gains the light to the "the zone as the author left it" list, and notes it
is a *zone* setting rather than the environment's own (unlike `Base`/`Orientation`).

## `state/Command.fs` — CHANGED

```fsharp
| SetLight of Previous : LightSettings * Next : LightSettings
```

`apply` sets `zone.Light <- next`; `undo` sets `zone.Light <- previous`. The exhaustive matches in
`apply`/`undo` gain the arm. It has **two** callers: the Inspector's committed edits (research D7)
and the instantiate batch (research D8).

## `editor/Spawning.fs` — CHANGED

`instantiateZone` captures the previous light and includes the light change in its existing batch,
beside the environment swap:

```fsharp
let previousLight = zone.Light
...
let step =
    Batch ([ for (ref, prop) in replaced -> PropDelete (ref, prop) ]
           @ [ SetLight (previousLight, template.Light) ]
           @ [ SetEnvironment (previous, Some next) ]
           @ [ for (ref, prop) in placed -> PropCreate (ref, prop) ])
```

One gesture, still one undo entry (FR-007, spec 035 FR-005); undo restores the previous light.

## `editor/Inspector.fs` — CHANGED

A "Lighting" collapsible section drawn for the active zone when **no entity is selected** — the
Inspector's fallback subject (research D6, FR-003). Four controls, each committing **one** `SetLight`
step when it is released or left (`isItemDeactivatedAfterEdit`, the rule the Inspector's trigger
fields already use):

| Control | Binding | Bounds | Commit on release |
|---|---|---|---|
| Sun Pitch | `ImGui.dragFloat` | 10 – 90 | `SetLight (before, { before with Pitch = v })` |
| Sun Yaw | `ImGui.dragFloat` | 0 – 360 | `SetLight (before, { before with Yaw = v })` |
| Light Colour | `ImGui.colorEdit3` | 0 – 1 per channel | `SetLight (before, { before with Color = Vector3(r, g, b) })` |
| Ambient | `ImGui.dragFloat` | 0.1 – 1.5 | `SetLight (before, { before with Ambient = v })` |

`before` is the zone's light as it was when the control was first activated (read before the widget)
and the committed value is what the widget now holds, so one drag is one step (SC-007). The push
goes through `UndoStack.push` on the active zone's stack, exactly as the Inspector's other edits do.
The colour triple is the reused `state.Editor.LightColorBuffer` (research D9, data-model.md), seeded
from `zone.Light.Color` while the picker is not active.

## `editor/Outliner.fs` — CHANGED (one call site)

A zone row now calls `GameplayState.selectZone` instead of `switchZone`, which is what makes the
click always bring the zone into the Inspector. Nothing else in the panel changes.

## `state/GameplayState.fs` — CHANGED

```fsharp
val selectZone : shared:SharedState -> state:byref<GameplayState> -> name:string -> unit
```

Brings a zone up in the editor: switches to it when it is another zone (the switch already drops the
entity selection), and drops the entity selection when it is the zone already active — the case a
plain `switchZone` no-ops on, and the one that would otherwise leave a selected entity in the
Inspector while the author is clicking the zone's row (FR-003). It is on `GameplayState` rather than
in the panel because that makes it assertable headlessly (`test/GameplayStateTests.fs`); an ImGui
button handler is not.

## `state/EditorState.fs` — CHANGED

`LightColorBuffer : float32 array` (length 3) on `EditorState`, initialised once, reused by the
picker.

## `screen/GameplayScreen.fs` — CHANGED

The hard-coded `Light` is replaced by the active zone's:

```fsharp
let light = Texture.toLight zone.Light
```

`zone` is already bound in `renderScene`. Both consumers are unchanged and receive the same value:
`EnvironmentBatch.render … light …` and `ActorRender.draw … light`.

## `fsharp-game.fsproj` / `test/fsharp-game.Tests.fsproj` — CHANGED

`test/LightTests.fs` is added to the test project's compile list. No production compile-list change
(no new production module).

## Explicitly unchanged

- **The shaders** (`static.vert/frag`, `mesh.vert`, `cel.frag`, `outline.frag`) and every pipeline
  — the sun arrives through the uniforms the shaders already declare. No `.spv` is recompiled.
- **The idealised face light (spec 040)** — `ActorRender` derives the `Face` mesh's band direction
  from the actor's own orientation, never from the scene light; a new sun changes the face's band
  *colours* only (FR-010).
- **The outline (spec 041)** — unlit, so unaffected (FR-011).
- **`Environment` / `EnvironmentBatch` / the static path and props** — the light is a zone's, not
  an environment's, and nothing about how geometry is batched or drawn changes.
- **The scene pass structure** — one pass, the same draws; only the `Light` value differs.
- **Shadows** — no shadow state, uniform or pass is added (FR-012).
- **`world/ZoneRegistry.fs`** — the registry stores whole `ZoneTemplate`s, so the new field rides
  along; the panel needs no change.
- **Persistence** — nothing is written to disk; phase 15 owns that.
