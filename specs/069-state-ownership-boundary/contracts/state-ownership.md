# Contract: State Ownership

## The rule

- A state member lives where its **readers** are.
- **`SharedState`** holds state the engine itself, or more than one screen, may read — state
  meaningful independently of the gameplay world.
- **`GameplayState`** holds state whose only readers are the gameplay screen and its editor layer —
  the gameplay world and the configuration of that world.

Authorship, persistence and a member's type (value vs. reference) do **not** decide placement. The
zones are authored and persisted and live on the gameplay state; the cast binding and the advent
flags are authored and (per the existing persistence story) also persisted and live on the shared
state, because a screen other than the world may read them.

## Membership

| `SharedState` | Why it is shared |
|---|---|
| `Gpu` | the engine's own render loop reads it (`Engine.fs`) — it lives below screens |
| `Assets` | any presenting screen may read it (a journal resolves portraits through it) |
| `Cast` | the slot → actor-asset binding; a journal/inventory may present it |
| `Advents` | story-progress flags a UI may gate on or show |

| `GameplayState` | Why it is not shared |
|---|---|
| `Zones` | the gameplay world (unchanged) |
| `ZoneRegistry` | the recipe table for those zones; only gameplay + its editor read it |
| `Physics` | the world those zones' bodies attach to; only gameplay reads it |
| camera, cue run, UI layer, dialogue, editor state, fade, runtime cast index | the screen's own runtime state (unchanged) |

## Invariants

- **One physics world.** It lives on the gameplay state; `GameplayState` owns every zone, so the
  world is still shared by every zone. A zone never holds it — `Zone.*` receives it as a parameter.
  Switching the active zone detaches the outgoing zone's bodies and attaches the incoming zone's.
- **One zone registry.** It lives on the gameplay state. A present place need not have a template,
  and a registered place need not be present.
- **No cycle, no ordering constraint.** `physics/Physics.fs` and `world/ZoneRegistry.fs` compile
  below `state/GameplayState.fs`; `Zone.*` and `Command.apply`/`undo` continue to take the world (and
  assets) as values, so no `Command → GameplayState` edge forms.
- **Lifetime is unchanged.** Both containers are constructed once in `ScreenManager.init` and held
  for the process; `GameplayState` is not cleared on a screen change.

## What would reverse a placement

- A **separate** screen that inspects physics (not a physics overlay as a mode of gameplay) would
  make the world cross-screen.
- A place/scenario-selection screen would be a cross-screen reader of the registry — but that job
  belongs to the scenario library (the documented cross-screen level concept), not the per-place
  registry, so it does not reverse this placement.

## The retired rationale

The previous comment justified a shared `ZoneRegistry` by *"the undo stacks are per zone, so
anything that must outlive every zone lives here (spec 034)."* The per-zone stacks were replaced by
one history on the gameplay state (`specs/048-unified-undo-stack`), and `docs/EDITOR_DESIGN.md`
D-022 already records that the reason is gone. The headers and docs state the readership rule in its
place; no document cites per-zone undo stacks as a placement reason.
