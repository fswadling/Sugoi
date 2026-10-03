# Quickstart: Inspector Environment Readout

Validation guide for the feature. It is a display relocation, so most steps are "look at the
panel" — there is no pure part to assert headlessly (research D6).

## Prerequisites

- `dotnet` SDK.
- An environment asset discoverable under the Asset Browser (e.g. `island` or `artroom`),
  and at least one zone.

## Build & test

```sh
dotnet build
dotnet test   # confirms nothing else regressed; this feature has no headless test
```

## Validation scenarios

1. **The readout is in the Inspector (FR-002, FR-003)** — with an environment loaded into
   the active zone and **nothing selected**, the Inspector shows `Environment: <name>`
   directly above the **Lighting** section.
2. **The readout is gone from the Outliner (FR-001)** — the Outliner's Zones section lists
   the project's zones (and any residency glyphs) with **no** `Environment:` line between it
   and the Actors section.
3. **Absent environment (FR-004)** — a zone with no environment reads `(none)`, with the
   Lighting section still beneath it.
4. **It follows the active zone (FR-005)** — switch between a zone with an environment and
   one without: the readout changes to match each, with no manual refresh. Instantiate a
   different environment into the active zone: the readout names the new one in the same
   frame.
5. **Entity selected (FR-007)** — select an actor/prop/waypoint/trigger: the Inspector shows
   that entity as before, with no environment readout and no Lighting section. Deselect
   (click the active zone's row in the Outliner): the readout and Lighting return.
6. **Read-only (FR-006)** — the readout offers no button, field, or drop-down; assigning or
   unloading an environment is done from the Asset Browser / Zone Registry as before.
7. **Residency is separate (research D4)** — with an environment loaded and resident, the
   Outliner's zone row still shows spec 050's residency glyph; unload it for memory (small
   budget or many environments) and the Inspector readout still names the environment, while
   the glyph changes.

## Expected outcome

- The active zone's environment name is read in the Inspector, above the light settings, and
  never in the Outliner.
- The value shown matches what the Outliner displayed before the change, `(none)` included.
- `dotnet build` is clean and `dotnet test` passes (no new test; the existing suite is
  unaffected).
