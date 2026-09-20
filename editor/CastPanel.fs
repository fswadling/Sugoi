module CastPanel

// The Cast panel: the cast as a tree, with a discovered actor asset assignable to each
// member-and-look. It is the one editor surface that assigns **in place** rather than through the
// Inspector, because a slot — a member and a look — is not a selectable item in the one-selection
// model, and there are forty of them (`docs/EDITOR_DESIGN.md`, D-022).
//
// Assigning is a direct write to the binding and not a command: the undo stacks are per zone
// (`GameplayState.UndoByZone`) and the binding outlives every zone, so an assignment recorded in
// one zone's stack would be unreachable from another and discarded outright when that zone is
// deleted. That is also why this takes `shared` alone — it edits project state, not the scene.

let draw (shared: SharedState.SharedState) : unit =
    do ImGui.beginWindow("Cast", System.IntPtr.Zero, 0) |> ignore

    if shared.Assets.Actors.Count = 0 then
        do ImGui.text("(no actor assets)")
    else

    // Sorted once for the whole panel rather than once per slot.
    let actors = shared.Assets.Actors.Values |> Seq.sortBy (fun asset -> asset.Id) |> Seq.toArray

    for character in Roster.characters do
        let name = Roster.characterName character

        // Closed by default: twenty members expanded is sixty-odd rows, and an author works on one
        // member at a time. The pushed id scopes the look labels, which repeat under every member.
        if ImGui.collapsingHeader(name, 0) then
            do ImGui.pushId name

            for look in Casting.allLooks do
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

            do ImGui.popId()

    do ImGui.endWindow()
