# Contract: Actors Addressed by Name

What a cue can say about an actor, and how each op finds it — after the cast role and the runtime cast index
are withdrawn (`specs/072`).

## The ops — `world/Cue.fs`

```fsharp
| AddActor of place: Place * character: Roster.Character * look: Casting.Look * waypoint: string
| RemoveActor of character: Roster.Character
// ... and the acting ops, unchanged in shape:
| MoveActor of character: Roster.Character * path: string * speed: float * movingClip: string * idleClip: string
| RotateActor of character: Roster.Character * waypoint: string * duration: float
| Animate | CrossFade | Morph | CrossMorph   // by character
| EnablePlayer of character: Roster.Character | DisablePlayer of character: Roster.Character
```

Every actor op carries a `Roster.Character` (the **closed roster**), never a free string for *who*. Only the
waypoint, the clip/morph names and the move path are strings, and they are names in a zone. `AddActor` names a
place; the others do not.

## Resolution — by name, no index (spec FR-003)

- **The character** is converted to its canonical name (`Roster.characterName`) and resolved by a scan of the
  **present places** in `Place.all` order for the first live actor whose `Base.Name` matches
  (`Zone.tryActorByName`). There is no cast index and no role.
- **The place** (`AddActor` only) is a `Place`; not present ⇒ the op performs nothing.
- **The waypoint** (`AddActor`, `RotateActor`) is a name in the relevant zone; not resolved ⇒ the op performs
  nothing.
- **The art** (`AddActor`) is `Casting.tryAsset { Character; Look }`; no entry ⇒ the op performs nothing.

## `AddActor` — a pure spawn (spec FR-006)

| Condition | Then |
|-----------|------|
| place absent, waypoint unresolved, or the look unbound | performs nothing |
| otherwise | create an actor named `characterName character`, in the named place, at the waypoint’s pose (position + facing), drawn from the binding for the look |

`AddActor` never looks for an existing actor. A second `AddActor` of the same character places a **second**
actor of that name (spec FR-008). To move a member, a cue removes her and adds her.

## `RemoveActor` (spec FR-003, FR-007)

Find the actor of the character’s name across the present places; when found, remove it (destroy the body,
release the renderer); when none is found, perform nothing. No place is named.

## The acting ops

`MoveActor`, `RotateActor`, the blends and the player toggles find their actor by the same name scan, then act
exactly as before (`specs/066`). A member not in the scene performs nothing.

## Postconditions

- **Address is the name**: an actor is found by `Base.Name`; the actor carries no role (spec FR-001, FR-002).
- **No index**: after any create, delete, undo, redo, zone delete or zone-delete-undo, no cast bookkeeping is
  touched (spec FR-009); there is none to touch.
- **No uniqueness**: two actors may share a name and resolve to the first match, silently (spec FR-008).
- **The active place is unchanged**: `AddActor` places a member; it never switches zones. A member placed in an
  inactive place exists, with her body detached from the simulation (research D6).
- **The cue moves on**: each op answers `ValueSome Fin`.

## Explicitly unchanged / out of scope

- **The roster** (`cast/Roster.fs`) — the closed set a cue names (spec FR-004).
- **The look vocabulary, the cast binding and the cast panel** (`world/Casting.fs`, `editor/CastPanel.fs`) —
  the binding still maps `(character, look) → asset`, and the panel assigns exactly as before; only its
  **spawn** control stops recording a role (spec FR-005, FR-010).
- **`SharedState.Cast`** is the cast **binding** and is retained; only `GameplayState.Cast`, the runtime
  index, is removed (the two share a field name).
- **The cue panel's clip/morph choosers** read the binding alone, via the first bound look in
  `Casting.allLooks` order (spec FR-013).
- **The dialogue portrait path** (`world/Dialogue.fs`, `world/DialogueRender.fs`) — resolves through the
  binding, never the actor (spec FR-005, US2).
- **The narrative engine** — reads the same roster; unaffected.
- **The undo history** — an actor op is a runtime effect (`specs/064` FR-007).
- **A distinct warp/move op** — not introduced; `AddActor` is a spawn alone (spec Assumptions).
