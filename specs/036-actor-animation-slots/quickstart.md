# Quickstart: Actor Animation Slots with Weights

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- The Akane actor asset (`assets/actors/Akane/Akane.iqm`), which defines two clips, for the interactive
  walkthrough — any actor asset with at least two clips will do.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Automated coverage (headless)

`test/ActorTests.fs` gains tests over the synthetic models it already builds. What each proves:

1. **Blend midpoint (US1, FR-003, SC-001)** — two slots at equal weight, driven to the same or different
   frames, blend to the per-joint midpoint of the two clips' translations and scales, and to the
   `Quaternion.Slerp` midpoint of their rotations. A translation-only fixture (the existing
   `translationModel`) makes the translation half exact.
2. **Single slot is unchanged (FR-011, SC-002)** — `blendPose` with one slot at weight 1 equals
   `samplePose` for the same clip and frame, at every joint. This is the regression guard for the
   `samplePose` refactor.
3. **Relative weights (FR-004)** — weights 0.5/0.5 and 1.0/1.0 produce the same pose.
4. **Bind-pose fallback (FR-008, SC-003)** — an empty slot map, and a map whose every weight is 0, pose the
   bind pose (the `createModel` locals composed), not a zeroed pose.
5. **Own clocks (FR-009)** — `advanceSlotsInPlace` advances each enabled slot by its own rate and leaves a
   disabled one where it is.
6. **Slot algebra (FR-002, FR-006, FR-007)** — `setSlotWeight` adds when absent and adjusts when present
   (no duplicate slot for the same clip), clamps to 0..1; `removeSlot` frees; an add past capacity raises.
7. **No allocation (FR-012, SC-005)** — `blendPose` allocates 0 bytes over a run of frames, measured the
   way the existing `samplePose allocates nothing` test measures it.

## Interactive walkthrough (SC-007, SC-008, SC-009, FR-014–FR-020)

Run the app, press backtick to enter edit mode, and select an actor.

1. **Section shows (FR-014)** — the Inspector shows an **Animations** header under the actor's transform,
   listing the clips the actor holds (an actor spawned from an asset with an Idle clip shows one) with a
   weight for each. Select a prop, a waypoint, a trigger or the environment: no Animations section appears
   (US3 scenario 4).
2. **Add (FR-015, US3 scenario 2)** — click another clip's button under the add row: it joins the list at
   weight 0.0 and the actor's pose does **not** change yet.
3. **Weight (FR-016, US3 scenario 3)** — drag the first clip's weight to 0.5 and the added clip's to 0.5:
   the actor's pose becomes the blend of the two, live, as the drag moves.
4. **Remove, including the last (FR-015, FR-018, US3 scenario 7)** — remove the added clip: it leaves the
   list and its contribution is gone. Remove the remaining clip too: the section reports "(holds no clips)"
   and the actor stands in its rest pose, not a collapsed one. Re-add a clip before the next step.
5. **Degenerate cases report (FR-017, SC-008)** — with an actor whose asset defines no clips, the section
   reads "(asset has no clips)"; with every clip already held, it reads "(all clips added)"; with eight
   held, "(maximum 8 clips)". None offers an add that would fail.
6. **No undo, no persistence (FR-019, SC-009)** — move the actor (one scene edit), then change its slots,
   then press Ctrl+Z **once**: the actor moves back and its slots are as you left them. Press Ctrl+Z again:
   nothing about the slots was ever on the stack. Restart the app: the actor holds its default single slot.
7. **Undo a deletion rebuilds the default slot (FR-019)** — with the actor selected, press Del (the actor is
   removed), then Ctrl+Z. The actor returns holding **one** slot — Idle (or its first clip) at weight 1 —
   because the undo rebuilds its renderer and the rebuild seeds the default, not the slots it held before it
   was deleted. This is the spec's edge case that a restore of runtime state is not a restore of slots.
8. **Existing single-clip actors unchanged (FR-011, SC-006)** — an actor you do not touch still plays its
   Idle clip exactly as before the feature, and the player character's Jog/Idle switch (the hard switch in
   `Systems.animate`) still works.

## Automated coverage boundaries

- The blend and the slot algebra are pure and fully covered headlessly (above).
- The renderer's name-resolving wrappers (`setClipWeight`/`removeClip`) and the Inspector section need a GPU
  device and a running ImGui frame, so they are covered by the walkthrough, as `035` left its panel lines to
  its quickstart.
- FR-019 (no command, no persistence) and SC-009 are structural — nothing is written to the undo stack
  because the section calls no command; the walkthrough step 6 confirms it in the running app.

## Expected outcome

- An actor can hold up to eight clips at once and renders the weighted average of them: translations and
  scales averaged linearly, rotations blended as rotations, and the bind pose when nothing contributes.
- An actor holding one clip at weight 1 poses exactly as it did before the feature, and the animation and
  skinning suites pass unchanged.
- The actor Inspector has an Animations section that adds, removes and re-weights clips live, records no
  undo step, and persists nothing.
- The blend adds no per-frame allocation.
- `dotnet build` is clean; `dotnet test` passes.
