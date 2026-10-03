# Contract: Batched asset uploads

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data model**: [data-model.md](./data-model.md)

The surfaces that change, what they guarantee, and what explicitly does not.

## `render/Gpu.fs` — CHANGED

```fsharp
/// The per-staging-buffer bound. Copy offsets are 4-byte aligned; peak staging is one buffer.
[<Literal>]
val StagingCapacity : int

/// A staging transfer buffer (mapped while recording) and one recorded copy.
type Staging = { Transfer : TransferBuffer; Capacity : int; Filled : int; Mapped : nativeint }
type Copy =
    | BufferCopy  of SrcOffset : int * Dest : Buffer  * DestOffset : uint32 * Size : uint32
    | TextureCopy of SrcOffset : int * Tex  : Texture * W : uint32 * Y : uint32 * H : uint32

/// One asset's uploads. Host-side; the command buffer is acquired only when a flush submits.
type UploadBatch = { Device : Device; Staging : Staging option; Copies : ResizeArray<Copy>; MipTextures : ResizeArray<Texture> }
val beginUpload  : device:Device -> UploadBatch

/// Record a buffer upload. Packs into the current staging, flushing it (one submit) when full;
/// a resource larger than `StagingCapacity` is split into 4-byte-aligned copies.
val recordBuffer : batch:UploadBatch -> dst:Buffer -> data:byte array -> unit

/// Record a texture level-0 upload (split into whole-row bands if larger than the capacity).
/// `mipLevels > 1` schedules mip generation at the final flush.
val recordTexture : batch:UploadBatch -> tex:Texture -> data:byte array -> width:uint32 -> height:uint32 -> mipLevels:uint32 -> unit

/// Final flush: submit the remaining copies and generate mips, in one command buffer. A batch
/// with nothing recorded acquires nothing.
val finishUpload : batch:UploadBatch -> unit

/// Abandon: release the current staging and drop the records. No further submit is made, but if
/// the batch already flushed, earlier copies were submitted and the caller's rollback releases
/// their target resources.
val abortUpload : batch:UploadBatch -> unit

/// Unchanged surface, now a direct data-sized one-shot upload (a one-off 4 MiB buffer does not
/// pay for a 256 MiB staging buffer). A failure cancels the command buffer.
val uploadBufferOnce : device:Device -> dst:Buffer -> data:byte array -> unit
```

- **Guarantee (FR-001)**: submissions scale with staging flushes, not with the resource count.
- **Guarantee (FR-003)**: every copy is inside a copy pass; every mip generation is outside any
  pass and after the copies it depends on.
- **Guarantee (FR-004)**: peak staging is one buffer; a failed submit cancels its command buffer.

## `render/Texture.fs` — CHANGED

```fsharp
// load gains the batch and records its level-0 upload into it:
val load : batch:Gpu.UploadBatch -> device:Gpu.Device -> path:string -> Texture
```

- Decode, GPU texture creation, sampler creation and the `Bytes` accounting are unchanged.
- The level-0 copy (and, when levels > 1, a mip registration) goes into `batch`.
- `solid` (the shared white placeholder) keeps a direct one-shot upload.

## `render/StaticMesh.fs` — CHANGED

```fsharp
// create gains the batch and records its two buffer uploads:
val create : batch:Gpu.UploadBatch -> device:Gpu.Device -> positions:float32 array -> normals:float32 array -> uvs:float32 array -> indices:int array -> StaticMesh
```

- The two GPU buffers are still created here; only their uploads are recorded.

## `world/Assets.fs` — CHANGED (internal)

- `buildLoaded` begins one batch, threads it through `StaticMesh.create`, `resolveMaterial`
  and `acquireTexture`, and:
  - on success, calls `Gpu.finishUpload` **before** returning the `EnvironmentLoaded`;
  - on any failure, calls `Gpu.abortUpload` **before** the existing mesh/texture rollback and
    `reraise`.
- `acquireTexture` decodes + creates on a 0→1 acquire and records the upload into the batch;
  a cache hit records nothing (FR-005).
- The `loadTexture` actor path wraps its one-shot batch in `try/with abortUpload`.
- The per-asset instrumentation line is opt-in (`FFS_LOAD_TIMING=1`).

## Explicitly NOT changed

- **Residency** (spec 050): `Loaded`, `ResidentBytes`, the LRU (`Cache`),
  `ENVIRONMENT_BUDGET_BYTES`, the ensure/release/isResident functions, load-on-demand, the
  loud failure on non-resident use, and shutdown release. `ResidentBytes` is identical (FR-007).
- **Texture refcount cache** (`Loader.Textures`): acquire/release semantics and the shared
  white placeholder are unchanged (FR-005).
- **The draw path**: `EnvironmentBatch`, `EnvironmentRender`, `StaticMesh.drawInstanced`, the
  instanced layout, and buffer identity as the batch key. Geometry is still one GPU buffer per
  submesh (spec Q2: combining buffers is deferred, FR-010).
- **Per-frame dynamic uploads**: `EnvironmentBatch.upload`, `DebugDraw.upload`, `Ui.upload`.
- **Shaders, pipelines, passes, formats, depth/MSAA targets**: untouched.
- **Actors and props' eager loading**: timing unchanged; props use the batched path (FR-009).

## Test surface

- Device-free tests cannot exercise the upload path. The **412-test suite is the regression
  gate**. The batched behaviour is verified by the GPU walkthrough in
  [quickstart.md](./quickstart.md) with `FFS_LOAD_TIMING=1`.
