# Rendering Notes (SDL3 GPU + ImGui viewport)

**Status**: Reference. Reusable findings from getting the debug-draw grid and
the editor's 3D viewport working (Phase 2b, `specs/003-editor-workspace`).
These are the lessons worth remembering before building more of the render
layer; several of them are one-character mistakes that cost a long debug.

## Scope

- SDL3 GPU debug-draw line pipeline (`render/DebugDraw.fs` + `shaders/line.*`).
- The editor viewport as ImGui's passthru central dock node (grid drawn to the
  swapchain behind ImGui, showing through the central node).
- Render/copy pass ordering and the matrix/uniform conventions.

The authoritative "known-working" reference is `ocaml-game/lib/render.ml` and
`ocaml-game/lib/editor/editor.ml`; its `docs/SDL3_GPU_NOTES.md` and
`docs/IMGUI_NOTES.md` are also worth reading.

---

## 1. The passthru central node flag value (the big one)

`render/ImGui.fs` hardcodes the docking flag we pass to
`igDockSpaceOverViewport`. The value matters, and it is easy to get wrong:

| Flag | Value |
|---|---|
| `ImGuiDockNodeFlags_KeepAliveOnly` | `1 << 0` = 1 |
| `ImGuiDockNodeFlags_NoDockingOverCentralNode` | `1 << 2` = 4 |
| `ImGuiDockNodeFlags_PassthruCentralNode` | `1 << 3` = 8 |

