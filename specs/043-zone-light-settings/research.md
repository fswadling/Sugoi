# Phase 0 Research: Zone Light Settings

The capability is settled by the reference (`odin-game`) and by what the port already has; the work
is in the choices the reference does not pin down for *this* codebase — where the authored type
lives, where the controls sit in the port's own editor, how a light change is committed and undone,
and how an unedited scene is kept pixel-identical. Sources are in the spec's Origin.

## Decision 1 — The four settings, authored as angles plus a colour and a scalar

- **Decision**: a zone's light is `LightSettings { Pitch; Yaw; Color; Ambient }` — sun pitch
  (degrees above the horizon), sun yaw (compass degrees), an RGB colour and a scalar ambient.
- **Rationale**: exactly the four fields `odin-game` stores on a zone and exposes in its editor
  (`src/zone_registry.odin` `.odin:14`, `src/editor_properties.odin:714`), and exactly what its
  `Zone_Settings` records (`.odin:9`). Matching the reference keeps the port's authored model and
  its editor surface in step with the game it is a port of (spec Origin, FR-001).
- **Alternatives considered**: **a raw direction vector + colour + ambient** (no pitch/yaw) — the
  shape of the port's `Light`, but it gives the editor three unconstrained components instead of
  two bounded angles, and diverges from the reference; **pitch/yaw/colour only** (no ambient) —
  drops a control the reference ships and leaves the ambient term un-authorable; **per-actor or
  per-environment light** — neither is what the reference does (the light is a zone's), and a
  per-environment light could not light a zone with no environment loaded (Story 2).

## Decision 2 — The authored type lives beside `Light`, in `render/Texture.fs`

- **Decision**: `LightSettings`, its default and `toLight : LightSettings -> Light` go in
  `render/Texture.fs`, next to `Light`.
- **Rationale**: the derivation's only output is a `Light`, and `LightSettings` is its only input —
  keeping the pair together makes the mapping impossible to split. `render/Texture.fs` is already
  the module that owns scene lighting (`Light`) and materials (`Material`) despite its name, and it
  is compiled *before* every world module, so `ZoneTemplate.fs` and `Zone.fs` can hold the type
  without a new module or a reordering. The world modules already consume `Texture` types.
- **Alternatives considered**: **a new `world/LightSettings.fs`** — a module for one record and one
  function, and it would still have to sit after `render/Texture.fs` to see `Light`; rejected as a
  module that pays for nothing (Minimalism I); **in `world/Zone.fs`** — impossible, because
  `world/ZoneTemplate.fs` is compiled before `Zone.fs` and must name the type.

## Decision 3 — The derivation is the reference's spherical convention

- **Decision**: `toLight` derives the direction *toward the sun* as
  `{ sin(yaw)·cos(pitch), sin(pitch), cos(yaw)·cos(pitch) }` (angles in radians), normalized, and
  builds `Light` with that direction, the colour and an ambient vector of the scalar on all three
  channels.
- **Rationale**: the exact formula `odin-game` uses at every one of its light sites
  (`src/environment_render.odin:240`, `src/ocean.odin:284`, `src/shadow.odin:131`), so a pitch/yaw
  means the same thing in both games (FR-008). Pitch 0 = horizon and 90 = zenith; yaw sweeps a full
  turn. The formula already yields a unit vector, so the explicit normalize is for exactness.
- **Alternatives considered**: **a yaw/pitch convention of the port's own** — would make a setting
  ported from `odin-game` mean something different, defeating the point; **storing the direction
  and never deriving** — D1.

## Decision 4 — The defaults reproduce today's hard-coded light exactly

- **Decision**: the default settings are pitch `54.7356`, yaw `45`, colour white, ambient `0.15`.
- **Rationale**: FR-004 — an unedited scene must render exactly as it does today. The hard-coded
  direction is `normalize(0.4, 0.8, 0.4)`, whose inverse spherical angles are yaw `atan2(0.4, 0.4) =
  45°` and pitch `asin(0.8 / sqrt(0.96)) = 54.7356°`; the hard-coded ambient is the neutral grey
  `(0.15, 0.15, 0.15)`, i.e. the scalar `0.15` on all three channels. Applying D3 to these defaults
  reproduces the constant to float precision.
- **Alternatives considered**: **`odin-game`'s own defaults** (pitch 70, yaw 45, colour
  `{255,248,240}`, ambient `0.85`) — the reference's look, but adopting them would silently
  relight every existing scene and break FR-004; a later, deliberate re-lighting is a content
  decision, not this feature's; **keeping the light only in `Light` and leaving the defaults
  implicit** — no, the authored record needs a stated default.

