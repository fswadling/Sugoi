module ZoneRegistryPanel

// The Zone Registry panel: the game's places as a tree, with the zone registered at each (spec 034).
//
// The tree is the vocabulary itself rather than a list beside it, so a place or a variant the code
// declares appears here without anyone remembering to add it, and two zones are free to declare
// entirely different sets.
//
// What this panel writes is not a scene edit. Registering files the zone against a place as a copy —
// its environment, its parts and its placed props — and a registration is one click to make again by hand, so
// registering is a direct write rather than a command; whether it should become an undoable step is open. That
// is the same reasoning the cast binding rests on (`docs/EDITOR_DESIGN.md`, D-023). Instantiating
// is the opposite kind of act and is undoable, through the command that already carries a whole
// environment.

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Zone Registry", System.IntPtr.Zero, 0) |> ignore

    // Read once for the whole panel: a control's enabled state and the action it takes must not be
    // able to disagree about whether the active zone has an environment loaded.
    let environment = (GameplayState.activeZone state).Environment

    for group in ZoneRegistry.byZone do
        // Closed by default: the panel is a map of the game's places rather than a workspace, and an
        // author works on one place at a time. The pushed id scopes the rows to their zone.
        if ImGui.collapsingHeader(group.Name, 0) then
            do ImGui.pushId group.Name

            for entry in group.Places do
                // The place scopes the row: its controls carry the same labels as every other row's,
                // which is the ImGui id collision a panel with repeated row widgets ships unless both
                // names are on the stack.
                do ImGui.pushId entry.Name

                let registered = ZoneRegistry.tryTemplate entry.Place shared.ZoneRegistry

                // An id that names no loaded asset raises rather than being skipped: only discovered
                // assets can be registered, so it is a bug rather than a state to render.
                let preview =
                    match registered with
                    | Some template -> shared.Assets.Environments.[template.AssetId].Name
                    | None -> "(none)"

                do ImGui.text entry.Name
                do ImGui.sameLine(0.0f, 8.0f)
                do ImGui.text preview

                // Register the active zone against this place: one write, no command. The registry is not
                // the scene's, so nothing in the scene changes and no undo step is recorded — the
                // affordance is drawn where it becomes usable rather than hidden. The entry is captured
                // only when the control is activated, because capturing walks the zone's parts and its
                // props; the enabled state is the cheap question, and the two cannot disagree.
                do ImGui.sameLine(0.0f, 8.0f)
                do ImGui.beginDisabled (Option.isNone environment)

                if ImGui.smallButton "Register" then
                    match Zone.captureTemplate (GameplayState.activeZone state) with
                    | Some template -> shared.ZoneRegistry.Templates.[entry.Place] <- template
                    | None -> failwith "the register control is disabled while no environment is loaded"

                do ImGui.endDisabled()

                // Instantiate the registered zone into the active zone. This is a scene edit, so it goes
                // through the command that already carries a whole environment and already reverses it:
                // one step, and undo puts back whatever the zone held (FR-013).
                do ImGui.sameLine(0.0f, 8.0f)
                do ImGui.beginDisabled (Option.isNone registered)

                if ImGui.smallButton "Instantiate" then
                    match registered with
                    | Some template ->
                        do Spawning.instantiateZone shared &state template
                        // The swap leaves a Part selection dangling; the caller reconciles it, the way
                        // the undo and redo paths do.
                        do Selection.dropStalePartSelection &state
                    | None -> failwith "the instantiate control is disabled while the place has no entry"

                do ImGui.endDisabled()

                do ImGui.popId()

            do ImGui.popId()

    do ImGui.endWindow()
