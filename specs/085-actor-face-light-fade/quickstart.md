# Quickstart: Validate the Face Light Fade

The fade lives in the shader body, so it needs a GPU and cannot run headless. Validation is therefore
two parts: the headless suite (proves nothing else regressed) and an interactive walkthrough (proves
the look).

## Prerequisites

- A build of the game (`dotnet build`) and the SDL3 native libraries it already needs.
- An actor whose asset has a mesh named `Face` (the `Akane` asset does — `specs/040`).
- A zone with an environment so the sun can be aimed.

## 1. Headless suite (SC-004)

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

Expected: build clean; **578 tests pass, 0 failed** (nothing in the suite is introduced or changed —
this feature adds no pure logic). This guards FR-005/FR-008: the environment, prop, UI and editor
paths and the `specs/040` face rules are untouched.

## 2. Interactive walkthrough (SC-001, SC-002, SC-003)

Run the game (`dotnet run`) and, in the editor:

1. **Aim the sun off-vertical.** With no entity selected, use the Inspector's **Lighting** section
   (`specs/043`): set **Sun Pitch** to something like `30°` and note the **Sun Yaw**.
2. **Place an actor and turn it.** Place the character, then rotate it through a full circle (the
   transform gizmo). Watch its **face** (not its body):
   - **Face pointing at the sun** (the actor's forward, `+Z`, aimed along the sun's yaw): the face
     shows the full `specs/040` two-tone split — the lit band at full brightness.
   - **Facing a quarter turn off** (forward at 90° to the sun's azimuth): the face is at the switch —
     an intermediate tone, dimmer than full, brighter than the shadow band.
   - **Facing directly away from the sun** (forward opposite the sun's yaw): the face's lit band has
     switched to the shadow band, so the whole face sits in the ambient-only shade that the
     body already shows. It must **not** be pure black (the ambient floor — SC-002).
   - The face is **two-tone throughout** — full lit split on the toward side, flat shade on the away
     side — switching at the crossing with only a small eased band, not a slow gradient. Confirm it
     does not strobe as the actor turns slowly through the 90° point.
3. **Confirm the boundary does not move.** Whatever the heading and sun, the face's band boundary
   stays in the same place on the face (`specs/040` FR-007); only the brightness changes.
4. **Confirm the axis is azimuthal.** Raise **Sun Pitch** toward `90°` (sun overhead). The face's
   swing should **not** weaken — it still switches fully when the actor turns away (this is the
   azimuth-only decision). At exactly `90°` there is no azimuth to compare; the face keeps its full
   front light.
5. **Confirm nothing else changed** (SC-003). The actor's **body** bands still follow the scene sun;
   a face-less actor is unaffected; a co-located environment/prop, a portrait, the UI and the editor
   panels render exactly as before. Change the sun's **colour**/**Ambient** and confirm the face's
   tones still take the scene colour/ambient.

## 3. Notes

- If the fade looks too soft or too crisp, the only knob is the switch in `shaders/cel.frag`
  (`smoothstep(-FACE_FADE_SOFTNESS, FACE_FADE_SOFTNESS, toward)`, the `FACE_FADE_SOFTNESS` half-width);
  it is deliberately a fixed expression (FR-006), so retuning it is a shader
  edit and a `glslc` recompile, not a UI change.
- The `.spv` is committed; after editing `cel.frag`, recompile with
  `glslc shaders/cel.frag -o shaders/cel.frag.spv` (the same invocation every other `.spv` uses).