## Decision 5 — Ambient is a scalar, and the port's neutral one

- **Decision**: ambient is authored as one intensity and expands to the neutral vector
  `(a, a, a)`.
- **Rationale**: the reference authors a scalar (`ambient: f32`), so a scalar is what the editor
  exposes (D1); but the reference expands it with a slight warm tint `(a, a·1.04, a·1.12)`
  (`src/environment_render.odin:257`) while the port's shader takes a neutral `(0.15,0.15,0.15)`.
  Keeping the port's neutral expansion is what preserves the look (FR-004, D4); adopting the tint
  is a relighting (rejected in D4). The authored *model* still matches the reference; only the
  expansion differs, and it is recorded here so the difference is deliberate rather than drift.
- **Alternatives considered**: **the reference's tinted expansion** — changes the look at the
  default, FR-004; **a full ambient colour (three authored channels)** — more control than the
  reference offers and more UI than the spec asks for (FR-001).

## Decision 6 — The controls live in the Inspector, with `dragFloat` and a new `colorEdit3`

- **Decision**: a "Lighting" collapsible section in the **Inspector**, shown for the active zone
  when no entity is selected: `dragFloat` for pitch (10–90), yaw (0–360) and ambient (0.1–1.5), and a
  new `igColorEdit3` binding for the colour.
- **Rationale**: the Inspector is the single editing surface for "the thing being edited", and a
  zone is a thing an author edits. Making the zone the Inspector's subject *when no entity is
  selected* gives the reference's behaviour (its properties panel carries the Light block) without
  a second selection concept: the `Selection` field stays `EntityRef option`, an entity selection
  wins, and the zone is the fallback — so the two can never disagree, and no new state has to be
  kept in step. Clicking a zone's row in the Outliner already switches to it, and the switch already
  clears the entity selection, so the Outliner needs almost no change: only the zone *already*
  active is a gap, because a switch to the zone in use is a no-op and would leave the selected
  entity on screen. A `GameplayState.selectZone` closes it — one call the Outliner's zone row makes
  instead of `switchZone` — and is testable headlessly, which the panel is not. `igDragFloat` is already bound; `igColorEdit3` is already compiled into the vendored
  `libcimgui.so`, and `docs/EDITOR_DESIGN.md`'s binding scope (D-017) names `ColorEdit` alongside
  `DragFloat` as in-scope, so the picker is the sanctioned control for a colour. Bounded `dragFloat`
  ranges are what enforce FR-009.
- **Alternatives considered**: **a "Lighting" section in the Outliner** — where the spec first put
  it, but the Outliner is a tree of entities, not an editing surface, and the user's answer put the
  controls in the Inspector; **a separate zone selection flag** (`EditorState.ZoneSelected`) —
  faithful to "selecting a zone is an act", but it is a second source of truth about what is being
  edited that must be kept in step with `Selection` in ~6 places (the Outliner, `Spawning`, the
  viewport pick, the delete paths), and its only visible effect is that the lighting is hidden until
  the author clicks a zone row — rejected (Minimalism I); **a `SelectionTarget` DU** (`Entity of
  EntityRef | Zone`) — the "correct" model, but it rewrites the core single-selection model (and
  `SelectionTests`) for a lighting feature; **a new panel** — more surface than four controls earn;
  **`igSliderFloat` for the three scalars** (the reference uses slider bars) — the port's sanctioned
  list names `DragFloat`, not `Slider`, and a range-clamped drag reads the same; **three
  `dragFloat`s for R/G/B instead of a picker** — no new binding, but a worse colour control and a
  departure from the reference, for the sake of one P/Invoke.

## Decision 7 — An undoable change, one step per committed control

- **Decision**: a light change goes through the `SetLight` command (D8) on commit — when a control
  is released or left (`isItemDeactivatedAfterEdit`, the rule the Inspector already uses for its
  trigger fields and text fields). Consecutive changes do **not** coalesce, so two committed changes
  are two undo steps.
