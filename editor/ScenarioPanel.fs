module ScenarioPanel

// The Scenario panel (spec 070; string-keyed since spec 083): the game's scenarios, which the author names, files,
// forks and removes by hand. It is the right-column surface beside the Inspector, because a scenario is not a
// selectable item in the scene.
//
// A scenario is a plain string now (`world/ScenarioRegistry.fs`), so the panel offers a **text field** and an
// **Add** control to name a new one, and a **list** of the current scenarios, each row with **Register**, **Clear**,
// **Fork** and **Delete**. Add is disabled for a blank-after-trim name or one already present; a name is matched
// exactly (ordinal). Register **captures** rather than edits: it copies every present zone and the screen's cue into
// the library. Each add, delete, clear and register is one step on the one undo history (`docs/EDITOR_DESIGN.md`,
// D-011) — the library is not a zone's, so `Command.SetScenario` carries it before and after the edit and
// `state/Undo.fs` writes `GameplayState.Scenarios`. The panel keeps no history of its own; `Ctrl+Z` reaches the
// step (the same class as the Advent panel).

open Command
open ScenarioRegistry

// ── the write path ──

/// Write `next` as one step on the one history, reading the entry's `before` here so a control is a single call.
/// Keeps no history of its own; `Ctrl+Z` reaches the step. Public like `AdventPanel.commit`, so the write path is
/// drivable headlessly.
let commit (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (next: Registry) : unit =
    let before = state.Scenarios
    do Undo.commit shared &state (SetScenario (before, next))

/// Fork a registered scenario into the scene as **one** step on the one history (spec 078): the commands that
/// reverse the materialised entities, plus — when the scenario's cue is not `Fin` — the cue forked into the
/// screen's cue. The scene work is already done by `materialise`, so the step is pushed without re-applying (the
/// `Spawning.instantiateZone` pattern). The active place, camera and selection are untouched (FR-004). Public so
/// it is drivable headlessly, like `commit`.
let fork (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (name: string) : unit =
    match tryContents name state.Scenarios with
    | None -> () // the control is disabled when unregistered (FR-008); the write path does nothing
    | Some contents ->
    match ScenarioFork.materialise shared &state contents with
    | None -> () // a target zone lacks room: the materialiser logged it and changed nothing (FR-010)
    | Some commands ->

    // An empty cue (`Fin`) forks nothing, so the running cue is genuinely untouched (US1 #4).
    let before = state.Cue
    let cueSteps =
        if contents.Cue.IsFin then []
        else
            let next = Cue.forkInto contents.Cue before
            state.Cue <- next
            [ SetCue (before, next) ]

    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo (Batch (commands @ cueSteps)) }

// ── the text field's NUL-terminated buffer ──

let private bufferText (buffer: byte[]) : string =
    let zero = System.Array.IndexOf(buffer, 0uy)
    let length = if zero < 0 then buffer.Length else zero
    if length = 0 then "" else System.Text.Encoding.UTF8.GetString(buffer, 0, length)

// ── the panel ──

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Scenario", System.IntPtr.Zero, 0) |> ignore

    // The field names a new scenario. Add is offered only for a name that survives trimming and is not already
    // present, and clears the field on success so the next name can be typed straight away (the Advent panel rule).
    let buffer = state.Editor.ScenarioNameBuffer
    do ImGui.inputText("Name##addscenario", buffer, unativeint buffer.Length, 0, System.IntPtr.Zero, System.IntPtr.Zero) |> ignore
    let typed = normalise (bufferText buffer)
    do ImGui.sameLine(0.0f, -1.0f)
    let addable = canAdd typed state.Scenarios
    do ImGui.beginDisabled(not addable)
    if ImGui.smallButton("Add##addscenario_btn") && addable then
        do commit shared &state (add typed state.Scenarios)
        do System.Array.Clear(buffer, 0, buffer.Length)
    do ImGui.endDisabled()

    // The current scenarios, in the order they were added. Register files the current scene under the name; Clear
    // empties a registered scenario but keeps its name; Fork (enabled only when registered) materialises it; Delete
    // removes the name and its contents. The name is unique in the library, so it is a stable widget id.
    let scenarioNames = names state.Scenarios
    if scenarioNames.IsEmpty then
        do ImGui.text "(none)"
    else
        for name in scenarioNames do
            do ImGui.pushId name

            let registered = tryContents name state.Scenarios

            // What the row reports: the number of zones filed, or that nothing is (yet). The cue is the screen's,
            // so it is the zone count that says a scenario is non-empty.
            let preview =
                match registered with
                | Some contents -> sprintf "%d zone(s)" contents.Zones.Count
                | None -> "(none)"

            do ImGui.text name
            do ImGui.sameLine(0.0f, 8.0f)
            do ImGui.text preview

            // Register captures the current scene as the scenario's contents. Always offered: the active place is
            // always present, so there is always something to capture.
            do ImGui.sameLine(0.0f, 8.0f)
            if ImGui.smallButton "Register" then
                do commit shared &state (setContents name (Some (capture state.Zones state.Cue)) state.Scenarios)

            // Clear empties a registered scenario, so it is offered only when there is something to clear; the
            // affordance is drawn where it becomes usable, not hidden. The name is kept.
            do ImGui.sameLine(0.0f, 8.0f)
            do ImGui.beginDisabled (Option.isNone registered)
            if ImGui.smallButton "Clear" then
                do commit shared &state (setContents name None state.Scenarios)
            do ImGui.endDisabled()

            // Fork adds the scenario's entities to the scene and forks its cue, as one undoable step (spec 078).
            do ImGui.sameLine(0.0f, 8.0f)
            do ImGui.beginDisabled (Option.isNone registered)
            if ImGui.smallButton "Fork" then
                do fork shared &state name
            do ImGui.endDisabled()

            // Delete removes the name and any contents filed under it, so the row leaves the list.
            do ImGui.sameLine(0.0f, 8.0f)
            if ImGui.smallButton "Delete" then
                do commit shared &state (remove name state.Scenarios)

            do ImGui.popId()

    do ImGui.endWindow()
