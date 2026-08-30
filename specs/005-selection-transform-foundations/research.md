# Research: Selection + Transform Foundations

Decisions made while implementing the 2c data/editor-state layer. Retrospective —
these are the rationale behind commit `ca73afd`.

## Decision 1 — Slot-map index access is non-generational

- **Decision**: add `tryGetByIndex`/`setByIndex`/`removeByIndex` that read/write a
  slot by `uint32` index alone, without a generation check, alongside the existing
  generational `get`/`free` (which take a `Handle`).
- **Rationale**: the editor addresses entities by `EntityRef { kind; index }` — a
  handle with no generation, matching `ocaml-game`'s `Slot_map.get/set/remove`.
  The generational path stays for runtime systems; the index path is the editor's.
- **Alternatives considered**: reuse the generational `get`/`free` (rejected —
  `EntityRef` has no generation, so it can't); carry a generation in `EntityRef`
  (rejected — editor-time handles are simpler without it, and undo stores values).

## Decision 2 — `setByIndex` re-occupies freed slots

- **Decision**: `setByIndex` overwrites an alive slot, or re-occupies a freed slot
  at the same index (rebuilding the free list).
- **Rationale**: undo of a delete must restore the entity at its *original* index
  so other references stay valid. This mirrors `ocaml-game`'s `Slot_map.set`.
- **Alternatives considered**: restore via `add` (rejected — `add` hands back the
  free-list head, which may differ from the original index).

## Decision 3 — `EntityRef` uses `option`, not a sentinel

- **Decision**: `EntityRef = { Kind; Index : uint32 }`; "no entity" is expressed
  as `EntityRef option`. The `NO_ENTITY` sentinel (`Index = -1`) was removed.
- **Rationale**: the sentinel is odin-game cruft — C-like code needs a null value
  for inline struct fields, but F# has `option`. `EditorState.Selection` was
  already `EntityRef option`, so nothing used the sentinel.
- **Alternatives considered**: `Index : uint32 option` (rejected — a ref with a
  `Kind` but no index is an awkward half-state); keep `Index : int` (rejected —
  mismatches the slot map's `uint32` indices and forces casts at call sites).

## Decision 4 — Slot-map indices stay `uint32`

- **Decision**: keep `uint32` for slot indices/generation/free-head and
  `EntityRef.Index`, centralizing the `int`↔`uint32` conversions inside
  `SlotMap.fs` (`.NET` arrays are `int`-indexed, so each access casts once).
- **Rationale**: consistent with the odin port (fixed-capacity arrays, `u32`
  indices, `UInt32.MaxValue` free-list sentinel) and with the earlier `Count` →
  `uint32` change. Callers pass `uint32` straight through — no casts leak out.
- **Alternatives considered**: switch everything to `int` (rejected — reverts the
  `uint32` direction and reintroduces a negative sentinel).

## Decision 5 — Props live inside the environment

- **Decision**: `Environment = { Base; Props : SlotMap<Prop> }`; the zone holds
  `Actors` and `Environment` (not a flat `Props` slot map).
- **Rationale**: matches `docs/ARCHITECTURE.md` and `ocaml-game`'s
  `environment_entity = { entity; props }`. The environment owns its props.
- **Alternatives considered**: a flat `Zone.Props` (rejected — would need a later
  migration to align with the architecture).

## Decision 6 — Debug-draw shapes are box + sphere only

- **Decision**: add `wireframeBox` (12 edges) and `sphere` (three great circles) to
  `DebugDraw`, deferring `cylinder` (triggers) and a standalone `circle` (rotate
  rings are drawn by the viewport with its own basis).
- **Rationale**: these two cover entity markers and move/scale gizmo tips for 2c;
  minimalism — add the rest when their consumers arrive.
- **Alternatives considered**: a general `circle` primitive (rejected — unused by
  these two shapes; the viewport draws rings directly).
