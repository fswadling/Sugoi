# Contract: A Zone's Identity is a Place

## Identity

- A runtime zone is a **`Place`** — a value from the closed union in `world/Place.fs`. There is no
  free-string zone identity anywhere: not in the zone map, not in the zone steps, not in the cue's zone op,
  not in the editor.
- A `Place` is a compile-time constant. It is never created or deleted at run time. Adding a location is a
  code edit (a new union case).

## The zone map

- `GameplayState.Zones : Dictionary<Place, Zone>`; `GameplayState.ActiveZone : Place`.
- **Present** = a place has a zone in that map; **absent** = it does not. At most one zone per place.
- `GameplayState.create` makes **`Place.start`** present and active, and nothing else.
- `switchZone place` makes a present place active; the active place is always present.
- `addZone place` makes an absent place present and active; `deleteZone place` makes an inactive present place
  absent (refused for the active place).

## Presence transitions

| From | Operation | To |
|------|-----------|-----|
| absent `p` | `addZone p` (Outliner add, or Instantiate) | present `p`, active |
| present `p`, `p ≠ active` | `deleteZone p` (Outliner remove) | absent `p`; active unchanged |
| present `active` | `deleteZone active` | **refused** (switch first) |

## Environment residency (distinct term)

- Presence and environment residency are independent: a present place may have no environment (`[-]`), a
  loading one (`[~]`), a loaded one (`[+]`), or a failed one (`[x]`).
- **Removing a zone must unload its environment** from the GPU (FR-010) — unless the environment is shared
  with a still-present place, in which case it is not unloaded on the removed zone's account.
- `Zone.release` stays the zone's resource teardown; the environment release is through the asset manager.

## The registry (config, not identity)

- `ZoneRegistry.Registry : { Templates : Dictionary<Place, ZoneTemplate> }` — sparse, orthogonal.
- `tryTemplate place` is the registry's question; a present place need not have a template, and a registered
  place need not be present.
- **Register** captures the active place's zone as that place's template.
- **Instantiate** makes the named place present and active (adding it when absent) and loads its template, as
  one undoable step.

## Diagnostics

- A place's name comes from `Place.placeName`, derived from the union (as `Advent.name`/`Roster.characterName`
  are). A zone carries no independent name.
