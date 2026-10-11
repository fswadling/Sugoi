# Quickstart: Retire the Cast

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).
- At least one discovered actor asset whose manifest declares a `[Name …]` (the speaker string) and a
  `[Portraits …]` block (so a portrait line resolves).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with **no warnings** and the suite green, with no device opened.

- **speaker by name**: `DialogueTests` shows a portrait line whose speaker is a string and asserts the asset named
  for it supplies the portrait for the expression — with no binding.
- **the name index**: `AssetNameIndexTests` asserts a name resolves to its asset, exactly, with the lowest `Id`
  winning a tie, and that a missing name resolves to none.
- **no cast in the game**: no `Casting` module, `SharedState.Cast` or Cast panel; `world/Dialogue*.fs` name no
  `Roster`/`Casting` value (grep).
- **the engine is untouched**: `test/RosterTests.fs` and the narrative suites pass **unmodified** — the roster now
  lives at `narrative/Roster.fs` (SC-003).
- **allocation-free draw**: the retained `tryPortrait allocates nothing per call` guard passes.
- **in-flight actor op resolves once**: `CueTests` begins a move and asserts it keeps acting on the same actor (a
  same-named newcomer does not steal it), ends when the actor is removed, and — via an allocation guard on
  `stepMove` — adds **0 bytes per tick** (SC-008, SC-009).

## Manual

1. **A portrait line resolves with no setup**: with an actor asset named for a character and declaring portraits,
   set a portrait line to that name — the portrait and name plate appear, with no binding assigned anywhere.
2. **The speaker chooser is the asset list**: open the Dialogue panel's (or a cue `Exposit` op's) speaker chooser;
   it offers the discovered actor names and no roster member.
3. **A miss is explained**: name a speaker with no such asset; the line is left undrawn and the panel says why, in
   terms of the asset (not a Cast panel).
4. **The cast is gone**: the editor has no "Cast" window; the Asset Browser still places an actor; the roster no
   longer appears anywhere in the game.
5. **A running move keeps its actor**: start a `MoveActor` and, mid-flight, spawn a second actor of the same name —
   the move stays on the first. Remove the moving actor and the move ends at once (the slot is dead).

## References

- Contract: [contracts/portraits-by-name.md](./contracts/portraits-by-name.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
