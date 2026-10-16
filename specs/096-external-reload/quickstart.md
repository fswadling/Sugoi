# Quickstart: Live Reload of the Project File

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`) for the editor.
- Run the game from the repository root, so `project.manifest` resolves there (the `specs/095` file).
- A running game with at least one registered scenario, so a reload has something to change.

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with **no warnings** and the suite green, with no device opened. `ProjectTests` covers the
reload over a temp directory:

- **external change installed**: rewrite the file, advance the poll past the interval, and assert the reloaded library
  equals the file's.
- **own write ignored**: after the game writes the file, the next poll reports no change (the text matches).
- **identical rewrite ignored**: rewriting the same bytes reports no change.
- **damaged / partial**: a malformed external write is reported, installs nothing, leaves the file alone, and a
  subsequent valid write is still picked up.
- **deleted**: removing the file installs nothing and leaves the library as it was.
- **interval gate**: a second immediate poll (before the interval has elapsed) does not re-read.

## Manual

1. **Live reload picks up an external edit**: while editing, register a scenario, then in another editor change that
   scenario's name or a cue text inside `project.manifest` and save. Within about a quarter of a second the
   **Scenario** panel reflects the change — no restart.
2. **Quiet during play**: press the play/edit toggle to play, then make an external edit — nothing is reloaded while
   playing (play performs no file I/O); stop playing and the change lands on the first editing frame.
3. **Atomic save is picked up too**: make the same edit in an editor that saves via a temp file + rename (many do);
   the change still lands, because the poll reads content, not events.
4. **The game's own saves don't churn**: edit a scene in the game and watch that nothing is reloaded back from the
   file (the game's own write is recognised).
5. **A partial write is survivable**: save a large hand-edit in one shot while the game runs; if a poll catches a
   half-written file it is reported and the running library is untouched, and the finished file is picked up on the
   next interval.
6. **Deletion is harmless**: delete `project.manifest` — the running library stays exactly as it is, and nothing is
   cleared.
7. **Suspend it**: turn **Live reload** off in the **Miscellaneous** panel, edit the file, and confirm nothing is
   reloaded; turn it back on and the pending change lands. Automatic saving keeps working with the toggle either way.

## References

- Contract (the poll and its outcomes): [contracts/live-reload.md](./contracts/live-reload.md)
- Data model (the store, the helpers, the toggle): [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
- The file this watches: [`specs/095-scenario-persistence`](../095-scenario-persistence/contracts/project-file.md)
