# Contract: In-flight Actor Ops Resolve Once (`specs/074` US4)

What an in-flight actor cue op carries, and what each tick does — so a running move, turn or blend never sweeps the
scene by name.

## The `…State` cases — `world/Cue.fs`

```fsharp
| BlendState      of actor: string * place: Place * handle: Handle * morph: bool * fromName: string
                    * toName: string * initialFrom: float32 * initialTo: float32 * target: float32
                    * duration: float * endsAt: float
| MoveActorState  of actor: string * place: Place * handle: Handle * points: Vector3[]
                    * speed: float * duration: float * endsAt: float * initialRotation: Quaternion
                    * finalRotation: Quaternion * blend: float * movingClip: string * idleClip: string
| RotateActorState of actor: string * place: Place * handle: Handle * initialRotation: Quaternion
                    * targetRotation: Quaternion * duration: float * endsAt: float
```

- `handle` is `core/SlotMap.fs`'s `Handle` (`Index` + `Generation`); `place` is `world/Place.fs`'s `Place`. Both are
  compiled below `world/Cue.fs`, so the cue vocabulary stays free of zone/entity/asset/device types (FR-019).
- `actor` (the `string`) is kept as the row label and for diagnostics; it is **not** used to resolve during a step.

## The step — `world/CueEngine.fs`

Each tick, in order, ends the op (`true`) or proceeds:

| Check | Condition | Then |
|-------|-----------|------|
| place present | `not (state.Zones.ContainsKey place)` | end |
| slot alive | `not (zone.Actors.IsAlive handle)` | end |
| still the same actor | `(&zone.Actors.Get handle).State.Base.Name <> actor` | end |
| active zone (`Move`/`Rotate` only) | `place <> state.ActiveZone` | end |
| otherwise | | proceed (`&zone.Actors.Get handle`) |

`state.Zones` is read with `ContainsKey` + the indexer — **not** `TryGetValue`, which allocates a `(bool * Zone)`
tuple. `BlendState` has no active-zone check (a blend acts in the actor's own zone).

## Begin — the one-time name resolve

`beginMove`/`beginRotate`/`beginBlend` resolve once, as today, via `tryResolveActor` (the name scan), keep the
actor's `handle` (`handleOf ref`) and `place`, and hold the `…State`. A miss is a no-op (`Fin`), as `specs/073`.

## Postconditions

- **O(1) per tick**: no name scan while an op is in flight (FR-015).
- **No per-tick allocation**: the step returns through slot-map access, not an `option`/tuple (FR-016).
- **Pinned actor**: a same-named newcomer added mid-flight does **not** take over the op (FR-017, SC-009); removing
  the actor (or her place) ends it.
- **One-shot ops unchanged**: `RemoveActor`, `EnablePlayer`/`DisablePlayer` and the `begin*` resolutions still
  resolve by name (FR-018).

## Explicitly unchanged / out of scope

- **The authored actor ops** (`MoveActor`, `RotateActor`, `Animate`, … ) — still name the actor by `string`; no op
  added or removed, no argument type changed on the authored forms.
- **The cue walk and its `…State` cases' other fields** — the captured blend weights, spline points and rotations
  are unchanged; only the actor's resolution is.
- **Zone/trigger/camera ops** — unchanged.
