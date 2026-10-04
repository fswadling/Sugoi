# Research: Print & Trigger Cue Ops

## D1 — A toggle names a place and a trigger; `Zone.tryTriggerByName` resolves it

**Decision**: `EnableTrigger` / `DisableTrigger` each carry a `Place` and a trigger name. The engine resolves
the named place's zone through `state.Zones` (no-op when the place is not present), then the trigger through a
new `Zone.tryTriggerByName zone name : EntityRef option` — the first live trigger whose `Base.Name` matches, in
slot order, exactly as `Zone.tryWaypointByName` resolves a waypoint.

**Rationale**: this is the project's cross-zone convention (the spec's recorded answer): cue ops were designed
to span zones, and zones are transient. Naming a place lets a cue arm a trigger in a zone it is not in, and the
absent-place case falls out as a no-op (the actor ops' rule). There is no global trigger index (triggers are
per-zone, not unique across the world like a cast member), so a place plus a name is the right key — the same
shape the zone ops use for their place. `tryWaypointByName` already establishes the lookup idiom to mirror.

**Alternatives considered**: active-zone only (odin parity) — rejected: it cannot reach a trigger in another
zone, which the cross-zone cue design needs, and it would surprise alongside the zone/actor ops. A trigger
index in `GameplayState` — rejected: triggers are not globally unique and are per-zone, so an index would be
new machinery for no case the spec has.

## D2 — The toggle writes the flag through `Zone.setTrigger`; the runtime state is the system's

**Decision**: on a hit, the engine reads the trigger (`Zone.tryTriggerAt`), and — unless `Base.Enabled` already
holds the target — writes it back with `Zone.setTrigger zone ref { state with Base = { state.Base with Enabled = enabled } }`.
It touches only the authored `Enabled` flag; the runtime `Triggered`/tracked-prompt state is recomputed by the
next `Systems.updateTriggers`, exactly as the Inspector's checkbox behaves. A place that is not present, a name
the place holds no trigger for, and a redundant toggle each perform nothing and report their reason on stderr.

**Rationale**: `Enabled` is what `Systems.updateTriggers` already honours ("a disabled trigger is never inside")
and what `specs/058` documents; setting only it keeps the toggle a pure flag flip and lets the system own the
derived state. The no-op-and-log rule is the `specs/066` convention, so a re-run cannot fault.

**A diagnostic helper is needed for non-actor ops.** The existing `CueEngine.noOp` is shaped for an actor
(`noOp op character reason`); the trigger ops have no character. Add a small
`noOpTrigger op place trigger reason` that prints `cue: <op>(<place>/<trigger>) performs nothing: <reason>`,
rather than reshaping the actor helper and its (many) call sites.

## D3 — `Print` writes to the console

**Decision**: `Print of text: string` is a no-progress leaf that writes the text to stdout with
`printfn "cue: %s" text` when the walk reaches it, then is done.

**Rationale**: the spec's recorded answer — a developer-facing trace, matching odin's `fmt.println("[Cue_Op]", …)`
and consistent with the cue's stderr diagnostics (same `cue:` prefix, so a line is easy to grep). It is not
`Exposit`, which is player-facing dialogue (`specs/057`).

**Alternatives considered**: the on-screen `DebugOverlay` — rejected: `Print` is an authoring-time trace and the
console is where the cue's other messages already go; an overlay would need layout/state the op does not warrant.

## D4 — Editor: a place chooser (control) and a trigger-name combo (panel)

**Decision**: `CueTree` gains the ops in `kinds` (a new **Triggers** group for the two toggles; **Print** under
**Debug**, or the existing **Timing** group), `kindName`, `label`, `executing` (all three are *not* executing),
`children` (none), `defaultOfKind` (`Print ""`; `EnableTrigger(Place.start, "")`), `withPlace` for the toggles
and `textOf`/`withText` for `Print`. Its `drawEditor` extends the existing place-combo arm
(`SetZone|AddZone|RemoveZone|PreloadZone` / `SetActor`) to include the two toggles. `CueEditor.drawNodeText`
gains a branch for the toggles: a combo over `zoneOfPlace place`'s triggers (`slot.Value.State.Base.Name`),
with a typed fallback when the place is not present, committed through a new `CueTree.withTrigger`. `Print`
falls through to the generic text field (`textOf`/`withText`), so no new `EditorState` buffer or panel signature
is needed.

**Rationale**: the control (`CueTree`) has no zone read (`FR-006`), so the place — a closed set — is offered
there as the zone ops' is, and the *name in a place* — which needs a zone — is offered by the panel
(`CueEditor`), exactly as `SetActor`'s waypoint is. `Print`'s text is a plain field, which the panel already
draws for `textOf`/`withText`.

**Alternatives considered**: a `…State`/buffer for the trigger field — rejected: a combo reads the current name
from the node and writes through `withTrigger`, needing no buffer, as `drawWaypointCombo` already demonstrates.

## D5 — Verification is headless

**Decision**: assert through `CueEngine.tick`. Spawn a trigger in a place (`Zone.spawnTrigger`), run a cue
carrying a toggle naming that place and trigger, and assert `Zone.tryTriggerAt` reports the flipped `Enabled`;
step `Systems.updateTriggers` to observe firing/tracking stop and resume. Assert the no-ops (absent place,
missing name, redundant toggle) change nothing, and that no op writes `state.Editor.Undo`. `Print` is asserted
to be a no-progress leaf that ends immediately; its stdout is a side effect the suite does not capture (as the
stderr diagnostics are not). `Zone.tryTriggerByName` is asserted directly (first match wins).
