# Quickstart: Trigger Cue

Validation that the feature works end to end. The runtime half runs headlessly; the authoring half is exercised in the
running editor.

## Prerequisites

- .NET 10 SDK; the native ImGui/SDL libraries already vendored under `native/`.
- Build clean first: `dotnet build`

## Automated (headless)

```sh
dotnet test
```

The suites that prove this feature:

- **`test/SystemsTests.fs`** — the entry fire and `forkTriggerCue`:
  - a trigger whose cue is `Wait 1` forks it into a `Fin` screen cue when the player enters (the cue becomes the
    trigger's cue), and the trigger reports the player inside;
  - standing inside across further updates does **not** fork again; the cue is unchanged;
  - leaving and re-entering forks again;
  - a trigger whose cue is `Fin`, a disabled trigger, and a zone with no live player fork nothing;
  - an interactable trigger is **not** forked on entry;
  - `forkTriggerCue zone (ValueSome ref) &cue` forks the tracked trigger's cue; `ValueNone`, a stale ref and a `Fin`
    cue each leave `cue` unchanged.
- **`test/CueTests.fs`** — `forkInto` still runs an arriving cue beside the running one and disturbs nothing (the
  relocated function; same assertions).
- **`test/CuePanelTests.fs`** — the inline editor's write path and reset:
  - committing an edit to a trigger's cue yields one `TriggerSet` step whose `After.Cue` is the edited cue, and undo
    restores `Before.Cue`;
  - the trigger inline editor's transient fields differ from the Cue panel's (distinct selection and buffer), and a
    selection change resets the trigger fields.

`dotnet test` must pass and `dotnet build` be clean (SC-006, SC-007).

## Manual (editor)

1. `dotnet run`
2. In the Outliner's **Triggers** section, add a trigger and select it.
3. In the Inspector, under the trigger's shape/prompt fields, the **Cue** section shows the trigger's cue (`Fin`).
   Select its row, re-kind it to `Sequence` (or add a child), and build e.g. `Sequence[Wait 2; Exposit "hello"]`.
   Each accepted edit is one step: press Ctrl+Z and Ctrl+Y to confirm it undoes and redoes.
4. Designate an actor as the player (select it → **Set as Player**), then move the trigger around it so the player
   starts **outside** the volume.
5. Press **Backtick** to play. Walk the player into the trigger. After the wait, `"hello"` is shown — the trigger's cue
   ran beside the (empty) screen cue.
6. Walk out and back in: the cue fires again.
7. Undo the trigger's cue edit in the Inspector, check the tree returns to `Fin`; Ctrl+Y restores it.
8. Mark the trigger **Interactable** with a prompt, play, walk in, and press **E** — the cue fires on the press, not on
   entry, and the prompt is shown while inside.

### What to look for

- One entry, one fire (no repeat while standing still).
- The screen's running cue is not disturbed by a fire or by editing the authored cue.
- A trigger's cue edit survives entering and leaving play (it is a zone step, not a pruned `SetCue`).
- The inline editor and the Cue panel do not share a selected row or a text field.

## Expected outcomes

- SC-001/SC-002/SC-003/SC-004 — the headless fire cases above.
- SC-005 — authoring a trigger's cue is one undo step per edit and redo restores it.
- SC-006 — `dotnet build` clean, `dotnet test` passes, no device opened by the new tests.
