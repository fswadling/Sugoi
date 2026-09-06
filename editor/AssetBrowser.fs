module AssetBrowser

// Bottom panel: lists the discovered actors, environments, and props in named
// sections. Clicking an actor/prop spawns it into the active zone and selects
// it; clicking an environment loads it into the zone's single environment slot.

open Entity
open Command

let private baseEntity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Asset Browser", System.IntPtr.Zero, 0) |> ignore

    let assets = shared.Assets

    if assets.Actors.Count = 0 && assets.Props.Count = 0 && assets.Environments.Count = 0 then
        do ImGui.text("(no assets)")
    else
        // ── Actors ──
        if assets.Actors.Count > 0 then
            do ImGui.text("Actors")
            for asset in assets.Actors.Values |> Seq.sortBy (fun a -> a.Id) do
                if ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f)) then
                    let entity = baseEntity asset.Name
                    let ref = Zone.spawnActor state.Zone shared.Physics assets entity asset.Id
                    let binding = { AssetId = Some asset.Id; SubmeshIndex = 0; SubmeshCount = 0 }
                    let undo = UndoStack.push state.Editor.Undo (Spawn (ref, entity, binding))
                    state.Editor <- { state.Editor with Selection = Some ref; Undo = undo }

        // ── Environments ──
        if assets.Environments.Count > 0 then
            do ImGui.text("")
            do ImGui.text("Environments")
            for asset in assets.Environments.Values |> Seq.sortBy (fun a -> a.Id) do
                if ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f)) then
                    let previous = state.Zone.Environment |> Option.map (fun e -> e.AssetId)
                    do Zone.loadEnvironment state.Zone shared.Physics assets asset.Id
                    let undo = UndoStack.push state.Editor.Undo (SetEnvironment (previous, Some asset.Id))
                    state.Editor <- { state.Editor with Undo = undo }

        // ── Props ──
        if assets.Props.Count > 0 then
            do ImGui.text("")
            do ImGui.text("Props")
            for asset in assets.Props.Values |> Seq.sortBy (fun a -> a.Id) do
                if ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f)) then
                    let entity = baseEntity asset.Name
                    let ref = Zone.spawnProp state.Zone shared.Physics assets entity asset.Id
                    let binding = { AssetId = Some asset.Id; SubmeshIndex = 0; SubmeshCount = 0 }
                    let undo = UndoStack.push state.Editor.Undo (Spawn (ref, entity, binding))
                    state.Editor <- { state.Editor with Selection = Some ref; Undo = undo }

    do ImGui.endWindow()
