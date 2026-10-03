# Quickstart: Exposit Cue Op

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- Nothing else: **no window, no GPU, no device**. The dialogue is plain data on `GameplayState`, which the suite builds
  the way `test/CueTests.fs` already does.
- `dotnet build` clean (warnings are errors in this repo).

## Automated coverage (headless)

`dotnet test` runs the extended suites. What it asserts, by story:

| Story / requirement | Assertion |
|---|---|
| 1 — presents a line | `Exposit(text, Kind.Dialogue)` writes `Shown`, `Text` and `Kind.Dialogue`; `…, Kind.Thought)` writes `Kind.Thought`; `…, Kind.PortraitDialogue(Akane, Happy))` writes that portrait kind with its speaker and expression; the op is at the next op in the same step; a second `Exposit` replaces the first. |
| 2 — an unresolved portrait is not fatal | with no cast binding, a portrait `Exposit` still sets the dialogue to the authored kind and the cue advances without raising; `DialogueRender.missReason` reports the miss in the data-only cases. |
| 3 — the panel authors it | `label` is `"Exposit: …"`; `defaultOfKind "Exposit"` is a plain line with empty text; `textOf` reports the text; `withText` replaces the text and preserves the kind; `withExpositKind` replaces the kind and preserves the text and raises for a non-`Exposit`. |
| FR-004/SC-003 — instant | a cue of `Exposit`s is `Fin` within one step. |
| FR-006/SC-006 — build/test | `dotnet build` clean, `dotnet test` green, no device. |

## Interactive walkthrough

The engine loads with the game (`CueEngine.fs` writes `GameplayState`), so the fastest way to *see* the op is a scratch
test in `test/CueTests.fs` using the same headless fixture:

```fsharp
let mutable state = withCue (seqOf [ Exposit("You're late.", Dialogue.Kind.PortraitDialogue(Akane, Dialogue.Emotion.Angry)); Wait 2.0; Exposit("…", Dialogue.Kind.Thought) ])

CueEngine.tick shared &state 0.0
Assert.True state.Dialogue.Shown
Assert.Equal(Dialogue.Kind.PortraitDialogue(Akane, Dialogue.Emotion.Angry), state.Dialogue.Kind)
Assert.Equal("You're late.", state.Dialogue.Text)

CueEngine.tick shared &state 2.0
Assert.Equal(Dialogue.Kind.Thought, state.Dialogue.Kind)   // the wait ended, the thought is shown
Assert.Equal("…", state.Dialogue.Text)
```

Then the properties that are about *shape*:

- **Instant** — `seqOf [ Exposit("a", Kind.Dialogue); Exposit("b", Kind.Dialogue) ]` is `Fin` after one tick, and only
  `"b"` is shown.
- **Presentation round-trips** — `withExpositKind (Kind.PortraitDialogue(Yuki, Sad)) (Exposit("hi", Kind.Dialogue))`
  describes as an `Exposit` carrying that portrait kind, with the text unchanged.
- **Non-fatal miss** — with an empty cast binding, a portrait `Exposit` still shows the authored kind; the view leaves
  it undrawn and the Dialogue panel reports why.

## Expected outcome

- `dotnet test` is green, including the three presentation kinds and the instant/replace cases.
- The scratch tick shows a portrait line, then a thought, as the cue runs.
- In the editor, selecting an exposit row shows a presentation chooser; choosing Portrait reveals speaker and
  expression choosers, both closed lists, and changing either is one undo step. A portrait line whose speaker has no
  bound actor asset is written by the run but not drawn, and the Dialogue panel says so.
