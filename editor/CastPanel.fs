module CastPanel

// The Cast panel: the cast as a tree, with a discovered actor asset assignable to each
// member-and-look. It is the one editor surface that assigns **in place** rather than through the
// Inspector, because a slot — a member and a look — is not a selectable item in the one-selection
// model, and there is one for every look of every member (`docs/EDITOR_DESIGN.md`, D-022).
//
// Assigning is a direct write to the binding and not a command: a binding is not a scene edit — the cast is not
// a zone's, and re-making an assignment costs two clicks in a list. Whether it should become an undoable step is
// open; `docs/EDITOR_DESIGN.md` D-022 has the reasoning.
//
// Placing an actor is the opposite kind of act, and takes the gameplay state for it: a bound slot's
// Spawn goes through `Spawning`, which creates the actor, records the command that reverses it, and
// selects it. So placing is a scene edit and is undoable, while assigning is not (spec 033).

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Cast", System.IntPtr.Zero, 0) |> ignore

    if shared.Assets.Actors.Count = 0 then
        do ImGui.text("(no actor assets)")
    else

    // Sorted once for the whole panel rather than once per slot.
    let actors = shared.Assets.Actors.Values |> Seq.sortBy (fun asset -> asset.Id) |> Seq.toArray

    for character in Roster.characters do
        let name = Roster.characterName character

        // Closed by default: every member expanded at once is a wall of rows, and an author works on
        // one member at a time. The pushed id scopes the rows to their member.
        if ImGui.collapsingHeader(name, 0) then
            do ImGui.pushId name

            for look in Casting.allLooks do
                // The member scopes the member's rows, the look scopes the row. Both are needed:
                // every member walks the same looks in the same order, so a repeated label like
                // "Spawn" would otherwise hash to one id across the rows and collide.
                do ImGui.pushId (Casting.lookName look)

                let slot : Casting.Slot = { Character = character; Look = look }
                let current = Casting.tryAsset slot shared.Cast

                // An id that names no loaded asset raises rather than being skipped: only discovered
                // assets can be assigned, so it is a bug rather than a state to render.
                let preview =
                    match current with
                    | Some id -> shared.Assets.Actors.[id].Name
                    | None -> "(none)"

                if ImGui.beginCombo(Casting.lookName look, preview, 0) then
                    if ImGui.selectableBool("(none)", Option.isNone current, 0, ImGui.ImVec2(0.0f, 0.0f)) then
                        shared.Cast.Bindings.Remove slot |> ignore

                    for asset in actors do
                        let selected =
                            match current with
                            | Some id -> System.String.Equals(id, asset.Id, System.StringComparison.Ordinal)
                            | None -> false

                        if ImGui.selectableBool(asset.Name, selected, 0, ImGui.ImVec2(0.0f, 0.0f)) then
                            shared.Cast.Bindings.[slot] <- asset.Id

                    do ImGui.endCombo()

                // One control per slot, inert while the slot is unassigned: the affordance is drawn
                // where it will become usable rather than hidden, and there is no default model to
                // fall back on.
                do ImGui.sameLine(0.0f, 8.0f)
                do ImGui.beginDisabled (Option.isNone current)

                if ImGui.smallButton "Spawn" then
                    match current with
                    | Some assetId ->
                        do Spawning.spawnActor shared &state (Spawning.baseEntity name) assetId (Some character)
                    | None -> ()

                do ImGui.endDisabled()

                do ImGui.popId()

            do ImGui.popId()

    do ImGui.endWindow()
