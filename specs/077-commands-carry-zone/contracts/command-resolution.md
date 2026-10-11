# Contract: Command place resolution (internal)

**Feature**: `specs/077-commands-carry-zone`

An internal engine surface, so the "contract" is the module API the rest of the code depends on. Only what this
feature changes is contracted.

## `state/Command.fs` — CHANGED

```fsharp
// Every zone-content case gains `Place` as its first field (see data-model.md for the full list).
type Command = ...

// The place a content command acts on; `None` for a state-level step and for `Batch`.
let placeOf (cmd: Command) : Place option

// Signatures unchanged: the zone passed in is the one the command names (resolved by the caller).
let rec apply (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) (cmd: Command) : unit
let rec undo  (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) (cmd: Command) : unit

// `UndoStack.push` coalesces consecutive transforms only when they share BOTH place and ref.
```

**Invariants**

- `placeOf` is total: `Some` for every zone-content case, `None` for `ZoneMove`/`ZoneAdd`/`ZoneDelete`/`SetCue`/
  `Advent`/`SetScenario` and `Batch`.
- `Command.apply`/`undo` keep refusing a state-level step (`invalidOp`), as today.

## `state/History.fs` — CHANGED

```fsharp
// The state-level runner resolves each content command's place to its zone before delegating.
let rec apply (shared) (state: byref<GameplayState>) (cmd: Command) : unit
let rec undo  (shared) (state: byref<GameplayState>) (cmd: Command) : unit
```

**Invariants**

- A content command is applied to `state.Zones.[place]`, **not** `GameplayState.activeZone`.
- An absent named place raises (a broken invariant).
- `Batch` recurses per sub-command; forward for apply, reverse for undo.
- `ZoneDelete`'s undo does **not** change `state.ActiveZone`: it creates the zone, `occupyZone`s the contents, then
  `Zone.deactivatePhysics` (the zone is not active).
- `ZoneMove`/`ZoneAdd`/`SetCue`/`Advent`/`SetScenario` keep their present behaviour.

## `editor/*` — CHANGED (construction sites)

Every command built in the editor names the **active place at build time** (`state.ActiveZone`):
`Selection.*Ref`, `Spawning.*` (and `instantiateZone`'s batch), `Editor`'s drag/nudge `Transform`s, `Inspector`
(via `Selection`), `CueEditor`, `AssetBrowser`'s `SetEnvironment`, `ZoneRegistryPanel`'s instantiate batch.

**Unchanged**: which edit records a step, and how many.

## Acceptance

1. An entity edit made in place A, with place B active, undoes in A's zone; B's camera and selection are unchanged
   (US1/SC-001).
2. A `Batch` naming two places applies to both and reverses as one step, active place unchanged (US2/SC-002).
3. No content command's undo/redo changes the active place (SC-003).
4. A single-place session is unchanged: one edit is one step; transforms coalesce per (place, ref); tests green and
   the build warning-free (SC-004).
5. `docs/EDITOR_DESIGN.md` and `docs/ARCHITECTURE.md` and the `Command`/`History` headers no longer state that a
   command carries no zone (SC-005).
6. Restoring a deleted zone leaves the active place, camera and selection untouched (SC-006).
