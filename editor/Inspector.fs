module Inspector

// Right panel: the single editing surface for the selection. Shows the selected
// entity's name + transform; actors additionally expose a toggle to visualize
// their capsule collider.

open Entity

let draw (state: byref<GameplayState.GameplayState>) =
    do ImGui.beginWindow("Inspector", System.IntPtr.Zero, 0) |> ignore

    match state.Editor.Selection with
    | Some ref ->
        let entity = Zone.tryEntityAt state.Zone ref
        match entity with
        | None -> do ImGui.text("(no selection)")
        | Some e ->

        do ImGui.text(sprintf "Name: %s" e.Name)
        do ImGui.text(sprintf "Position: %A" e.Transform.Position)
        do ImGui.text(sprintf "Rotation: %A" (eulerDegrees e.Transform.Rotation))
        do ImGui.text(sprintf "Scale: %A" e.Transform.Scale)

        // Actor-only capsule-collider toggle. `TryEntityAt` above resolved this ref;
        // re-resolve through `Get` after the ImGui call rather than holding a byref.
        if ref.Kind = EntityKind.Actor then
            let handle : SlotMap.Handle = { Index = ref.Index; Generation = ref.Generation }
            let mutable show = (state.Zone.Actors.Get(handle)).ShowCollider
            if ImGui.checkbox("Show Collider", &show) then
                let actor = &state.Zone.Actors.Get(handle)
                actor <- { actor with ShowCollider = show }
    | None ->
        do ImGui.text("(no selection)")

    do ImGui.endWindow()
