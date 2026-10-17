# Quickstart: Per-Actor Physics Modes and Properties

**Feature**: `specs/102-actor-physics`

## Prerequisites

- .NET 10 SDK.
- The native libraries built or present under `native/` (the existing setup for
  running Sugoi).
- A working copy of the repository at the feature branch.

## Build and test

```bash
dotnet build
dotnet test
```

The physics and editor tests are headless. The Jolt `Foundation.Init` is
process-global, so the test project already serialises physics collections.

Focused run once the new tests exist:

```bash
dotnet test --filter "FullyQualifiedName~ActorPhysics"
```

## Automated validation

`test/ActorPhysicsTests.fs` covers the behaviour; the expected outcomes below
map to the spec's acceptance scenarios.

### Mode behaviour

- A newly placed actor has `Mobility = Kinematic`.
- A kinematic actor placed in front of a dynamic player is not displaced over
  repeated physics steps, and the player is blocked by her.
- A `NoPhysics` actor has `Actor.Physics = None`; the player passes through her.
- A dynamic actor is displaced by the same collision.

### Properties

- Editing mass, friction, restitution, or gravity factor leaves the actor's
  position, facing, scale, and name unchanged.
- Two dynamic actors with materially different mass respond differently to the
  same collision.
- A dynamic actor with restitution 0 does not bounce.
- A dynamic actor with gravity factor 0 does not fall; a higher factor falls
  faster.
- A dynamic actor with higher friction slides less on the same surface.

### Editor and undo

- One mode change records exactly one `ActorPhysicsSet` step.
- Undo restores the previous setting and body behaviour; redo reapplies it.
- A no-op edit records no step.
- Duplicating a kinematic or dynamic actor copies her setting.
- The designated player's Inspector offers only `Dynamic`.
- Designating a non-dynamic actor promotes her in the same history step.
- Undoing a `SetPlayer` step that restores a kinematic previous player promotes
  her to dynamic.
- Activating a zone whose designated player is kinematic promotes her to dynamic.

### Cue behaviour

- A `MoveActor` cue drives a kinematic actor along her path and stops her at a
  wall placed across the path.
- A `MoveActor` or `RotateActor` cue against a `NoPhysics` actor reports a
  no-op and leaves her state intact.
- A cue `EnablePlayer` on a kinematic actor promotes her to dynamic.
- An `AddActor` cue creates the actor as kinematic.

### Scenario and persistence

- A scenario fork materialises each actor with her authored setting.
- Save and reload round-trips every actor's setting.
- A project file with no `Physics` field on an actor decodes that actor as
  dynamic.
- A hand-edited out-of-range property is reported through the project error path
  and no partial library is installed.

### Viewport selection

- A `NoPhysics` actor can be selected by clicking her marker/volume in the
  viewport.
- A `NoPhysics` actor in front of a body-backed actor is selected when she is
  nearer the ray.

### Performance

- `Systems.syncActors` allocates nothing when run over Dynamic, Kinematic, and
  `NoPhysics` actors.
- The gameplay loop keeps the project's target frame rate with the feature.

## Manual validation

1. Run the game with `dotnet run`.
2. Open the Asset Browser and place an actor.
3. Select her in the Inspector. Confirm the `Physics` combo shows `Kinematic`,
   `Mass` and `Gravity Factor` are hidden, and `Friction` and `Restitution`
   remain.
4. Enter play mode (Play) and walk into her. Confirm she does not move and the
   player is blocked.
5. Return to edit mode, set the mode to `Dynamic`, set `Mass` to a low and then
   a high value, enter play mode, and collide with her. Confirm the heavier
   setting is displaced less.
6. Set the mode to `NoPhysics`. Confirm `Show Collider` shows no capsule, the
   player walks through her, and she is still selectable in the viewport.
7. Undo the mode change and confirm the previous mode and body return in one
   step. Redo and confirm the change returns.
8. Designate the kinematic actor as the player, then designate another actor and
   undo that designation. Confirm the restored player is dynamic.
9. Duplicate the actor and confirm the copy has the same setting.
10. Capture the zone into a scenario, fork it, and confirm the materialised
    actors keep their settings.
11. Save (automatic), restart the game, and confirm the settings survive.
12. Open the existing `project.manifest` from before the feature and confirm its
    actors behave as dynamic actors.
13. Hand-edit one actor's `Mass` in `project.manifest` to an out-of-range value,
    reload, and confirm the project error path reports it and no partial library
    is installed.

## Expected results

- No actor tips over in any mode.
- A kinematic NPC is solid but never pushed by the player.
- A no-physics actor has no collision volume and does not block anything.
- Physics edits are one undo step each and never move the actor on screen.
- Existing projects keep their previous actor behaviour.
