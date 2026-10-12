module AssetBrowser

// Bottom panel: lists the discovered actors, environments, and props in named
// sections. Clicking an actor/prop spawns it into the active zone and selects
// it; clicking an environment loads it into the zone's single environment slot.

open Command

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Asset Browser", System.IntPtr.Zero, 0) |> ignore

    let assets = shared.Assets
    let zone = GameplayState.activeZone state

    if assets.Actors.Count = 0 && assets.Props.Count = 0 && assets.Environments.Count = 0 then
        do ImGui.text("(no assets)")
    else
        // ── Actors ──
        if assets.Actors.Count > 0 then
            do ImGui.text("Actors")
            let actorRoom = zone.Actors.Count < zone.Actors.Capacity
            for asset in assets.Actors.Values |> Seq.sortBy (fun a -> a.Id) do
                do ImGui.beginDisabled(not actorRoom)
                let add = ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f))
                do ImGui.endDisabled()
                if add then
                    do Spawning.spawnActor shared &state (Spawning.baseEntity asset.Name) asset.Id

        // ── Environments ──
        if assets.Environments.Count > 0 then
            do ImGui.text("")
            do ImGui.text("Environments")
            for asset in assets.Environments.Values |> Seq.sortBy (fun a -> a.Id) do
                if ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f)) then
                    let previous = zone.Environment
                    let next = Zone.loadEnvironment zone state.Physics assets asset.Id
                    // A carried-over part selection no longer resolves; drop it.
                    do Selection.dropStalePartSelection &state
                    let undo = UndoStack.push state.Editor.Undo (SetEnvironment (state.ActiveZone, previous, Some next))
                    state.Editor <- { state.Editor with Undo = undo }

        // ── Props ──
        if assets.Props.Count > 0 then
            do ImGui.text("")
            do ImGui.text("Props")
            for asset in assets.Props.Values |> Seq.sortBy (fun a -> a.Id) do
                do ImGui.beginDisabled(not (zone.Props.Count < zone.Props.Capacity))
                let add = ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f))
                do ImGui.endDisabled()
                if add then
                    do Spawning.spawnProp shared &state (Spawning.baseEntity asset.Name) asset.Id

    do ImGui.endWindow()
