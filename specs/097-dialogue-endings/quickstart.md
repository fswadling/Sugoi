# Quickstart: Dialogue Endings

## Prerequisites

- .NET 10 SDK and the repo's native libraries (run `native/build.sh` if `libcimgui.so` is missing).
- From the repository root: `dotnet build`.

## Automated coverage (headless)

Run the changed suites first:

```sh
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~DialogueTests"
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~CueTests"
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~CuePanelTests"
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~ProjectTests"
```

Then the full suite:

```sh
dotnet build
dotnet test test/Sugoi.Tests.fsproj
```

What to expect:

1. **Ending rules** — `updateDialogue` ends a line for `ByPlayer`, `ByTimeout`, or the first of the
   two; it ignores a press when `ByPlayer` is absent; it ignores a player press while a text field has
   focus; it does nothing for the empty set.
2. **Waiting** — an `ExpositWait` line keeps the cue at `ExpositWaitState` while `Shown` is true and
   advances when its step observes the clear (the next tick if a parallel branch advanced after it
   produced the clear). A non-waiting `Exposit`/`ExpositWith` writes the line and the cue continues.
3. **Cue-managed waiting** — an `ExpositWait` with the empty set waits until a parallel/other cue closes
   or replaces-and-then-closes the line; it does not warn or block in the editor.
4. **Close** — `CloseExposition` clears the line and costs no time; a following op runs in the same tick.
5. **Timer independence** — a non-waiting timed line clears even after the cue is `Fin`.
6. **Zero-byte idle** — an idle `ExpositWaitState` tick and an `updateDialogue` call (shown or not)
   allocate 0 bytes (the existing `GC.GetAllocatedBytesForCurrentThread` pattern).
7. **Persistence** — the new cue cases round-trip through `Project.encode`/`Project.decode`, including
   `Set<Ending>` and `float voption`; the old two-argument `Exposit` still decodes and is read as the
   empty set (cue-managed).

## Manual walkthrough

1. Run `dotnet run`, open the editor and press `Space`/the play toggle to enter play mode.
2. In the **Dialogue** panel, set a line, tick **Shown**, and confirm:
   - with neither condition enabled (the empty set), neither `Space` nor time closes it; an explicit
     close or replacement still works, because cue closing is unconditional;
   - with **Player can close** only, `Space` closes it;
   - with **Closes after** only, it starts at 3.0 seconds, closes itself, and `Space` does nothing;
   - with both enabled, the first of `Space` or the duration closes it.
3. In the **Cue** panel, build (the ending set in the manifest is `[ByPlayer]`, and the timeout is
   `[Some 3.5]`):
   - `Sequence[Exposit "one" [Dialogue]; Wait 2; CloseExposition]` — `Exposit` writes the empty set; the
     cue runs on with the line up, then closes it; `Space` is ignored.
   - `Sequence[ExpositWait "one" [Dialogue] [ByPlayer] None; ExpositWait "two" [Dialogue] [ByPlayer] None]`
     — each line waits for `Space`, and the cue advances in the same frame.
   - `Sequence[ExpositWait "one" [Dialogue] [ByPlayer ByTimeout] [Some 3.5]]` — `Space` skips early,
     otherwise the line clears after 3.5 seconds.
   - `Sequence[ExpositWith "ambient" [Dialogue] [ByTimeout] [Some 2]]; Wait 5` — the line clears itself
     while the cue continues.
   - `Sequence[ExpositWith "both" [Dialogue] [ByPlayer ByTimeout] [Some 2]]` — the player may close it
     early; otherwise the cue continues and the line clears on its timer.
4. Confirm the cue row labels read `Exposit: … [cue]`, `[player]`, `[3.5s]` or `[player or 3.5s]`, and
   that a running waiting op is read-only. For the cue-managed waiting case, put `ExpositWait` and a
   `Parallel` close in one cue: the wait resumes on the tick after the close when the close branch is
   advanced after it, which is still within one play frame.
5. Select a trigger and use its inline cue editor; confirm the same display kinds and ending controls.
6. Play the intro scene in `project.manifest`: the three lines use the empty set (no `Space` close), and
   the new `CloseExposition` clears the last line when the scene reaches it.

## Persistence check

```sh
# With the editor open, change a cue and let the project write.
# Then close and relaunch; the cue decodes with its endings intact.
dotnet run
```

Expected: no project-load advisory; the authored `Exposit`, `ExpositWith`, `ExpositWait` and
`CloseExposition` nodes survive. A pre-feature project file still decodes because the two-argument
`Exposit` is unchanged and reads as the empty set.
