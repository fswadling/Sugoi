module AssetBrowser

// Bottom panel: lists the discovered actors/props. Clicking an actor/prop spawns
// it into the active zone and selects it (environments are listed later, not
// spawnable this phase).

open Entity
open Command

let private baseEntity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

let draw (assets: Assets.AssetManager) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Asset Browser", System.IntPtr.Zero, 0) |> ignore

    if assets.Actors.Count = 0 && assets.Props.Count = 0 && assets.Environments.Count = 0 then
        do ImGui.text("(no assets)")
    else
        // Actors spawn with their model binding.
        for asset in assets.Actors.Values |> Seq.sortBy (fun a -> a.Id) do
            if ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f)) then
                let entity = baseEntity asset.Name
                let ref = Zone.spawnActor state.Zone assets entity asset.Id
                let undo = UndoStack.push state.Editor.Undo (Spawn (ref, entity, asset.Id))
                state.Editor <- { state.Editor with Selection = Some ref; Undo = undo }

        // Props spawn with their model binding (a static glTF submesh set).
        for asset in assets.Props.Values |> Seq.sortBy (fun a -> a.Id) do
            if ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f)) then
                let entity = baseEntity asset.Name
                let ref = Zone.spawnProp state.Zone assets entity asset.Id
                let undo = UndoStack.push state.Editor.Undo (Spawn (ref, entity, asset.Id))
                state.Editor <- { state.Editor with Selection = Some ref; Undo = undo }

        // Environments load into the zone's single environment slot.
        for asset in assets.Environments.Values |> Seq.sortBy (fun a -> a.Id) do
            if ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f)) then
                do Zone.setEnvironment state.Zone assets asset.Id

    do ImGui.endWindow()
