# Quickstart: Triggers

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- For the interactive scenarios: a display, a zone with an actor, and that actor
  bound as the player (*Set as Player* in the Inspector).

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # includes ZoneTests, SpawningTests, SystemsTests
```

The storage and edit paths are covered in `test/ZoneTests.fs` and
`test/SpawningTests.fs`, and the containment rule exhaustively in the new
`test/SystemsTests.fs` — the update needs neither a physics world nor a GPU. The
Inspector widgets, the wireframe, and the play overlay need an ImGui frame and are
covered by the scenarios below.

## Validation scenarios

Automated (headless):

1. **A trigger is a zone entity (FR-001)** — a spawned trigger resolves to its base
   entity, is alive, lives in `zone.Triggers`, and reports `bindingNone`; renaming
   it and duplicating it behave as for other entities, and a duplicated trigger
   starts with its own payload.
2. **Names and transforms (FR-003)** — `setName` and `setTransform` on a trigger
   ref work as for the other kinds and keep the ref valid.
3. **Removal and slot reuse (FR-001)** — removing a trigger makes its ref stale,
   and a trigger spawned into the freed slot does not resolve the stale ref.
4. **Containment (FR-005, SC-003)** — for a trigger of known radius and height, the
   update reports inside for a player on each boundary and just inside it, and
   outside for a player just beyond the radius, just above the top, and just below
   the base.
5. **Entry versus remaining inside (FR-007, SC-004)** — a player held at the same
   inside position across repeated updates keeps reporting inside, so a
   once-per-entry action is derivable from the state rather than from a repeat.
6. **Disabled and absent player (FR-006, SC-005)** — a disabled trigger reports
   outside even with the player inside it, and clears any state it held; with no
   designation, or a designation whose actor is gone, every trigger reports
   outside.
7. **Several triggers at once (FR-005)** — a player inside two overlapping triggers
   is reported inside both, with no exclusivity.
8. **Interactable tracking (FR-008, SC-006)** — with two interactable triggers
   inside, the nearer is answered; moving the player nearer the other switches it;
   a non-interactable trigger inside is never answered; a disabled interactable one
   is never answered; leaving clears the answer.
9. **No frame cost (SC-007)** — the update is callable over a zone of triggers in a
   loop with no allocation (asserted the way the suite's other allocation checks
   are, or by inspection of the loop returning a struct-free answer).

Interactive (editor; needs a display):

10. **Place and size a volume (US1/AC1-3, SC-002)** — in edit mode press *Add
    Trigger*: it appears at the origin, listed in the Outliner. Move it with the
    gizmo, then drag its radius and height in the Inspector, and confirm the
    cylinder wireframe matches and follows.
11. **Authoring parity (US1/AC4-5)** — rename the trigger and confirm the Outliner
    row follows; duplicate it and confirm a second cylinder appears; delete it and
    undo, and confirm each of those is one undo step.
12. **Detection while playing (US2, SC-003)** — enter play mode and walk the player
    into the volume: the overlay lists the trigger's name while inside, and stops
    when the player leaves. Walk along the radius boundary and past the top of the
    volume, and confirm the readout stops exactly outside.
13. **Disabled (US2/AC4)** — in the Inspector untick Enabled, play again, and
    confirm standing in the volume reports nothing; tick it back and confirm
    detection returns.
14. **No player (US2/AC5)** — with no player bound, play and stand any actor in the
    volume: nothing is reported.
15. **Interactable prompt (US3, SC-006)** — mark the trigger interactable and give
    it a prompt, play, and confirm the prompt appears while inside and not after
    leaving; add a second interactable trigger nearer the player and confirm the
    prompt switches to it.
16. **Triggers are not colliders (FR-011, SC-008)** — walk through a trigger volume
    and confirm the player passes through it, that nothing else in the scene
    changes, and that the frame-rate overlay stays at the target.
17. **Zone scoping (FR-013)** — add a second zone, switch: the new zone draws no
    cylinders and detects nothing; switch back and confirm the triggers and their
    state are as they were.

## Expected outcome

- A zone holds trigger volumes: named cylinders with a position, radius, height,
  an interactable flag, and a prompt, authored and undone like any other entity,
  and drawn to scale as a cylinder wireframe.
- While playing, each trigger reports whether the player is inside it, a disabled
  trigger and a missing player report nothing, and the nearest interactable trigger
  the player is inside has its prompt shown.
- **Nothing runs a cue**: firing on entry and interacting are the cue engine's, and
  the play-mode overlay is what stands in for them until then.
- Triggers collide with nothing and affect no other entity, and the existing test
  suite still passes.
