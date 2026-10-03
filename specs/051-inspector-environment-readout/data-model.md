# Data Model: Inspector Environment Readout

**This feature adds no data.** It chooses where an existing value is displayed, so the model
is the (unchanged) shape of the facts the readout reads. Recording it here is what makes
"adds no field" checkable.

## What the readout reads

| Fact | Source (unchanged) | Meaning to the readout |
|---|---|---|
| The active zone | `GameplayState.activeZone state` | The zone the Inspector takes as its fallback subject when nothing is selected. |
| The zone's environment | `Zone.Environment : Environment option` | `Some` → the readout names it; `None` → the readout reads `(none)`. |
| The environment's name | `environment.Base.Name` | The string shown after `Environment: `, verbatim as the Outliner showed it. |

No new field, record, union case, or option is introduced. The `Zone` record, its
`Environment` field, the `Environment` entity, and `GameplayState.activeZone` are all as they
were.

## What is deliberately not modelled

- **Residency**: whether the environment's data is resident is `Assets.isEnvironmentResident`
  (spec 050) and stays a separate fact, shown by spec 050's glyph in the Outliner — not by
  this readout (research D4).
- **A selected entity**: the readout has no relationship to `state.Editor.Selection` beyond
  the branch that draws it. An entity and the zone are not shown together (research D2); no
  state tracks that, because the branch already excludes it.
- **Any editable environment setting**: none is introduced; the readout is read-only
  (research D3).

## Validation rules

- The readout is total: every `Zone.Environment` value yields a string (`(none)` or the
  name). There is no error path.
- The string for a given zone matches what the Outliner previously displayed for that zone —
  the relocation is behaviour-preserving for the value (spec FR-004, SC-002).
