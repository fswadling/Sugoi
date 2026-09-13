module Inspector

// Right panel: the single editing surface for the selection. Shows the selected
// entity's name + transform; actors additionally expose a toggle to visualize
// their capsule collider.

open Entity

let draw (state: byref<GameplayState.GameplayState>) =
    do ImGui.beginWindow("Inspector", System.IntPtr.Zero, 0) |> ignore

    let zone = GameplayState.activeZone state

    match state.Editor.Selection with
    | Some ref ->
        let entity = Zone.tryEntityAt zone ref
        match entity with
        | None -> do ImGui.text("(no selection)")
        | Some e ->

        do ImGui.text(sprintf "Name: %s" e.Name)
        do ImGui.text(sprintf "Position: %A" e.Transform.Position)
        do ImGui.text(sprintf "Rotation: %A" (eulerDegrees e.Transform.Rotation))
        do ImGui.text(sprintf "Scale: %A" e.Transform.Scale)

        // Actor-only: bind this actor as the played character. A deliberate
        // play-binding action, not an undoable scene edit, and it touches no physics.
        if ref.Kind = EntityKind.Actor then
            let handle : SlotMap.Handle = { Index = ref.Index; Generation = ref.Generation }
            let mutable show = (zone.Actors.Get(handle)).ShowCollider
            if ImGui.checkbox("Show Collider", &show) then
                let actor = &zone.Actors.Get(handle)
                actor <- { actor with ShowCollider = show }

            if ImGui.button("Set as Player", ImGui.ImVec2(0.0f, 0.0f)) then
                zone.Player <- ValueSome (Systems.playerHandleOf ref)
    | None ->
        do ImGui.text("(no selection)")

    do ImGui.endWindow()
