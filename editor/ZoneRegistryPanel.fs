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

open Command

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

                let registered = ZoneRegistry.tryTemplate entry.Place state.ZoneRegistry

                // An id that names no loaded asset raises rather than being skipped: only discovered
                // assets can be registered, so it is a bug rather than a state to render.
                let preview =
                    match registered with
                    | Some template -> shared.Assets.Environments.[template.AssetId].Name
                    | None -> "(none)"

                do ImGui.text entry.Name
                do ImGui.sameLine(0.0f, 8.0f)
                do ImGui.text preview

                // Register captures the **active place's** zone as that place's template, so it is offered
                // only for the row that is the active place (spec 061 FR-009): a place's config is captured
                // from its own zone, not filed under another. One write, no command.
                do ImGui.sameLine(0.0f, 8.0f)
                do ImGui.beginDisabled (Option.isNone environment || entry.Place <> state.ActiveZone)

                if ImGui.smallButton "Register" then
                    match Zone.captureTemplate (GameplayState.activeZone state) with
                    | Some template -> state.ZoneRegistry.Templates.[entry.Place] <- template
                    | None -> failwith "the register control is disabled while no environment is loaded"

                do ImGui.endDisabled()

                // Instantiate the registered zone at **this place**: make the place present and active (so
                // its bodies may be built) and load its template, as one step (spec 061 FR-009). The presence
                // change is folded into the step `instantiateZone` pushes, so one undo reverses the whole thing.
                do ImGui.sameLine(0.0f, 8.0f)
                do ImGui.beginDisabled (Option.isNone registered)

                if ImGui.smallButton "Instantiate" then
                    match registered with
                    | Some template ->
                        let place = entry.Place
                        let presence =
                            if not (state.Zones.ContainsKey place) then Some (ZoneAdd (state.ActiveZone, place))
                            elif place <> state.ActiveZone then Some (ZoneMove (state.ActiveZone, place))
                            else None
                        match presence with
                        | Some cmd -> History.apply shared &state cmd
                        | None -> ()
                        do Spawning.instantiateZone shared &state template
                        // `instantiateZone` pushed its own step; fold the presence change in front of it.
                        match presence, state.Editor.Undo.Applied with
                        | Some cmd, step :: rest ->
                            state.Editor <- { state.Editor with Undo = { state.Editor.Undo with Applied = Batch [ cmd; step ] :: rest } }
                        | _ -> ()
                        // The swap leaves a Part selection dangling; the caller reconciles it, the way
                        // the undo and redo paths do.
                        do Selection.dropStalePartSelection &state
                    | None -> failwith "the instantiate control is disabled while the place has no entry"

                do ImGui.endDisabled()

                do ImGui.popId()

            do ImGui.popId()

    do ImGui.endWindow()
