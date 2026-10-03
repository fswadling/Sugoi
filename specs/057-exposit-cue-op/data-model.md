# Data Model: Exposit Cue Op

## `world/Cue.fs` — changed

```fsharp
// the existing case, widened by one field; `Dialogue.Kind` is the closed presentation union
| Exposit of text: string * kind: Dialogue.Kind
```

- **One op, three presentations.** `Dialogue.Kind` is
  `PortraitDialogue of Roster.Character * Emotion | Dialogue | Thought` (`world/Dialogue.fs`), so a portrait line
  always carries a speaker and an expression and a plain or thought line carries neither (research D1).
- **No `…State` sibling** — the op holds nothing in flight (`specs/046` FR-007; research D3).
- **`Cue.fs` widens its dependency note by one pure module** (`Dialogue`, which names `Roster`); it still names no
  zone, screen, asset or device.

## `world/CueEngine.fs` — changed

```fsharp
let private exposit (state: byref<GameplayState.GameplayState>) (text: string) (kind: Dialogue.Kind) : unit =
    state.Dialogue.Shown <- true
    state.Dialogue.Kind <- kind
    state.Dialogue.Text <- text

// in `advance`
| Exposit (text, kind) ->
    exposit &state text kind
    ValueSome Fin // no progress of its own: performed here and gone
```

- **The write is field-wise on the struct `Dialogue`** — no record copy, no allocation beyond the op value the walk
  already carries.
- **The engine still needs no `AssetManager`** and resolves no portrait; that is the view's (research D2). `Exposit`
  completes in the same step, so it never locks an ancestor in the panel.

## `editor/CueTree.fs` — changed

| Surface | Change |
|---|---|
| `label` | `Exposit (text, _) -> "Exposit: " + text` (unchanged rendering; research D4) |
| `kindName` | `Exposit _ -> "Exposit"` (unchanged) |
| `defaultOfKind` | `"Exposit" -> Exposit("", Dialogue.Kind.Dialogue)` — a plain line with empty text, the previous default |
| `textOf` | `Exposit (text, _) -> ValueSome text` |
| `withText` | `Exposit (_, kind) -> Exposit(text, kind)` — writing the text preserves the presentation |
| `withExpositKind` (new, public) | `withExpositKind (kind: Dialogue.Kind) (cue: Cue) : Cue` — `Exposit (text, _) -> Exposit(text, kind)`; raises for a non-`Exposit`, as `withText`/`withAdvent` do |
| `drawEditor` | a presentation chooser (Dialogue / Thought / Portrait) for an `Exposit`; for `PortraitDialogue`, a speaker chooser over `Roster.characters` and an expression chooser over `Dialogue.emotions`. Re-choosing the current kind does not clobber the pair; choosing Portrait when not already portrait makes `PortraitDialogue(List.head Roster.characters, Neutral)`. Each accepted change is one `withAt`/commit |
| `executing` | `Exposit _` is not executing (unchanged wildcard) |
| `children` | `Exposit _` has none (unchanged wildcard) |

## Presentation chooser mapping (panel)

| Chooser entry | Resulting kind |
|---|---|
| Dialogue | `Kind.Dialogue` |
| Thought | `Kind.Thought` |
| Portrait | `Kind.PortraitDialogue(current character or `List.head Roster.characters`, current emotion or `Neutral`)` |
| Speaker sub-chooser (portrait only) | `Kind.PortraitDialogue(chosen, emotion)` |
| Expression sub-chooser (portrait only) | `Kind.PortraitDialogue(character, chosen)` |

## Validation rules

| Rule | Where it is enforced |
|---|---|
| A speaker cannot be unknown | the type — `Roster.Character` is a closed union |
| An expression cannot be unknown | the type — `Dialogue.Emotion` is a closed union |
| A plain/thought line has no speaker or expression | the type — only `PortraitDialogue` carries them |
| An unresolvable portrait is not an error | `DialogueRender` returns a `Missed` value; the op still writes the line (research D2) |
| `Exposit` is never read-only in the panel | it has no `…State` form, so `executing` is `false` |

## Deliberately not modelled

- **A clear/hide line op** — the reference has none; the player clears the line, and a cue only writes it.
- **Waiting for the line to be dismissed** — a new mechanism the reference does not have; scripts use `Wait`.
- **Resolving or storing a portrait on the op or the dialogue** — the view resolves per frame (`specs/038`).
- **`[<SymbolicExpansion>]` on `Emotion`** — deferred to persistence (phase 15; research D6).
