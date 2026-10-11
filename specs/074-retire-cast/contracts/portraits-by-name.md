# Contract: Portraits by Name — the Cast Retired

What the dialogue says about a speaker after the cast left the game (`specs/074`), and what the editor offers.

## The vocabulary — `world/Dialogue.fs`

```fsharp
type Kind =
    | PortraitDialogue of speaker: string * Emotion   // the actor's `Name`
    | Dialogue
    | Thought
```

The speaker is a **plain string**. `Dialogue`'s `speaker` is the name plate text and the portrait key. No
`Roster.Character`, no look, no binding.

## Resolution — `world/DialogueRender.fs`

- **The asset**: the actor asset whose declared `Name` equals the speaker string, matched **exactly** (ordinal),
  the lowest `Id` winning a tie. Reached through the asset manager's `ActorsByName` index (built once at scan).
- **The portrait**: `portraitOf emotion asset.Portraits` — the asset's declared texture for the expression; the
  mapping is unchanged from `specs/038` FR-005.
- **A miss is a value**: `NoAsset` (no discovered asset of that name) or `NoPortraits` (the asset declares none)
  leaves the line undrawn and the authoring panel explains it; it never raises.

| Condition | Then |
|-----------|------|
| an asset's `Name` equals the speaker and it declares portraits | draw `portraitOf emotion` of that asset |
| an asset's `Name` equals the speaker but declares no portraits | left undrawn, `NoPortraits` |
| no asset's `Name` equals the speaker | left undrawn, `NoAsset` |

## The editor

- **Cue `Exposit` (portrait)**: the speaker chooser offers the **discovered actor names** (sorted, distinct), with a
  typed fallback; a new portrait defaults to the first discovered name (or `""`).
- **Dialogue panel**: the same — the speaker chooser offers the discovered actor names; the expression chooser is
  unchanged.
- **Placement**: unaffected — the Asset Browser and the cue `AddActor` name an actor asset directly.

## Postconditions

- **No cast in the game**: no `Casting` module, no `Look`/`Slot`/`Bindings`/`Cast`, no `SharedState.Cast`, no Cast
  panel; `world/Dialogue*.fs` name no `Roster`/`Casting` value (spec FR-005…FR-008, SC-002).
- **The roster is the engine's**: `narrative/Roster.fs` and the narrative suites pass unmodified (spec FR-006,
  SC-003).
- **The chooser is the asset manager**: a speaker resolves regardless of placement (spec FR-009, US3).
- **Allocation-free draw**: the resolve is a contains + indexer on the scan-time index (spec FR-011, SC-006).

## Explicitly unchanged / out of scope

- **The dialogue view's geometry, colours and text** (`specs/038`) — identical output.
- **The cue vocabulary** (`specs/073`) — actor ops still name a string; no op added or removed.
- **The narrative engine's behaviour** — it reads the same union, now at `narrative/Roster.fs`.
- **The asset manifests** — the `[Portraits …]` block and the `[Name …]` field are read as before; only their role
  in resolution changed.
