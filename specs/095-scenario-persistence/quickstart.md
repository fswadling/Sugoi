# Quickstart: Automatic Persistence of the Scenario Library

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`) for the editor.
- Run the game from the repository root (the working directory the project file resolves against, beside `assets/`).
- No asset setup is needed to exercise persistence: a scenario name is a string, and a scenario whose zones
  reference no assets still round-trips.

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with **no warnings** and the suite green, with no device opened. `ProjectTests` covers:

- **round-trip**: a library built through the real capture path `decode (encode r) = r`, equal scenario for scenario,
  including every zone's staging and entities.
- **the meaningful shapes**: a named-but-unregistered scenario, a chain-ending waypoint (`Next = None`), and an empty
  cue each survive the round trip.
- **absent file**: `loadFrom` a path that does not exist returns the empty library, reports nothing, and writes
  nothing.
- **damaged file**: `loadFrom` a malformed file, and one whose `Version` is unknown, reports via `Diagnostics`,
  returns the empty library, and leaves the file's bytes unchanged.
- **atomic write**: after `saveTo`, the `.tmp` file is gone and the target is the complete document.
- **change-driven**: `persist` writes nothing when handed the same library instance twice, and writes when handed a
  new one (an edit, and an undo back to a prior library, are each a new instance).

## Manual

1. **Author and quit**: open the game, in the **Scenario** panel add a scenario (`Intro`), build a small scene, and
   click **Register** on the row. Quit the game. Confirm `project.manifest` now exists at the repo root and reads as
   s-syntax naming `Intro`.
2. **Reopen**: start the game again. The **Scenario** panel lists `Intro` with its contents, in the same order, with
   **no** save or load action taken.
3. **Change and reopen**: delete `Intro`, then `Ctrl+Z` to restore it; quit and reopen — `Intro` is still there (an
   undo is persisted, not lost). Add a second name and note both persist in the order you added them.
4. **Refused change writes nothing**: try to add a blank name or a duplicate — the file's timestamp does not change.
5. **Missing file**: delete `project.manifest` and start the game — it opens with an empty library and no error;
   nothing is written until you change something.
6. **Damaged file**: truncate `project.manifest` (or change `Version` to `99`) and start the game — the error overlay
   reports it, the library is empty, and the file is still on disk, unchanged, so you can inspect or repair it.
7. **Hand-edit**: open `project.manifest`, add a `;` comment line, and relaunch — the file still loads (comments are
   permitted).

## References

- Contract (the file format and save/load behaviour): [contracts/project-file.md](./contracts/project-file.md)
- Data model (the record tree and the module surface): [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
