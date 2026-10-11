# Contract: Cue Actors Addressed by Name

What a cue says about an actor after the roster left the cue vocabulary (`specs/073`), and what the editor
offers.

## The ops — `world/Cue.fs`

```fsharp
| AddActor of place: Place * actor: string * asset: string * waypoint: string
| RemoveActor of actor: string
| MoveActor of actor: string * path: string * speed: float * movingClip: string * idleClip: string
| RotateActor of actor: string * waypoint: string * duration: float
| Animate | CrossFade | Morph | CrossMorph   // by `actor: string`
| EnablePlayer of actor: string | DisablePlayer of actor: string
// ... and the run-form cases: MoveActorState/RotateActorState/BlendState carry `actor: string`
```

Every actor op names its actor by a **plain string**. No op names a `Roster.Character`, a `Casting.Look`, or the
cast binding. The place, where named, is still a `Place`.

## Resolution — unchanged scan, string key

- **The actor** is the first live actor whose `Base.Name` equals the op's string, over the present places in
  `Place.all` order (`Zone.tryActorByName`; `GameplayState.tryActorByName`). A miss ⇒ the op performs nothing.
- **The place** (`AddActor`) is a `Place`; not present ⇒ nothing.
- **The waypoint** (`AddActor`, `RotateActor`) is a name in the relevant zone; not resolved ⇒ nothing.
- **The asset** (`AddActor`) must name a discovered actor asset; otherwise ⇒ nothing.

There is no cast index, no role, and no binding in this path.

## `AddActor` — a pure spawn naming an asset (spec FR-003)

| Condition | Then |
|-----------|------|
| place absent, waypoint unresolved, or the asset not discovered | performs nothing |
| otherwise | create an actor named `actor`, in the named place, at the waypoint's pose, drawn from the named asset |

A second `AddActor` of the same name places a second actor (a name is a label).

## `RemoveActor` and the acting ops

Find the actor of the name across the present places; act on it, or perform nothing when none is found.

## The editor

- **Existing-actor ops**: the actor chooser offers the **viewed place's live actors** (the active place; the op
  names none), as a combo mirroring the waypoint chooser — nothing from the roster. A typed field appears only
  when the place itself is absent.
- **`AddActor`**: a **place** combo (closed set), an **asset** combo over discovered actor assets, a typed
  **name** (defaults to the asset's), and the named place's **waypoint** chooser.
- **Blends / `MoveActor`**: the clip/morph choosers offer the **placed actor's** asset's names; nothing when
  she is not in the viewed place, and the field falls back to typing.

## Postconditions

- **No roster in cues**: `world/Cue.fs` names no `Roster`/`Casting` value (spec FR-004, SC-001).
- **The chooser is the scene**: the actor chooser offers only the viewed place's actors (spec SC-002).
- **Art from the placed actor**: blend choosers read her asset; no binding needed (spec SC-003).
- **A name is a label**: duplicates resolve to the first match, silently (`specs/072`).
- **Runtime only**: no actor op writes the undo history (`specs/064` FR-007).

## Explicitly unchanged / out of scope

- **The roster, the look vocabulary, the cast binding, the cast panel, the dialogue portrait path**
  (`cast/Roster.fs`, `world/Casting.fs`, `editor/CastPanel.fs`, `world/Dialogue*.fs`) — retained for portraits
  (spec FR-007, SC-004).
- **The zone / trigger / camera ops** — unchanged.
- **The narrative engine** — reads the same roster; unaffected.
- **No op added or removed** — only the actor argument's type and `AddActor`'s shape change.
