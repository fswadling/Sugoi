# Quickstart: Cue Panel

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- **Rebuild the native ImGui library once**, because the panel's dock line is native (research D6):

  ```sh
  bash native/build.sh        # g++ and the vendored cimgui source; no network, no cmake
  ```

  `libcimgui.so` is build output and git-ignored, so this is a local step, not a commit.
- `dotnet build` clean (warnings are errors in this repo).

## Automated coverage (headless)

`dotnet test` runs the new suite alongside the existing ones. It needs **no window, no GPU and no device**: the
control's readable half is a function of the cue alone. What it asserts:

| Property | Assertion |
|---|---|
| rows (FR-002) | a bare leaf is one row at depth 0; a `Sequence` is a row whose children are indented one deeper; a `Sequence` inside a `Sequence` is indented one deeper again and a `Parallel`'s sides are siblings; the queue behind a running op is listed front-first; `Fin` is a row, not an empty panel |
| the label (FR-004) | every kind's row says what it is — `WaitState (2, deadline)` as `Wait 2`, `MoveCameraState` as `Move camera`, `Zone: classroom` as itself — so the view is what is left to run and never an internal state's name |
| the lock (FR-011, SC-006) | a fresh authored cue locks **0** nodes; a cue part-way through a wait locks the leaf **and the spine to the root**, and the queue behind it locks **0**; a running `Parallel` locks **both** branches; a `Fork`'s inner branch is locked while the remainder behind it is not |
| the addition (FR-011, SC-006) | a running `Sequence` **takes** a child — its queue's tail is ahead of the running op — while the op in flight, a queued leaf, a `Parallel` and a path that names nothing do **not** |
| selection (FR-005) | a path that names no node is reported by `tryAt` and **raises** from `withAt`/`withoutAt` — reading the cue changes nothing |
| navigation (SC-007) | `withAt` replaces exactly the node a path names and leaves every other node the same value; `withoutAt` removes exactly it; a round trip gives back what was put in |
| the history (SC-008) | a push then an undo restores the cue exactly; a redo puts the edit back; a push clears the redo side; an empty history has nothing to take back |
| the panel's write path (SC-007, SC-008) | `CueEditor.commit` writes the screen's cue exactly once and records exactly one step; `undo` restores it exactly, `redo` puts it back, and `canUndo`/`canRedo` track both — driven against a headless `GameplayState`, since this is where the pieces meet |

## Interactive walkthrough

**Set a cue first: nothing sets one yet** (spec 046's assumption, and the spec's own). Until phase 10's triggers
arrive, the panel shows `Fin` on a fresh run, so the walkthrough begins with a **scratch** line in
`screen/GameplayScreen.fs`'s `activate`, deleted once the panel has been seen:

```fsharp
// scratch: a cue to look at, while nothing authors one yet
state.Cue <- Cue.Sequence(Cue.Wait 4.0, Prime.FDeque.ofList [ Cue.Exposit "hello"; Cue.Wait 2.0 ])
```

Then:

1. **Play.** Run the game (`dotnet run`). The cue begins on the first play tick — `Wait 4` starts. Press
   backtick to open the editor: the cue **freezes** (the play chain is what advances play time), which is
   exactly what the panel depends on.
2. **Read it.** The Cue tab is docked in the right column beside the Inspector (Ctrl+Tab cycles to it). It shows:

   ```
   Sequence
     Wait 4
     Exposit: hello
     Wait 2
   ```

   The first child is the op in flight; the two behind it are the queue. Nothing shows the ops already run,
   because they are gone from the value rather than filtered out.
3. **Read what is running, and what it will still take.** Click the `Wait 4` row: the property editor says the op
   is running and cannot be changed — no Kind dropdown, no Seconds field, no Remove. Click the `Sequence` row above
   it: that has no Kind either, but it **does** offer **Add Child**, because an addition lands on the queue, whose
   tail is ahead of the op in flight. Click `Exposit: hello`: it is editable, and so is everything else behind the
   running op.
4. **Edit what is still to run.** Change the queued `Exposit`'s text, or set its Kind to `Wait`; or press
   **Add Child** on the `Sequence` and watch the new op appear at the end of the queue (and be selected). Each
   change is written to `state.Cue` — the screen's own cue — and Undo takes it back; Redo puts it back again.
5. **Play, and return.** Press backtick to play on. Let the cue finish (it ends as `Fin`). Open the editor
   again: the panel shows `Fin` — "nothing left to run", not an error and not an empty panel — and there is
   **nothing to undo**, because the history was dropped the moment Play began.
6. **Two cues in one window** (once phase 10 exists): a trigger's cue drawn in the Inspector takes its own id
   prefix, so the two trees' rows do not collide.

To see the tree shrink, repeat 1–2 with a longer first wait and a shorter play: each return shows the cue as it
now stands, with the ops that ran simply absent.

## Expected outcome

- `dotnet test` is green, including the lock assertions: what the panel shows and what it will let you change
  are decided by a pure function of the cue, asserted with no device.
- In the game: the Cue tab sits beside the Inspector in the right column; opening the editor shows the running cue with the op in
  flight as the first child of its sequence; a running op offers no control and says why while the sequence holding
  it offers its one addition; a queued node is
  editable, and Undo/Redo step that edit back and forward exactly; playing and returning shows what is left, and
  a history that begins again with the editing session.
- The play path is untouched — spec 046's `dotnet test` allocation assertions still pass unchanged, because the
  panel draws only while editing.
