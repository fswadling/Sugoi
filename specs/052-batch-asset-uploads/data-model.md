# Data Model: Batched asset uploads

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

This feature adds one small runtime value — the **upload batch** — and changes who calls
the submit. It adds nothing persisted and changes no existing entity's shape.

## `UploadBatch` (`render/Gpu.fs`)

One asset's uploads. Recording packs copies into the **current** staging buffer and flushes
it (one submission) when it fills; the final flush also generates mips. Peak staging memory
is one buffer, not the asset's size.

```
UploadBatch
  Device          : Gpu.Device
  Staging         : Staging option          // the current staging buffer, while recording
  Copies          : ResizeArray<Copy>       // copies packed into the current staging
  MipTextures     : ResizeArray<Gpu.Texture>// textures needing mip generation (levels > 1)
```

- **Identity**: one batch per asset build; never shared between assets.
- **Lifetime**: `beginUpload` → (`recordBuffer` | `recordTexture`)* → `finishUpload` |
  `abortUpload`. Not reused.
- **Invariant**: after `finishUpload`/`abortUpload` the value is dead; no field is read.

## `Staging` (`render/Gpu.fs`)

The one transfer buffer currently being filled. Mapped once; released when the batch
flushes or is aborted.

```
Staging
  Transfer : Gpu.TransferBuffer
  Capacity : int          // first buffer: max(need, InitialStaging); later: up to StagingCapacity
  Filled   : int mutable  // bytes used so far
  Mapped   : nativeint mutable  // host pointer, or 0 once unmapped
```

## `Copy` (`render/Gpu.fs`)

One recorded copy, named by its byte offset **in the current staging buffer** (there is only
one at a time):

```
Copy =
  | BufferCopy  of SrcOffset : int * Dest : Gpu.Buffer  * DestOffset : uint32 * Size : uint32
  | TextureCopy of SrcOffset : int * Tex  : Gpu.Texture * W : uint32 * Y : uint32 * H : uint32
```

- The staging bytes are already written (mapped, filled, still mapped) when the record exists.
- Copies are 4-byte aligned in staging (`alignUp`), so §"Validation rules" VR-007 holds.

## `StagingCapacity` / `InitialStaging` (`render/Gpu.fs`)

`StagingCapacity` (256 MiB) bounds **peak staging memory**. Copies are packed into a staging
buffer and flushed when it fills. The **first** buffer is sized to the workload (up to
`InitialStaging`, 4 MiB) so a small asset stays small (SC-005); once an asset has flushed it is
known to be large, so later buffers use `StagingCapacity` (which keeps the flush count single
digits, FR-001). A single copy larger than `StagingCapacity` — a pathological texture row, not
reachable with real assets — gets a buffer of its own.

## Existing entities — unchanged

- **`Texture`** (`render/Texture.fs`): `{ Device; Handle; Sampler; Bytes }` unchanged. Only
  *when* its level-0 copy is submitted changes.
- **`StaticMesh`** (`render/StaticMesh.fs`): unchanged shape; only its two buffer uploads are
  recorded into a batch.
- **`EnvironmentLoaded`** / **`EnvironmentAsset`** (`world/EnvironmentAsset.fs`): unchanged.
  The batch is a local of `buildLoaded`.
- **`AssetManager`** (`world/Assets.fs`): unchanged. `ResidentBytes`, `Cache` (the LRU) and
  the texture refcount cache are untouched (FR-007).

## Lifecycle / state transitions

```
        beginUpload
             │
             ▼
        Recording ──recordBuffer/recordTexture──► Recording
             │                        │
             │             staging full? ──yes──► flush (submit, release staging)
             │
      ┌──────┴───────┐
 finishUpload    abortUpload
      │              │
      ▼              ▼
  Submitted       Aborted
 (final flush)  (nothing submitted)
```

- **flush** (mid-record): unmap the staging; acquire one command buffer; one copy pass for
  the recorded copies; submit; release the staging. Then a fresh staging is created on the
  next reservation. Mips are **not** generated here — their level-0 may not be complete.
- **finishUpload**: the final flush, in one submit — the remaining copies **plus**
  `GenerateMipmapsForGPUTexture` for every `MipTextures` entry (outside the pass, per the
  API), then release the staging.
- **abortUpload**: release the current staging and drop the records, with no further submit. An
  asset larger than one staging buffer may already have **flushed** (submitted) earlier copies;
  the caller's rollback releases the GPU resources those copies targeted, so nothing leaks.
- A failed submit **cancels** the command buffer (`SDL_CancelGPUCommandBuffer`) before
  reraising.

## Validation rules

- **VR-001 (FR-001)**: submissions scale with staging flushes, not with the resource count —
  single digits for the reference assets.
- **VR-002 (FR-004)**: peak staging memory is one buffer (`≤ StagingCapacity`, or a single
  larger resource's own buffer), and does **not** grow with the asset's total size.
- **VR-003 (FR-003)**: every `UploadTo*` is inside a copy pass; every
  `GenerateMipmapsForGPUTexture` is outside any pass and after the copies it depends on.
- **VR-004 (FR-006)**: `abortUpload` releases the staging exactly once (`Staging` is cleared
  before releasing) and makes no further submission. An asset that already flushed submitted
  earlier copies; the caller's rollback releases those copies' target resources, so nothing leaks.
- **VR-005 (FR-002)**: the bytes copied to each destination and the mip levels generated are
  identical to the per-resource path.
- **VR-006 (FR-007)**: `ResidentBytes` for a loaded asset is unchanged; the batch does not
  change byte accounting.
- **VR-007**: staging copy offsets are 4-byte aligned (RGBA8 texel size), so a texture upload
  is legal.
