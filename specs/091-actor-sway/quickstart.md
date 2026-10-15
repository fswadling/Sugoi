# Quickstart: Actor sway bones

Validation for `specs/091-actor-sway`. Two halves: a headless suite that proves the maths and the
manifest rules, and a walkthrough that proves the thing you can actually see — hair trailing a
walking character and settling when it stops.

## Prerequisites

- The repo builds: `dotnet build`.
- Run from the repository root.
- For the walkthrough only: a GPU (SDL3 + SDL_gpu), and `assets/actors/Akane/akane.manifest` with
  its `Sway` chains declared (part of this feature — see [contracts/sway.md](./contracts/sway.md)).

## 1. Headless validation (no device)

```sh
dotnet test test/fsharp-game.Tests.fsproj --filter FullyQualifiedName~SwayTests
```

Expected: all green. What each assertion buys, mapped to the spec:

| Check | Spec |
|---|---|
| A good chain resolves to joint indices in order | FR-001, FR-009 |
| A joint name the model lacks is refused, with a message naming it | FR-009 |
| A chain that is not a parent→child path is refused | FR-006 (D3) |
| A stiffness outside `(0, 1]` — `0`, or above `1` — or fewer than two joints, is refused | FR-010 |
| A chain over the point cap, or an actor over the chain cap, is refused | FR-012 |
| `quat_from_two_vectors` lands `src` on `tgt` (`Vector3.Transform`), for axis-aligned, oblique and near-opposite pairs | D3 |
| A segment's applied rotation puts that segment **exactly** on its simulated direction | FR-006 |
| The applied local rotation is **unchanged by yawing the whole actor** | FR-005, SC-003 |
| With no motion, a displaced chain converges back to the animated pose within the tolerance | FR-004, SC-002 |
| A displacement large enough to exceed the bound is clamped to `SwayMaxAngle` | FR-007 |
| An anchor jump of more than `SwayTeleportDistance` re-seeds instead of stepping | FR-013 |
| Stepping allocates 0 bytes (warm up, then measure) | FR-012, SC-006 |

Also confirm the feature is inert for an actor that declares nothing:

```sh
dotnet test test/fsharp-game.Tests.fsproj --filter FullyQualifiedName~SwayTests
```

includes a case where `resolve` is never called and `apply` on an empty `SwayState` writes
nothing.

The existing suites must be green **with their assertions unmodified**, which is FR-015 and — for the pose change —
the sharpest check, because `ActorTests.fs` asserts the composed matrices that `composeJoint`
still produces:

```sh
dotnet test
```

## 2. Walkthrough A — hair trails and settles (US1, US3)

1. `dotnet run`.
2. Walk to the gameplay screen, press the key that enters the editor (`` ` ``), select an actor
   carrying a sway chain (Akane), and note the hair: while **editing**, it holds the animated
   pose exactly — no drift, no swing (FR-008).
3. Press Play.
4. Jog the player and watch the hair: it should lag *behind* the direction of travel — lean back
   as the character moves forward — and never lead it. Stop: the hair should swing back and
   settle into the idle pose within about a second, then hold (FR-003, FR-004, SC-001, SC-002).
5. Turn on the spot and stop: the hair swings out and settles again.
6. Return to the editor and confirm the hair is once more exactly the animated pose — Play
   resumes from rest, not from a frozen mid-swing (FR-008, D4 seeding).

## 3. Walkthrough B — the direction is right, at any facing

This is the check the reference fails (spec Origin, defect 1).

1. In Play, walk the actor forward, then turn it 180° (walk the other way) and watch the chain
   against the *world*, not against the character: the sway must follow the world direction of
   travel identically in both cases (FR-005, SC-003).
2. Put two instances of the same actor in a zone at different yaws, move each in the same world
   direction, and confirm their chains bend the same way in the world.
3. Look along a chain and confirm every joint contributes bend — including the segment leaving
   the **first** chain joint (the hairline), which the reference never rotates (FR-006, SC-004).

## 4. Walkthrough C — the manifest rules are loud, and harmless

1. Edit `assets/actors/Akane/akane.manifest` and misspell one hair joint (e.g.
   `J_Sec_Hair2_0X`). Reload.
2. Expected: a report naming the actor and the joint, that chain is simply gone, and the actor
   **still spawns and renders** with the chains that resolved (FR-009). Other actors are
   unaffected.
3. Restore the name; set a chain's `Stiffness` to `2.0`; reload. Expected: the same
   drop-and-report, for the stiffness (FR-010).

## 5. Walkthrough D — the render path is untouched (FR-014, FR-015)

1. Confirm no shader or pipeline changed: `git status` shows nothing under `shaders/` or
   `render/`.
2. Confirm cel shading and the outline (specs 040, 041) look exactly as before on the same actor
   — the sway moved geometry, it did not change how geometry is lit or edged.
3. Confirm an actor with no sway chains is untouched, and that the environment, props, portraits,
   UI and the editor's own drawing are unchanged.

## Cleanup

Nothing to clean up. Sway persists nothing, allocates nothing per frame, and writes no authored
state: no file, no GPU resource, no history step.

## What this does not cover (deferred, per the spec's Assumptions)

- **Wind.** The port has no zone wind, so there is no wind force and no wind field in the
  manifest. A later wind phase adds a field rather than changing an existing one's meaning.
- **Collision.** The rope is kinematic, as the reference's non-goals state: a chain may pass
  through the body or the ground.
- **Per-instance or in-editor sway authoring.** Stiffness is the asset's, and there is no editor
  surface for it.
- **Editor preview of sway.** Play-only, matching the reference.
- **Non-uniformly scaled skeletons.** The arithmetic composes rotations and assumes a unit-scale
  skeleton; a non-uniformly scaled joint is out of scope (research D2).
