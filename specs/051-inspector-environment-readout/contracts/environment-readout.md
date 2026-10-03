# Contract: Inspector Environment Readout

The two editor surfaces this feature touches, and what explicitly does not change. The
environment readout moves from the Outliner's Zones section to the Inspector, where it joins
the zone's lighting as a zone-level setting. Nothing below the editor changes.

## `editor/Outliner.fs` — CHANGED (a deletion)

The block between the Zones section and the Actors section is removed:

```fsharp
// REMOVED
let environmentName =
    match zone.Environment with
    | None -> "(none)"
    | Some environment -> environment.Base.Name
do ImGui.text(sprintf "Environment: %s" environmentName)
do ImGui.text("")
```

`let zone = GameplayState.activeZone state` **stays** — the Actors / Props / Parts /
Triggers / Waypoints sections below still read it. Nothing else in the panel changes: the
Zones section (add / select / delete, and spec 050's residency glyph) is untouched, and the
panel now runs straight from the Zones section into the entity sections (FR-001).

## `editor/Inspector.fs` — CHANGED (a read-only line)

A small helper draws the readout, and the no-selection branch calls it above the Lighting
section:

```fsharp
// Read-only; `(none)` when the zone holds no environment. The value is the one the Outliner
// showed, carried over verbatim.
let private drawEnvironment (zone: Zone.Zone) : unit =
    let name =
        match zone.Environment with
        | None -> "(none)"
        | Some environment -> environment.Base.Name
    do ImGui.text(sprintf "Environment: %s" name)
```

```fsharp
    | None ->
        // No entity selected: the Inspector falls back to the active zone, whose environment and
        // lighting are the zone-level settings it edits (spec 043, FR-003).
        do drawEnvironment zone
        do drawLighting shared &state zone
```

The "Lighting" collapsing header is drawn immediately after, so the readout sits **above the
light settings** (FR-002, FR-003). `Zone` is already the module's type for this branch
(`drawLighting`, `drawAnimations`, `drawMorphs` all take it), so no new `open` is needed.

### The entity-selected branch — unchanged (FR-007)

```fsharp
    | Some ref -> … the entity's name, transform, trigger/actor sections, as today
```

No readout is drawn here. An entity is not the zone, and the Inspector shows one subject
(research D2); this is the spec's accepted consequence, not an omission.

## Explicitly unchanged

- **Zone state** (`world/Zone.fs`, `Zone.Environment`, `GameplayState.activeZone`) — the
  readout reads them; nothing is added or written.
- **Commands and undo** (`state/Command.fs`, the undo stacks) — a display line records no
  step (FR-008, research D3).
- **Environment loading and residency** (`world/Assets.fs`, spec 050) — naming an
  environment does not load, touch, or unload it; the readout is independent of residency
  (research D4).
- **The residency glyph** (spec 050) — it stays in the Outliner's zones row, where spec 050
  put it; this feature moves only the name line.
- **The Asset Browser and Zone Registry** — assigning/instantiating an environment is still
  their action; no control is added to the readout (FR-006).
- **Screen, renderer, shaders, physics** — not editor surface, untouched.
- **Persistence** — nothing is written to disk (phase 15 owns that).
- **Tests** — nothing pure is added, so no test file changes (research D6).

## Documentation back-links

- `docs/EDITOR_DESIGN.md` — the Inspector module-map entry now names the environment beside
  lighting; the D-024 note already describes the active zone as the fallback subject the
  readout joins.
- `specs/011-environment-parts-props/quickstart.md` — scenario 8 moves with the readout:
  "the Outliner's top line shows `Environment:`" becomes the Inspector readout above the
  Lighting section.