- **Rationale**: the spec's answer to Q2 is that an authored change must be reversible (FR-006,
  SC-007), and the undo stack is per zone (`GameplayState.UndoByZone`), so a light undo applies in
  the zone it was made in. Committing on release rather than per frame is what keeps one drag from
  becoming a hundred steps; not coalescing is a deliberate difference from transforms (which
  coalesce in `UndoStack.push`), because two deliberate light changes are two deliberate changes.
  This is a deliberate departure from `odin-game`, whose sliders write the live zone directly.
- **Alternatives considered**: **a direct live write (no undo)** — the reference's behaviour, and
  this plan's first draft, but rejected by Q2; **one step per frame while dragging** — floods the
  stack and makes Ctrl+Z useless; **coalescing consecutive light changes** — offered as Q2 option C
  and declined, and it would also have to fight `UndoStack.push`'s transform-only merge.

## Decision 8 — The record carries the light; instantiate restores it in one step

- **Decision**: `ZoneTemplate` gains a `Light` field. `Zone.captureTemplate` records the zone's
  light, and `Spawning.instantiateZone` sets the active zone's light to the recorded one by
  including a `Command.SetLight (previous, template.Light)` in the `Batch` it already builds.
- **Rationale**: FR-007, and the faithful port of the reference's `Zone_Settings` /
  `zone_settings_capture` / `zone_settings_apply` (`src/zone_registry.odin` `.odin:78`, `.odin:109`)
  — a registered place keeps the light it was authored under. The instantiate step is already one
  `Batch` and one undo entry (spec 035 FR-005); because the undo stack reverses *commands*, the
  light change must itself be a command in that batch, or undoing an instantiation would leave the
  light the instantiation imposed. `SetLight` therefore has **two** callers — the Inspector's
  committed edits (D7) and this batch — which is one small command instead of a second mechanism.
- **Alternatives considered**: **a live write during instantiate** — the light would survive an
  undo of the instantiation while the environment and props reverted, leaving the zone in a state
  it was never authored in; **extending `SetEnvironment` to carry the light** — changes an existing
  command's shape and every one of its call sites (`AssetBrowser`, `Spawning`, `ZoneTests`) for a
  second field, where an additive variant touches only the exhaustive `apply`/`undo` matches;
  **not recording the light at all** — drops US3 and FR-007.

## Decision 9 — The colour picker reuses a buffer; the frame allocates nothing new

- **Decision**: the picker draws over a reused `float32` triple held on `EditorState`, seeded from
  the zone's colour while the control is not active (the rule the Inspector already uses for its
  text buffers).
- **Rationale**: `igColorEdit3` takes `float col[3]`, which F# marshals from a `float32[]`; a fresh
  array per frame would be a per-frame allocation (FR-013) and the codebase keeps transient editor
  state on `EditorState` rather than module singletons (`state/EditorState.fs`). A 3-element array
  is the whole cost.
- **Alternatives considered**: **a local array per frame** — allocates every frame the panel is
  open (FR-013); **a module-level mutable** — against the stated convention that transient editor
  state lives on `EditorState`.

## Decision 10 — Test the pure derivation; the rest is the walkthrough

- **Decision**: a new `test/LightTests.fs` covers `Texture.toLight` and the defaults; the template
  carry and the light command are covered in `test/ZoneTests.fs` (`captureTemplate` records the
  light; `Command.apply`/`Command.undo` of `SetLight` set and restore it) and `test/SpawningTests.fs`
  (instantiate restores the light; undoing the step restores the previous). The rendered result, the
  Inspector's committed edit and the picker are the quickstart walkthrough.
- **Rationale**: tests cannot open a GPU device in this port, so only pure logic is asserted
  headlessly (`AGENTS.md`, and every prior rendering spec). The one thing a subtle bug would break
  invisibly is the pitch/yaw → direction mapping (a transposed axis would tilt every scene), which
  is exactly what is pure and testable; the recorded light and the `SetLight` reversal are pure data
  and are asserted through the existing capture/instantiate harness and `ZoneTests`'s command cases.
- **Alternatives considered**: **golden images** (no GPU in the host); **test nothing** (leaves the
  derivation — the part most likely to be wrong — uncovered).
