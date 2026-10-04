# Contract: Zone Cue Ops

The cue's zone vocabulary and what each op guarantees, at run time and in the editor.

## The ops

Every op is a leaf carrying a `place: Place` (a value from the closed union, never a string), performs its
effect where the walk reaches it, and costs the cue no subsequent time.

| Op | Precondition it acts on | Effect | Performs nothing when |
|----|-------------------------|--------|-----------------------|
| `SetZone p` | `p` present | `p` becomes the active place | `p` not present |
| `AddZone p` | `p` absent | `p` becomes present; its registered template (environment, light, props) is streamed in if it has one; its bodies are left detached (the zone is inactive) | `p` already present |
| `RemoveZone p` | `p` present and inactive | `p` becomes absent; its bodies and GPU resources are released; its environment is unloaded unless another present place shares it | `p` absent, or `p` is active |
| `PreloadZone p` | `p` present | the zone's environment load is requested (de-duplicated, non-blocking): its own environment, or — when it has none — the environment of its registered template, streamed into the zone first | `p` absent, or `p` has neither an environment nor a registered template |

The `SetZone` row is a **change of behaviour**: where a cue naming a not-present place used to raise, it now
performs nothing (`specs/061` deferred this; `specs/062` is the feature). `RemoveZone` naming the active place
is likewise a no-op, not an error.

## Presence

- **Present** = a place has a runtime zone in `GameplayState.Zones`; **absent** = it does not. At most one
  zone per place; a place is a compile-time constant.
- `AddZone` is the cue's only way to make a place present, and it never enters the zone — the active place is
  unchanged by it.
- `RemoveZone` is the cue's only way to make a place absent, and it only ever removes an *inactive* place; a
  cue that wants to leave a zone does `SetZone` first.
- Neither op touches `Editor.Undo`: cue zone changes are runtime effects, not authored steps. The editor's own
  add/delete (and their undo) are unchanged.

## Environment residency (a distinct term from presence)

- `PreloadZone` acts on residency, not presence: it starts a load for a place that is already present. When
  the zone has no environment of its own, it resolves the place's registered template, streams it in (so the
  zone has the content and environment the place is configured with), and loads that environment. A present
  place with neither an environment nor a registered template is the only no-op.
- A preload is **best-effort**: it is an ordinary on-demand request and is not reserved against later eviction.
  Its value is that the environment is resident before the switch; if budget pressure evicts it first, the
  switch simply reloads it (the existing lazy behaviour).
- **A preload never causes the active zone's environment to be unloaded.** When making room requires eviction,
  only environments that are not the active zone's are eligible, least-recently-used first. The gameplay layer
  pins the active zone's environment each frame; this contract does not widen that pin set.
- A preload de-duplicates against an environment already loading or resident, and returns immediately without
  loading on the calling thread.

## Editor authoring

- The cue panel offers **Add zone**, **Remove zone** and **Preload zone** (beside **Set zone**), each choosing
  a `Place` from the closed set — the same chooser the existing zone op uses. There is no text field for any of
  them; a place is a value, so a cue can never name a location the game does not declare.
- Each accepted edit is one step on the one undo history, like every other cue edit.

## No-op rule

- A zone op whose effect already holds, or that names a place it cannot act on, performs nothing and the cue
  continues. This is deliberate: a cue is runtime data and may run more than once, so a redundant op is the
  ordinary case rather than a bug. The editor's own functions keep failing loudly; only the cue path is
  forgiving.

## Not in this contract

- **Actor addressing** (naming actors in cues, keyed by a `Roster.Character`) is a separate successor of
  `specs/061`.
- **A strong preload reservation** (pinning a preload until it is used) is deliberately excluded; preloads are
  best-effort.
- **Persistence** of any of this: the cue and the runtime zone set are not serialized yet (roadmap phase 15).
