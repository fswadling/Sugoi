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

## 12. The game's 2D layer (spec 038)

`render/Ui.fs` is the game's own screen-space 2D layer — Dear ImGui is the editor's toolkit,
not a game's. It is one blended, depth-less pipeline drawn at the tail of the existing scene
pass, one grow-only indexed quad batch in reused arrays, and text through **SDL_ttf's GPU text
engine**.

- **The blend state is the one new pipeline state.** `SDL_GPUColorTargetBlendState` enables
  blending through a *property* (`EnableBlend`), not a field — its C fields begin at the six
  blend factors/ops. Setting only the factors leaves blending off, and the panel would not
  composite over the scene.
- **Text geometry is a pointer-linked list into SDL_ttf's memory.** `TTF_GetGPUTextDrawData`
  returns a `TTF_GPUAtlasDrawSequence*` whose `xy`/`uv`/`indices` point into the library. It is
  walked **once per line change** and copied into the layer's own arrays; walking it per frame
  would marshal and allocate (SC-007), so a line's geometry is cached until its text or wrap
  width changes.
- **Its coordinates use SDL_gpu's convention: y upwards.** Both the vertex and the texture
  coordinates point up, so the view flips y (`rect.Y - y`) when copying glyphs into a
  top-left-origin batch, passing the uv through unchanged.
- **The ortho matrix maps pixels to clip space.** Same convention as `DebugDraw`: the raw .NET
  row-major `Matrix4x4` floats are already the column-major GLSL layout for
  `u.ortho * vec4(pos, 0, 1)` (see §7). The origin is the top-left corner, y downwards, and a
  resize re-pushes the matrix.
- **A small pool of text slots.** One slot per kind of string the layer draws: a dialogue's name
  plate, its line, and the interact prompt (spec 039) — three. A frame re-measures none of them, so a
  stable string is laid out once and never rebuilt per frame.
