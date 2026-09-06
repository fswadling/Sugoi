module Inspector

// Right panel: the single editing surface for the selection. Shows the selected
// entity's name + transform; actors additionally expose a toggle to visualize
// their capsule collider.

open Entity

let draw (state: byref<GameplayState.GameplayState>) =
    do ImGui.beginWindow("Inspector", System.IntPtr.Zero, 0) |> ignore

    match state.Editor.Selection with
    | Some ref ->
        match Zone.entityAt state.Zone ref with
        | Some e ->
            do ImGui.text(sprintf "Name: %s" e.Name)
            do ImGui.text(sprintf "Position: %A" e.Transform.Position)
            do ImGui.text(sprintf "Rotation: %A" (eulerDegrees e.Transform.Rotation))
            do ImGui.text(sprintf "Scale: %A" e.Transform.Scale)

            // Actor-only capsule-collider visibility toggle.
            if ref.Kind = EntityKind.Actor then
                match SlotMap.tryGetByIndex state.Zone.Actors ref.Index with
                | None -> ()
                | Some actor ->
                let mutable show = actor.ShowCollider
                if ImGui.checkbox("Show Collider", &show) then
                    state.Zone.Actors <- SlotMap.setByIndex state.Zone.Actors ref.Index { actor with ShowCollider = show }
        | None ->
            do ImGui.text("(no selection)")
    | None ->
        do ImGui.text("(no selection)")

    do ImGui.endWindow()