The original code used `passthruCentralNode = 4`, which is actually
`NoDockingOverCentralNode`, **not** `PassthruCentralNode`. Symptom: the grid
rendered but was invisible — the empty central node painted an opaque
`ImGuiCol_DockingEmptyBg` over the swapchain — and the viewport area changed
colour when toggling the editor with `` ` ``.

The correct constant is `8`. This is the one value to double-check whenever the
viewport "looks like it's rendering but nothing shows through".

---

## 2. How the passthru flag actually propagates

Understanding this saves guessing next time the viewport goes opaque:

- `DockSpace(id, size, flags, …)` sets `node->SharedFlags = flags`. It asserts
  that `flags` does **not** contain `DockSpace` or `CentralNode` (those are
  managed internally as `LocalFlags`).
- `DockSpaceOverViewport(0, …, flags, …)` calls `DockSpace` internally and is
  our steady-state source of the flag — we call it **every frame**, so the F#
  constant is what wins, and it overwrites whatever the one-time shim set.
- Effective flags are `MergedFlags = SharedFlags | LocalFlags | LocalFlagsInWindows`.
  `SharedFlagsInheritMask_` is `~0`, so `SharedFlags` (including
  `PassthruCentralNode`) propagate from the root node to all children, which is
  why the empty central leaf node sees the passthru flag.
- `DockBuilderAddNode(id, DockSpace | PassthruCentralNode)` routes through
  `DockSpace`, so it too lands in `SharedFlags` (not `LocalFlags`). Passing it
  there is what makes the grid show through on frame 1, before the every-frame
  `DockSpaceOverViewport` re-applies it.

The paint decision is in `DockNodeUpdate`:

```cpp
node->LastBgColor = (node_flags & PassthruCentralNode) ? 0 : GetColorU32(DockingEmptyBg);
```

With passthru set, the empty central node paints nothing; the root node paints
`WindowBg` with a "hole" over the central node's rect, leaving the scene visible.

---

## 3. `DockBuilderAddNode` re-creates the node

`DockBuilderRemoveNode` + `DockBuilderAddNode` (the `setup_dock_layout` shim)
deletes and re-creates the dockspace node. The flags passed to
`DockBuilderAddNode` must therefore be self-sufficient: use
`ImGuiDockNodeFlags_DockSpace | ImGuiDockNodeFlags_PassthruCentralNode`.
Passing only `DockSpace` re-creates the node with `KeepAliveOnly` and loses the
passthru flag that `DockSpaceOverViewport` had set (until the next frame
re-applies it).

---

## 4. SDL3 GPU shader resource sets (SPIR-V)

SDL_gpu maps shader resources by convention, not by free choice. For SPIR-V
(verified against `SDL_gpu_vulkan.c` in SDL 3.4.12):

- Vertex shader: `set = 0` = samplers / storage textures / storage buffers
  (storage buffer *n* binds at `binding = numSamplers + numStorageTextures + n`);
  **`set = 1`** = uniform buffers.
- Fragment shader: `set = 2` = samplers / storage textures / storage buffers;
  **`set = 3`** = uniform buffers.

`SDL_PushGPUVertexUniformData(cmd, slot_index=0, …)` maps to
`set = 1, binding = 0`. The `line.vert` shader uses exactly that:

```glsl
layout(set = 1, binding = 0) uniform Uniforms { mat4 viewProj; } u;
```

A vertex-stage read-only storage buffer bound via
`SDL_BindGPUVertexStorageBuffers(pass, slot=0, …)` maps to `set = 0, binding = 0`
(assuming no samplers/storage textures), and the shader must be created with
`NumStorageBuffers = 1` or the pipeline layout won't include it.

---

## 5. Pushing uniforms (the 4 KiB cap)

- `SDL_PushGPUVertexUniformData` is **command-buffer-scoped** ("subsequent draw
  calls in this command buffer use this uniform data"). It does **not** need to
  be inside the render pass; pushing it before `BeginGPURenderPass` is fine.
- Uniform data must respect **std140** layout. A single `mat4` is just 16
  floats (64 bytes) and needs no extra padding.
- **Each pushed uniform slot is exposed to the shader with a hard 4 KiB range.**
  SDL's Vulkan backend memcpy's the full push into its ring buffer but writes
  the descriptor with `range = MAX_UBO_SECTION_SIZE` (4096). Any read past 4096
  bytes is out of range — on NVIDIA it returns zeros. Symptom we hit: a
  205×64-byte bone palette pushed as one uniform rendered bones 0–63 fine,
  while bones 64+ read as zero — vertices weighted to them collapsed to a
  point, producing degenerate spikes converging at the screen centre and
  missing geometry (hair, fingers). There is no error or warning.
- The fix for anything larger than 4 KiB is a **storage buffer**
  (`SDL_GPU_BUFFERUSAGE_GRAPHICS_STORAGE_READ` + per-frame transfer-buffer
  upload + `SDL_BindGPUVertexStorageBuffers`), which is what
  `world/ActorRender.fs` does for the bone palette.
- Uniform slots are also scarce: `MAX_UNIFORM_BUFFERS_PER_STAGE = 4` per stage,
  so splitting a big array across slots caps out quickly.

---

## 6. `SampleMask` must be 0

`GPUMultisampleState.SampleMask` is documented as "reserved for future use;
must be set to 0" for non-multisampled pipelines. Some older references (and
the ocaml spike) use `0xFF`; SDL 3.4.x asserts against that. Set it to `0u`.

---

## 7. Matrix convention (no transpose)

`.NET System.Numerics.Matrix4x4` is **row-major storage with row-vector math**
(`v * M`). Uploading the raw `M11..M44` floats to a GLSL column-major `mat4`
that does `u.viewProj * vec4(aPosition, 1.0)` is correct **without a
transpose**, because row-vector `v * M` is the transpose of column-vector
`M^T * v` — the components are identical.

Combined transform order is `view * proj` (row-vector convention: apply view,
then projection).

---

## 8. Render + copy pass sequencing

The frame records, in order on one command buffer:

1. `ImGui_ImplSDLGPU3_PrepareDrawData(draw_data, cmd)` — uploads ImGui's
   vertex/index buffers in a copy pass. Must run **before** the render pass
   that consumes it.
2. `DebugDraw.upload` — a second copy pass for the line vertices.
3. Scene render pass — `LoadOp = Clear`, draw the grid.
4. ImGui render pass — `LoadOp = Load`, `ImGui_ImplSDLGPU3_RenderDrawData`.

Notes:

- Multiple copy passes before the render passes are fine; only one pass is
  "open" at a time.
- `PrepareDrawData` → render, and `RenderDrawData` → render, are two different
  lifecycle points: prepare uploads, render emits commands *inside* the pass.
- Splitting scene (clear) from ImGui (load) is what preserves the scene under
  the ImGui overlay. A single clear+ImGui pass would wipe the grid.

---

## 9. Grid visibility must not be gated on editor state

The grid was hidden in play mode because two places tied it to `Editing`:

- `screen/GameplayScreen.fs` cleared the `DebugDraw` accumulator when
  `Editing = false`.
- `Engine.fs` gated `drawGrid` on `manager.Gameplay.Editing`.

The scene (grid) belongs to the Gameplay screen, not to the editor, so it
should draw whenever the active screen is `Gameplay`. The editor only controls
whether the docking workspace/panels are drawn on top.

---

## 10. Debug-draw vertex layout

Line vertices are 7 floats each — `position.xyz` (offset 0, `Float3`) +
`color.rgba` (offset 12, `Float4`) — 28-byte stride. The pipeline is a
`LineList` with `CullMode = None`, depth test off, and blending off.

---

## 11. Per-phase frame timing

The frame loop (`Engine.fs`) times each phase every frame and prints the averages
once a second alongside the frame time:

```
avg 22.4ms (last 16.8) | input 0.15  update 0.03  ui 7.40  imgui 0.02  acquire 0.47  record 17.15  submit 0.00 | phases 25.23 | actors 1 props 0 parts 463
```

The phases are `input` (poll + gather + ImGui NewFrame), `update`
(`ScreenManager.update`: systems, `Physics.step`, editor), `ui`
(`ScreenManager.draw` + overlay), `imgui` (`ImGui.render` + `getDrawData`),
`acquire` (command buffer + swapchain), `record` (draw-data prep, uploads, scene
pass, ImGui pass) and `submit`. `phases` is their sum, so the gap to `avg` is idle
sleep (headroom); the entity counts expose scene growth. The cost is one clock read
per phase, so it can be left on. Note that `avg` is averaged over the window while
`last` is the final frame — compare `avg` to `phases`, not `last`.

---

## Known follow-ups

- Two benign build warnings in `Engine.fs` (`FS0009`/`FS0051`) from the
  `NativePtr.toNativeInt (&&ev)` event dispatch; they can be silenced with
  `#nowarn` if desired.
- The frame loop still uses both `mailbox` present mode and a software 60 fps
  cap. `ocaml-game` (D-018) concluded the two clocks drift and removed the
  software cap in favour of the vblank wait alone; worth re-checking here.
- **Scene draw cost scales with glTF node count.** The ArtRoom loads as 463
  environment parts, each drawn with its own full state block (vertex + fragment
  uniform pushes, pipeline bind, sampler bind, index bind). Measured via the
  per-phase log: `record` ~20 ms/frame with the parts loaded versus ~0.5 ms with
  none, while `acquire` stays ~0.1 ms — so the frame is **CPU-bound on draw
  submission**, not GPU-bound. Frustum-culling parts and skipping unchanged binds
  are the next steps (`specs/016-walk-around/tasks.md` T023–T025).