- **The font and its atlas are loaded once.** The atlas is sampled through the layer's own
  clamp-to-edge, linear sampler (the private `Ui.createSampler` — a different sampler from
  `Texture`'s, which addresses by repeat); the portraits are ordinary `Texture`s from the asset
  loader.
- **The font's size follows the viewport.** The font is opened once, but `beginBatch` re-derives its
  pixel size from the viewport height each frame (`TTF_SetFontSize`); a change re-rasterises the
  glyphs and drops each text slot's cached geometry — including the stale atlas handles in its runs —
  so the text scales with the panel and survives a resize.
- **A slot's geometry is read back directly.** `setLine` caches a slot's glyph geometry; the caller
  reads it as a field (`ui.TextGeoms.[slot]`). A one-line accessor for that added no clarity and was
  removed.
- **The layer owns its GPU resources and frees them.** The pipeline, both shaders, the vertex/index
  buffers and their transfer buffers, the glyph sampler and the 1×1 white placeholder are created in
  `Ui.create` and released in `Ui.release`. Textures it did not create (the portraits) are freed by
  their owner through `Texture.release`.
- **The layer is silent and stateless.** A line it cannot draw — its portrait does not resolve, or it
  is taller than the viewport — is simply left undrawn: `draw` returns without emitting anything and
  keeps no mutable state of its own. Explaining *why* is the editor's job, not the render layer's (the
  Dialogue panel asks `DialogueRender.missReason`), so nothing is logged per frame from the draw path.
- **Its second consumer is the interact prompt (spec 039).** `world/PromptRender.fs` draws a trigger's
  prompt as one centred line near the bottom of the screen while the player is inside it, with a
  shadow under the text for contrast. It measures through the third text slot, and the shadow and the
  text share the atlas texture and sampler, so the pair is a single draw run. A dialogue line takes the
  screen over the prompt, so the two never compete for the bottom strip.

## 13. Cel-shaded actors (spec 040)

Actors are cel-shaded by `shaders/cel.frag`, which **replaced** the PBR `shaders/mesh.frag`. The
skinned mesh pipeline is actor-only (environments and props draw through the separate instanced
`static.*` path), so retargeting its fragment stage is what gives actors the two-tone look while the
environment stays PBR. The pipeline is `Gpu.CelPipeline`; `shaders/mesh.vert` (skinning + morphs) is
unchanged, so the vertex path and the pose are untouched.

- **Two flat bands, additive colour.** The wrapped half-Lambert `dot(N,L)*0.5+0.5` is stepped by
  `smoothstep(0.45, 0.55, …)`, and the bands are composed `albedo * (ambient + sunColour * band)` —
  the same `direct + ambient` shape the environment uses, so a lit actor matches the lit environment
  beside it. The ramp threshold/softness and the alpha cutoff are shader constants (FR-012).
- **The `Face` mesh is lit by an idealized front light.** The actor's IQM meshes named `Face`
  (`Mesh.Submesh.IsFace`, set once at load from the mesh name the parser already reads) take their
  band direction from a fixed light in front of the actor — `ActorRender.faceLightDir`, the actor's
  horizontal facing (local `+Z` under the model matrix, flattened and normalized) plus the reference's
  `0.25` elevation — instead of the scene sun, so the face keeps constant banding as the actor turns.
  Only the band *direction* is idealized; the colours still come from the scene.
- **One sampler, a lean 80-byte uniform.** Cel reads only the albedo map (`set = 2, binding = 0`);
  its fragment uniform (`set = 3, binding = 0`) is base colour, light dir/colour/ambient and the face
  vector — no normal/metallic-roughness/emissive factors and no camera position. Alpha is a test
  (discard), not a blend: the shared opaque-white placeholder means a factor-only material never
  discards, while hair/eye cutout texels do.
- **No per-frame allocation.** `IsFace` is a struct field; the uniform byte array is reused.
- **Deferred (not this spec):** shadow receiving (needs `render/Shadow.fs`), outlines, rim light,
  specular/matcap, ramp textures and per-actor parameters — the reference's later phases.

## 14. The actor outline (spec 041)

Actors are outlined by a second pass over the same posed mesh — the **inverted hull**. `shaders/mesh.vert`
is shared by both pipelines and extrudes the hull in world space; `shaders/outline.frag` draws it flat in
a dark colour through a **front-cull** pipeline (`Gpu.OutlinePipeline`), and the depth buffer hides
everything except the ring past the silhouette. The cel surface (spec 040) is unchanged and drawn over
it.

- **The extrusion direction is a position-welded average.** The hull is pushed along `Iqm.smoothNormals`
  (each vertex's normal averaged across coincident vertices), not the authored normal — the model is
  ~79% hard-edged (the hair ~100%), so the authored per-face normal would tear the outline open at every
  crease. This is the feature's whole point.
- **Fixed world-space width.** The hull is offset in world space along its smoothed normal by one
  constant world distance (`uOutline.x`), so the line shrinks with distance exactly as the actor does — a
  physical ink ribbon, thickest when the actor is closest, with no on-screen cap and no distance fade
  (FR-008). The cel surface pushes width 0 and is inert to it.
- **One vertex shader.** Both passes share `mesh.vert` (extended with `location = 5 aSmoothNormal` and
  the vertex uniform slot 2 `OutlineParams`), so the skinned/morphed pose is computed once.
- **Masked by the cutout.** `outline.frag` samples the material's albedo and discards below the cutoff,
  so the alpha-cutout hair/eye cards are not boxed.
- **The mesh is clockwise-wound.** Akane's IQM triangles are wound clockwise about their outward normals (verified from the asset), so the actor pipelines declare `FrontFace = Clockwise`; the outline's front-face cull then removes the camera-facing hull and keeps the far side. (This is why the cel surface never noticed: it does not cull.)
- **Depth-biased.** The outline pipeline enables depth bias so the hull cannot bleed through thin or
  concave geometry.
- **Deferred (not this spec):** interior crease lines (would need a normal G-buffer), shadows, rim,
  specular/matcap, per-actor parameters.

## 15. The multisampled scene pass (spec 042)

The 3D scene is drawn with **4× MSAA** and resolved into the swapchain. Before this, every pipeline and
render target was `SampleCount1` and the scene rendered straight into the acquired swapchain texture, so
every edge — the actor outline (spec 041) above all — was stair-stepped.

- **Two multisampled targets, one resolve.** `Gpu.MsaaTexture` (colour, in the swapchain format) and the
depth texture (`D32Float`) are both 4× (`Gpu.SampleCount`); the scene renders into them and the acquired
swapchain is the colour target's **resolve destination** (`GPUColorTargetInfo.ResolveTexture`). SDL_gpu
has no multisampled swapchain, so the offscreen target + resolve is the only way. Depth is never
resolved — SDL_gpu's depth-stencil target carries no resolve, and nothing samples depth after the pass.
- **The store op performs the resolve.** Setting `ResolveTexture` alone does nothing: the multisampled
colour target's `StoreOp` must be `SDL.GPUStoreOp.Resolve`, or the swapchain is never written and the
frame is black. `Resolve` writes the resolved colour and discards the multisample contents.
- **Every scene-pass pipeline shares the count.** A pass is only valid when its targets and every
  pipeline drawing into it agree, so the cel, outline and static pipelines (`render/Gpu.fs`), the
debug-line pipeline (`render/DebugDraw.fs`) and the 2D-layer pipeline (`render/Ui.fs`) all set
`MultisampleState.SampleCount`. They all draw in the one pass (`screen/GameplayScreen.fs`); the overlay
does not.
- **The overlay is composited after the resolve.** ImGui draws in its own pass that loads the resolved
swapchain, so its text stays crisp and is not re-sampled.
- **Fail loudly.** `SDL.GPUTextureSupportsSampleCount` is checked for both formats at init, and a null
target handle is a failure; there is no silent single-sample fallback (FR-009).
- **No per-frame allocation.** Both targets are created once and recreated only when the render extent
changes (`Gpu.ensureSceneTargets`, the depth texture's old ensure path); the frame's reused arrays are
unchanged.
- **Fixed 4×.** The sample count is a constant (`Gpu.SampleCount`); there is no authoring surface
(FR-010).
- **Deferred (not this spec):** FXAA/TAA and variable sample counts.

## Known follow-ups

- Two benign build warnings in `Engine.fs` (`FS0009`/`FS0051`) from the
  `NativePtr.toNativeInt (&&ev)` event dispatch; they can be silenced with
  `#nowarn` if desired.
- The frame loop still uses both `mailbox` present mode and a software 60 fps
  cap. `ocaml-game` (D-018) concluded the two clocks drift and removed the
  software cap in favour of the vblank wait alone; worth re-checking here.
- **Scene draw cost scales with glTF node count.** The ArtRoom loads as 463
  environment parts (521 submesh draws), each drawn with its own full state
  block. Measured via the per-phase log: `record` ~20 ms/frame with the parts
  loaded versus ~0.5 ms with none, while `acquire` stays ~0.1 ms — so the frame
  is **CPU-bound on draw submission**, not GPU-bound. Frustum culling and bind
  de-duplication were tried and measured to have **no effect**, and have been
  reverted; the fix is instancing (521 → 191 draws). Full investigation,
  measurements, external evidence and the implementation plan live in
  `docs/RENDER_PERFORMANCE.md`.
