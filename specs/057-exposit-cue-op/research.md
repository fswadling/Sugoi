# Phase 0 Research: Exposit Cue Op

The op and the display both already exist; this feature is the seam between them. Most of the decisions are about
carrying the *closed* presentation and leaving resolution where `specs/038` put it.

## Decision 1 — The op carries the closed `Dialogue.Kind`, not reference-style fields

- **Decision**: `Exposit` becomes `Exposit of text: string * kind: Dialogue.Kind`, where
  `Dialogue.Kind = PortraitDialogue of Roster.Character * Emotion | Dialogue | Thought`. `world/Cue.fs` names
  `world/Dialogue.fs`.
- **Rationale**: the reference (`Cue_Op_Exposit`) carries `text`, `speaker_name`, `kind` and `emotion` as separate
  string/enum fields. The port already has the closed presentation union that folds speaker and emotion into the kind
  exactly where they apply (`specs/038` FR-002: "a portrait line always carries both and a speaker-less kind never
  carries an expression"). Reusing it means a cue cannot author a portrait line with no speaker or a plain line with a
  stray expression — those are unrepresentable rather than validated. `world/Dialogue.fs` is pure (it names only
  `Roster`, itself pure) and is compiled before `world/Cue.fs`, so the vocabulary still loads on its own; it only
  widens `Cue.fs`'s note by one pure module, as `specs/056` already did for `Advent`.
- **Alternatives considered**: **separate ops per kind** (`ExpositPortrait of Character * Emotion * string`, …) —
  rejected: op sprawl where one op with a kind suffices, and it diverges from the reference's single op. **The
  reference's separate `kind` + `speaker_name` + `emotion` fields** — rejected: stringly-typed, and it admits states
  the display union exists to forbid. **Strings for speaker/emotion** — rejected by `specs/038`/`specs/055`'s whole
  rationale.

## Decision 2 — Resolution stays in the view, at draw time

- **Decision**: the op carries the speaker and expression and looks up nothing; `world/DialogueRender.tryPortrait`
  resolves `(character, emotion)` → cast binding → actor asset → portrait when the frame is drawn. An unresolved
  portrait is the expected miss `specs/038` models.
- **Rationale**: the reference resolves at op time only because its `Exposition_State` stores a `Texture2D` and a path;
  this port's exposition state is deliberately pure data and the view already owns the whole resolution chain
  (`specs/038` FR-005). Resolving in the engine would make the cue write a GPU resource and would need the
  `AssetManager` in the walk — a much larger, hotter dependency for no gain. Keeping it in the view also preserves the
  non-fatal behaviour: the line is written, the run continues, and the panel explains the absence.
- **Alternatives considered**: **resolving in the engine and writing a texture/path** — rejected: puts assets and a
  device on the cue's path and duplicates the view's resolution. **Failing loudly on an unbound speaker** — rejected:
  it contradicts `specs/038` FR-006 ("expected while authoring … a value rather than an exception") and would make a
  cue un-runnable until every portrait was bound.

## Decision 3 — No `…State` case; `Exposit` costs no time

- **Decision**: `Exposit` keeps the no-progress shape — the engine writes the dialogue and reports `ValueSome Fin`;
  there is no `ExpositState`.
- **Rationale**: the reference's `Cue_ExpositState` is an **empty** struct, i.e. the same no-progress shape; it does not
  wait for the line to be dismissed. `specs/046` FR-007 already places `Exposit` among the ops with no progress. A cue
  that shows a line and immediately shows another leaves only the last on screen (the reference's own behaviour);
  scripts space lines with `Wait`.
- **Alternatives considered**: **an `ExpositState` that blocks until the player dismisses the line** — rejected: it is
  a new mechanism the reference does not have, and the dismissal signal does not exist. **A `…State` begun-marker** —
  rejected: `Exposit` is instant, so there is nothing to mark begun.

## Decision 4 — The row label stays `"Exposit: " + text`

- **Decision**: `CueTree.label` renders `Exposit (text, _)` as `"Exposit: " + text`, unchanged; the panel's editor shows
  the kind below the tree.
- **Rationale**: the existing row and `describe` assertions (`"Exposit: hello"`) read well and are stable; the op's
  *name* is what the row is for, and the three kinds are still the same op. Adding the kind to the label would churn
  every label assertion for marginal value while the author is already one click from the kind chooser.
- **Alternatives considered**: **`"Exposit (Akane/Happy): …"`** — deferred, not rejected: if the tree later needs the
  kind at a glance, the label is the place, and it is one function.

## Decision 5 — The choosers live in the control, over closed lists

- **Decision**: `editor/CueTree.fs` gains a presentation chooser (Dialogue / Thought / Portrait) and, for a portrait
  line, a speaker chooser over `Roster.characters` and an expression chooser over `Dialogue.emotions`, all committed
  through a public `withExpositKind` write. The line's text keeps using the existing field.
- **Rationale**: a character and an emotion are closed lists, so the control can offer them without reading anything —
  no zone, no registry, no asset — exactly as it offers `Advent.all` (`specs/056` D5). Only the waypoint chooser needs
  the panel, because it needs the active zone (`specs/047` FR-006), and this does not. `withExpositKind` is public
  (where `withAdvent` was private) so the chooser's write is asserted headlessly, which `specs/047` FR-007 asks of the
  control's pure half.
- **Alternatives considered**: **the panel supplies the choosers** — rejected: the control needs no game state for a
  closed union. **A free-text speaker/expression field** — rejected by `specs/038`/`specs/055`.

## Decision 6 — `[<SymbolicExpansion>]` on `Emotion` is deferred, and the stale note corrected

- **Decision**: do **not** add `[<SymbolicExpansion>]` to `Dialogue.Emotion` in this feature; correct the note in
  `world/Dialogue.fs` that ties it to "the cue op that first authors a line" so it names **persistence** (roadmap
  phase 15) instead.
- **Rationale**: the attribute only affects `scvalue`/`scstring` serialization of manifests, and nothing serializes a
  cue or a scenario yet (persistence is phase 15). `Advent` — a closed union that cues already carry and that
  `specs/056` added — has no such attribute either, so adding it here would be a just-in-case (Constitution I). The
  comment's stated trigger was written before the cue existed; it is now inaccurate, so it is corrected to the real
  trigger rather than acted on prematurely.
- **Alternatives considered**: **adding the attribute now** — rejected: no serializer reads it yet, and the sibling
  union does not carry it. **Leaving the note as-is** — rejected: it would promise something this feature deliberately
  does not do, and the next author would reasonably expect the attribute to be present.
