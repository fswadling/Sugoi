module ScenarioPanel

// The Scenario panel (spec 070): the game's scenarios, with a Register control that files the current scene under
// one and a Clear control that empties it. It is the third in-place surface beside Cast and the Zone Registry
// (`docs/EDITOR_DESIGN.md`), because a scenario is not a selectable item in the scene.
//
// Register **captures** rather than edits: it copies every present zone and the screen's cue
// into one value filed under the scenario, and touches no zone, camera or selection. Unlike the Zone Registry's
// direct write it rides the one undo history — the capture is immutable data, so a before/after `SetScenario`
// step makes a registration, an overwrite and a clear all total under undo (the same class as the Advent panel).

open Command
open Scenario
open ScenarioRegistry

// ── the write path ──

/// File `next` under `scenario` as one step on the one history, reading the entry's `before` here so the panel's
/// control is a single call. `None` clears the entry. Keeps no history of its own; `Ctrl+Z` reaches the step.
/// Public like `AdventPanel.commit`, so the write path is drivable headlessly.
let commit (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (scenario: Scenario) (next: ScenarioContents option) : unit =
    let before = tryContents scenario state.Scenarios
    do History.commit shared &state (SetScenario (scenario, before, next))

/// Fork a registered scenario into the scene as **one** step on the one history (spec 078): the commands that
/// reverse the materialised entities, plus — when the scenario's cue is not `Fin` — the cue forked into the
/// screen's cue. The scene work is already done by `materialise`, so the step is pushed without re-applying (the
/// `Spawning.instantiateZone` pattern). The active place, camera and selection are untouched (FR-004). Public so
/// it is drivable headlessly, like `commit`.
let fork (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (scenario: Scenario) : unit =
    match tryContents scenario state.Scenarios with
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

// ── the panel ──

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Scenario", System.IntPtr.Zero, 0) |> ignore

    for scenario in all do
        // The scenario scopes the row: every row carries the same "Register"/"Clear" labels, which would otherwise
        // hash to one id across the rows and collide.
        do ImGui.pushId (scenarioName scenario)

        let registered = tryContents scenario state.Scenarios

        // What the row reports (FR-006): the number of zones filed, or that nothing is (yet). The cue is the
        // screen's, so it is the zone count that says a scenario is non-empty.
        let preview =
            match registered with
            | Some contents -> sprintf "%d zone(s)" contents.Zones.Count
            | None -> "(none)"

        do ImGui.text (scenarioName scenario)
        do ImGui.sameLine(0.0f, 8.0f)
        do ImGui.text preview

        // Register captures the current scene as the scenario's contents. Always offered: the active place is
        // always present, so there is always something to capture.
        do ImGui.sameLine(0.0f, 8.0f)
        if ImGui.smallButton "Register" then
            do commit shared &state scenario (Some (capture state.Zones state.Cue))

        // Clear empties a registered scenario, so it is offered only when there is something to clear: the
        // affordance is drawn where it becomes usable (as Cast's Spawn is), not hidden.
        do ImGui.sameLine(0.0f, 8.0f)
        do ImGui.beginDisabled (Option.isNone registered)
        if ImGui.smallButton "Clear" then
            do commit shared &state scenario None
        do ImGui.endDisabled()

        // Fork adds the scenario's entities to the scene and forks its cue, as one undoable step (spec 078).
        // Offered only when there is something to fork, like Clear (FR-008).
        do ImGui.sameLine(0.0f, 8.0f)
        do ImGui.beginDisabled (Option.isNone registered)
        if ImGui.smallButton "Fork" then
            do fork shared &state scenario
        do ImGui.endDisabled()

        do ImGui.popId()

    do ImGui.endWindow()
