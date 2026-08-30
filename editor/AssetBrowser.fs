module AssetBrowser

// Bottom panel: lists the discovered actors/props. Clicking an actor/prop spawns
// it into the active zone and selects it (environments are listed later, not
// spawnable this phase).

open Entity

let private baseEntity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

let draw (state: GameplayState.GameplayState) (shared: SharedState.SharedState) : GameplayState.GameplayState =
    do ImGui.beginWindow("Asset Browser", System.IntPtr.Zero, 0) |> ignore

    let mutable state = state

    if shared.Assets.Length = 0 then
        do ImGui.text("(no assets)")
    else
        for asset in shared.Assets do
            match asset.Kind with
            | EntityKind.Actor ->
                if ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f)) then
                    let zone, ref = Zone.spawnActor state.Zone (baseEntity asset.Name)
                    state <- { state with Zone = zone; Editor = { state.Editor with Selection = Some ref } }
            | EntityKind.Prop ->
                if ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f)) then
                    let zone, ref = Zone.spawnProp state.Zone (baseEntity asset.Name)
                    state <- { state with Zone = zone; Editor = { state.Editor with Selection = Some ref } }
            | _ -> ()

    do ImGui.endWindow()
    state
