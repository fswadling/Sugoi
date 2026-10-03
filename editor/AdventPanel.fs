module AdventPanel

// The Advent panel (spec 055): the story milestones — the named adventure flags — as a checklist the
// author ticks and unticks. It is docked beside the Inspector on the right, with the other editing
// surfaces, because what it edits is a set of story facts rather than a place in the scene.
//
// The milestones are a closed union (`world/Advent.fs`), so the panel enumerates every case rather
// than offering a free-text field: ticking a case sets it, unticking it clears it. Each toggle is one
// step on the one undo history (`docs/EDITOR_DESIGN.md`, D-011) — the set is not a zone's, so
// `Command.Advent` carries the set before and after the edit and `state/History.fs` writes
// `SharedState.Advents`, the same shape the cue's `SetCue` step has (`specs/048`, `specs/049`). The
// panel keeps no history of its own; `Ctrl+Z`, `Ctrl+Y` and the Edit menu reach the step.

open Advent

// ── the write path ──

/// Record a new flag set as one step on the one history (FR-007). The caller computes `next` with the
/// standard `Set` functions; this applies `next` and pushes the before/after step in one place, so the
/// panel keeps no history of its own.
let commit (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (next: Set<Advent>) : unit =
    let before = shared.Advents
    do History.commit shared &state (Command.Advent (before, next))

// ── the panel ──

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Advent", System.IntPtr.Zero, 0) |> ignore

    // One checkbox per milestone, seeded from the flag set: ticking sets the milestone, unticking
    // clears it, and either is one step. Each case's own name is its label, so the rows are stable
    // and unique.
    for advent in all do
        let mutable ticked = Set.contains advent shared.Advents
        if ImGui.checkbox(name advent, &ticked) then
            let next =
                if ticked then Set.add advent shared.Advents
                else Set.remove advent shared.Advents
            do commit shared &state next

    do ImGui.endWindow()
