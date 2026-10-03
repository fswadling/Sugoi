# Contract: Exposit Cue Op

The surfaces this feature adds or changes, and what explicitly does not change.

## `world/Cue.fs` — CHANGED

```fsharp
type Cue =
    // ... existing cases ...
    | Exposit of text: string * kind: Dialogue.Kind
```

**Contract**: the cue is still the run, and `Exposit` is still an ordinary case of it with no `…State` form. The op
now carries the **closed** presentation union (`Dialogue.Kind`, hence `Roster.Character` and `Emotion`), so the module
names `world/Dialogue.fs` — a pure module compiled earlier — and still names no zone, screen, asset or device. A
portrait line always carries a speaker and an expression; a plain or thought line carries neither; none of that is a
runtime check (FR-001, FR-002, FR-004).

## `world/CueEngine.fs` — CHANGED

```fsharp
let private exposit (state: byref<GameplayState.GameplayState>) (text: string) (kind: Dialogue.Kind) : unit =
    state.Dialogue.Shown <- true
    state.Dialogue.Kind <- kind
    state.Dialogue.Text <- text

| Exposit (text, kind) ->
    exposit &state text kind
    ValueSome Fin
```

**Contract**: `advance` is unchanged in kind — one cue in, the cue as it now stands out (or `ValueNone` while a timed op
is in flight). The op writes the screen's `Dialogue` struct where the walk reaches it (shown, text, kind), replacing
any line already shown, and completes in the same step. The engine resolves nothing: no cast lookup, no asset, no
portrait — that is `world/DialogueRender.fs`'s, at draw time, so an unresolved portrait neither fails nor blocks the run
(FR-003, FR-005; research D2/D3).

## `world/DialogueRender.fs` — UNCHANGED

It already resolves `(character, emotion)` through the cast binding and draws all three kinds (`specs/038`); this
feature only makes it reachable from a cue.

## `editor/CueTree.fs` — CHANGED

Adds a presentation chooser (Dialogue / Thought / Portrait) for an `Exposit`; for a portrait line, a speaker chooser
over `Roster.characters` and an expression chooser over `Dialogue.emotions`. Updates `label`, `defaultOfKind`, `textOf`
and `withText` for the new field and adds the public `withExpositKind` write.

```fsharp
/// The node with its presentation replaced — the write the choosers commit. Only an exposit carries one.
let withExpositKind (kind: Dialogue.Kind) (cue: Cue) : Cue =
    match cue with
    | Exposit (text, _) -> Exposit(text, kind)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no presentation" (label cue))
```

**Contract**: the control still reads no game state for the choosers — `Roster.characters` and `Dialogue.emotions` are
closed lists, so a chooser over them needs no zone and no registry (unlike a zone/waypoint name; `specs/047` FR-006).
It now also names `world/Dialogue.fs` (a pure module). Each accepted change is one `withAt` rebuild committed as one
`SetCue` step (FR-007, FR-008).

## `world/Dialogue.fs` — CHANGED (comment only)

The `Emotion` note that said it gains `[<SymbolicExpansion>]` "with the cue op that first authors a line" is corrected:
the trigger is persistence (phase 15), because nothing serializes a cue yet and `Advent` — which cues already carry —
has no such attribute (research D6).

## `test/CueTests.fs`, `test/CuePanelTests.fs` — CHANGED

**Contract**: `CueTests` gains the three presentation kinds' writes, the instant step, and the replace case, asserted
against a `GameplayState`'s dialogue (FR-006). `CuePanelTests` gains the label, the default, `textOf`/`withText`
preserving the kind, and `withExpositKind`. Neither opens a device.

## Explicitly unchanged

- **`world/DialogueRender.fs`** — the resolution chain and the three draws are already correct (`specs/038`).
- **`editor/DialoguePanel.fs`** — the manual authoring surface still writes `Dialogue` directly; the cue op is the
  scripted one and neither owns the other.
- **`state/GameplayState.fs`** — `Dialogue` already exists and is mutable; no new field.
- **`state/Command.fs`, `state/History.fs`, `editor/CueEditor.fs`** — a cue edit is still one `SetCue` step; the panel's
  text field is unchanged.
- **`cast/Roster.fs`** — the closed cast the chooser walks; this feature consumes it.
- **`fsharp-game.fsproj`** — `world/Dialogue.fs` is already compiled before `world/Cue.fs`; no new entry.
- **The other op families** — scenarios, fades, actor movement, camera toggles, animation blends stay deferred.
