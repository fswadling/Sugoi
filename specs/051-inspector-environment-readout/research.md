# Phase 0 Research: Inspector Environment Readout

The feature is a display relocation, so the research is a short record of the decisions that
determine where the line goes and what it is allowed to do — not a survey of techniques.

## D1 — The readout lives in the Inspector's fallback branch, above Lighting

**Decision**: draw `Environment: <name>` in `editor/Inspector.fs`, in the branch taken when
`state.Editor.Selection` is `None`, immediately before `drawLighting` / the "Lighting"
collapsing header.

**Rationale**: the active zone is already the Inspector's **fallback subject** (spec 043,
D-024): with no entity selected it shows the zone's settings, and its one setting today is
the light. The environment is another thing that describes the zone, so it belongs in the
same branch, next to the light, rather than in a panel of its own or in the Outliner, whose
subject is the scene's structure. The user's instruction names exactly this position:
"the inspector panel for that zone, above the light settings".

**Alternatives considered**:
- *A "Zone" section in the Inspector that also appears when an entity is selected* —
  rejected: the Inspector shows one subject (research D2), and adding a second would be new
  selection state for a read-only line.
- *Keep it in the Outliner but restyle it* — rejected: the Outliner lists places and entities,
  and the line is neither; the inconsistency is the location, not the styling.
- *A new Zone panel* — rejected: a whole panel for one read-only line is not minimal.

## D2 — Absent while an entity is selected

**Decision**: the readout is not shown when an entity is selected; the Inspector shows the
entity exactly as before.

**Rationale**: it follows from D1. The Inspector has one subject, and an entity is not the
zone; drawing the zone's environment beside an entity would either be misleading (an entity
inherits nothing from the name) or require the two subjects to be shown together, which the
fallback model exists to prevent. The cost is accepted and recorded in the spec's
Assumptions: the Outliner showed the name unconditionally, so this feature gives up an
always-visible environment name in exchange for keeping the zone's settings in one place.

**Alternatives considered**:
- *Show the readout above every Inspector view* — rejected: it makes the zone a second,
  always-present subject and muddies what the panel is editing.
- *Show it in both panels* — rejected: duplication is the inconsistency being removed.

## D3 — Read-only; no command, no undo step

**Decision**: display the name with `ImGui.text`; add no control, no `Command` variant, and
no undo entry.

**Rationale**: changing or unloading an environment is already the Asset Browser's and the
Zone Registry's action, through existing commands (specs 035, 050). A read-only label is
what the Outliner line was; the feature changes its location only (FR-008).

**Alternatives considered**:
- *Make the readout a dropdown to switch environments* — rejected as scope creep: the spec
  asks to relocate a label, not to add an assignment surface.

## D4 — It does not couple to residency

**Decision**: the readout shows the zone's environment regardless of whether that
environment's data is currently resident.

**Rationale**: residency is a separate concern with its own indicator — spec 050's glyph in
the Outliner's zones row. The readout names *which* environment the zone holds; residency
says whether its data is on the GPU. Conflating them would give one line two meanings and
would duplicate spec 050's indicator.

**Alternatives considered**:
- *Fold the residency glyph into the readout* — rejected: spec 050 deliberately keeps
  residency a minimal glyph in the zones list; this feature does not move it.

## D5 — The value and its fallback are unchanged

**Decision**: `match zone.Environment with None -> "(none)" | Some environment ->
environment.Base.Name`, exactly the match the Outliner performed.

**Rationale**: a relocation must show the same string for the same state, so the `(none)`
case and the `Base.Name` source are carried over verbatim. `zone.Environment` is the option
the Outliner already read; the value stays on the zone and no field is added (data-model.md).

## D6 — What is tested, and what cannot be

**Decision**: no headless test; validation is the quickstart walkthrough plus
`dotnet build` / `dotnet test`.

**Rationale**: both panels are ImGui draw functions. They need a GPU device and a frame to
run, which the test project cannot open (the same reason spec 043's Inspector section and
spec 050's glyph are quickstart-only). The feature adds nothing pure — no derivation, no
state, no command — so there is nothing a headless test could assert that the walkthrough
does not already cover more directly.

**Consequence**: `dotnet test` is run to confirm nothing else regressed, not to cover this
change; the observable behaviour is verified by eye in the running editor.
