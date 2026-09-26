# Contract: Actor Animation Slots with Weights

Public surface added or changed by this feature. Signatures are illustrative F#.

## `Actor` — `world/Actor.fs` (changed)

```fsharp
[<Literal>]
val MaxAnimationSlots : int = 8

// NEW — the weighted blend of the actor's contributing slots, composed into `pose`. Pure CPU math,
// allocation-free. A single contributing slot at weight 1 produces the same matrices `samplePose` does.
val blendPose :
    m:ActorModel -> slots:Span<SlotMap.Slot<AnimationSlot>> -> pose:PoseFrame -> unit

// NEW — advance every alive, enabled slot on its own clock. The per-frame hot path reaches here through
// `ActorRender.advanceInPlace`.
val advanceSlotsInPlace :
    m:ActorModel -> slots:Span<SlotMap.Slot<AnimationSlot>> -> dt:float32 -> unit

// NEW — find-or-add the slot for a clip and set its weight (clamped 0..1, enabled). Adding beyond
// `MaxAnimationSlots` raises through `SlotMap.Add`; an out-of-range `animIndex` raises.
val setSlotWeight :
    m:ActorModel -> slots:SlotMap<AnimationSlot> -> animIndex:int -> weight:float32 -> unit

// NEW — remove the slot for a clip; a no-op when the actor holds no slot for it.
val removeSlot :
    slots:SlotMap<AnimationSlot> -> animIndex:int -> unit

// UNCHANGED signature and behaviour — still the single-clip sampler, now built on the shared per-joint
// sampler below so the channel math exists once. Kept because it is the tested single-clip path and
// `blendPose`'s reference for FR-011.
val samplePose :
    m:ActorModel -> animIndex:int -> frame:float32 -> loop:bool -> pose:PoseFrame -> unit

// PRIVATE, extracted (the refactor `023`'s golden tests guard):
//   sampleJointTRS : Iqm.Model -> Iqm.Anim -> f0:int -> f1:int -> blend:float32 -> j:int
//                    -> struct (Vector3 * Quaternion * Vector3)
//   composeJoint   : ActorModel -> Iqm.Model -> j:int -> t:Vector3 -> r:Quaternion -> s:Vector3
//                    -> pose:PoseFrame -> unit
```

- `AnimationSlot.Weight` and `AnimationSlot.Enabled` become `mutable`; `Frame`, `AnimIndex`,
  `PlaybackFps` and `Loop` keep their meanings.
- `blendPose` normalises by the sum of contributing weights, so weights are relative (FR-004); its
  rotation is an incremental slerp, so it never linearly averages quaternion components (spec edge case);
  its fallback when nothing contributes is the joint's bind pose (FR-008). It takes the slot map's `Span`
  (what `Slots` yields) and only reads it — a `ReadOnlySpan` parameter would route the same span through
  `Span.op_Implicit`, which F# flags with warning FS3391.
- The module gains a `core/SlotMap.fs` dependency (compiled above it). It still has no GPU and no Zone
  dependency.

## `ActorRender` — `world/ActorRender.fs` (changed)

```fsharp
[<Struct>]
type ActorRender =
    { Asset : ActorAsset.ActorAsset
      AnimationSlots : SlotMap<Actor.AnimationSlot>   // replaces: mutable Animation : Actor.AnimationSlot
      Pose : Actor.PoseFrame
      BoneBuffer : Gpu.Buffer
      BoneStorageBuffers : nativeint array
      BoneTransfer : Gpu.TransferBuffer
      BoneBytes : byte array }

// UNCHANGED signature. Now seeds one slot at weight 1: "Idle" if the asset defines it, else the first
// clip, else no slot (an empty set, which blends to the bind pose).
val create : device:Gpu.Device -> asset:ActorAsset.ActorAsset -> ActorRender

// UNCHANGED signature. Now advances every slot.
val advanceInPlace : r:byref<ActorRender> -> dt:float32 -> unit

// UNCHANGED signature. Now produces the blended pose (`Actor.blendPose`) before the bone-palette upload;
// the byte copy and the copy-pass are untouched.
val upload : r:ActorRender -> cmd:Gpu.CommandBuffer -> unit

// NEW — resolve a clip name (raising when the asset does not define it, FR-010) and set its weight
// through `Actor.setSlotWeight`: the clip joins the set when it is not held (FR-005, FR-007).
val setClipWeight : r:byref<ActorRender> -> name:string -> weight:float32 -> unit

// NEW — resolve a clip name and remove its slot.
val removeClip : r:byref<ActorRender> -> name:string -> unit

// CHANGED behaviour, UNCHANGED signature — the hard switch, expressed over the slots: bring the named
// clip to weight 1 and zero every other slot's weight (leaving them held). This is what `Systems.animate`
// calls today, and FR-013 defers the cross-fade that would replace it.
val playClip : r:byref<ActorRender> -> name:string -> unit
```

- `draw` and `release` are unchanged: `draw` never touched the animation state, and the slots hold no GPU
  resource to release.
- `create` no longer builds a disabled dummy slot for a clip-less asset; an empty map is the same outcome
  (the bind pose) and is simpler.
- `animationSlotCapacity` re-exports `Actor.MaxAnimationSlots` for the Inspector's "at maximum" report
  (FR-017): the Inspector opens `Entity`, which also declares a module `Actor`, so the section reads the
  capacity from here rather than through that shared name.

## The Inspector section — `editor/Inspector.fs` (changed)

Inside the existing `if ref.Kind = EntityKind.Actor` block, after Show Collider / Set as Player:

```fsharp
// The actor's runtime renderer, reached the way `Systems.animate` reaches it.
let actor = &zone.Actors.Get(handleOf ref)
let renderer = &actor.Renderer
let anims = renderer.Asset.Model.Model.Anims

if ImGui.collapsingHeader("Animations", 0) then
    // ── held clips: a row each, with a weight drag and a remove ──
    // for each alive slot in renderer.AnimationSlots.Slots:
    //     ImGui.pushId (clip name)
    //     ImGui.text (clip name); ImGui.sameLine(0, 8)
    //     let mutable w = slot.Value.Weight
    //     if ImGui.dragFloat("Weight", &w, 0.01f, 0.0f, 1.0f, "%.2f", 0) then
    //         ActorRender.setClipWeight renderer (clip name) w        // FR-020: one path
    //     if ImGui.smallButton "Remove" then ActorRender.removeClip renderer (clip name)
    //     ImGui.popId()

    // ── add: a button per clip the asset defines and the actor does not hold ──
    // a clicked button calls: ActorRender.setClipWeight renderer name 0.0f   (research Decision 7)

    // ── the three degenerate cases, as text (FR-017) ──
    // anims.Length = 0            -> "(asset has no clips)"
    // every clip already held     -> "(all clips added)"
    // animationSlots.Count >= Max-> "(maximum 8 clips)"
```

- It uses `Entity.handleOf` and `Actor.Renderer`, both already public, and no new `Zone` accessor (research
  Decision 9).
- It records no command and stores nothing in `EditorState` (research Decision 8, 10); the widgets it needs
  — `collapsingHeader`, `text`, `sameLine`, `dragFloat`, `smallButton`, `pushId`/`popId` — already exist in
  `render/ImGui.fs` (D-017), so nothing native changes.
- It calls the same `setClipWeight` the gameplay caller uses, which is FR-020.

## Not changed

- `world/Systems.fs` — `animate` still calls `ActorRender.playClip`; the idle↔walk cross-fade is the work
  FR-013 defers.
- `state/Command.fs` — no new command. The slots are runtime state, which a payload may not hold (FR-019).
- `state/EditorState.fs` — no new field; the add is a button, not a stored choice (research Decision 8).
- `world/Entity.fs` — no field on `ActorState` or `Actor`; the renderer's type already covers it.
- `render/ImGui.fs` and `native/` — no new widget or binding.
- `core/SlotMap.fs` — used as it is; its `Slots` span and `Add`/`Free`/`Get` are the whole surface needed.
- `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` — no file is added or moved.
