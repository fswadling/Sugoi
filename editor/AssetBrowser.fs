module AssetBrowser

// Bottom panel: lists the discovered actors, environments, and props in named
// sections. Clicking an actor/prop spawns it into the active zone and selects
// it; clicking an environment loads it into the zone's single environment slot.

open Command

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    let visible = ImGui.beginWindow("Asset Browser", System.IntPtr.Zero, 0)
    if visible then

        let assets = shared.Assets
        let zone = GameplayState.activeZone state

        if assets.Actors.Count = 0 && assets.Props.Count = 0 && assets.Environments.Count = 0 then
            do ImGui.text("(no assets)")
        else
            // ── Spawn anchor ──
            // One shared placement mode for both asset sections. Environments are loaded, not spawned, and
            // ignore it. The selected value is transient editor state (spec 101).
            if assets.Actors.Count > 0 || assets.Props.Count > 0 then
                let anchor = state.Editor.SpawnAt
                if ImGui.beginCombo("Spawn at##asset_spawn", EditorState.anchorName anchor, 0) then
                    for candidate in EditorState.spawnAnchors do
                        do ImGui.beginDisabled(not (Spawning.anchorAvailable zone candidate))
                        if ImGui.selectable (EditorState.anchorName candidate) (candidate = anchor) then
                            state.Editor <- { state.Editor with SpawnAt = candidate }
                        do ImGui.endDisabled()
                    do ImGui.endCombo()
                do ImGui.text("")

            let canSpawn = Spawning.anchorAvailable zone state.Editor.SpawnAt

            // ── Actors ──
            if assets.Actors.Count > 0 then
                do ImGui.text("Actors")
                let actorRoom = canSpawn && zone.Actors.Count < zone.Actors.Capacity
                for asset in assets.Actors.Values |> Seq.sortBy (fun a -> a.Id) do
                    do ImGui.beginDisabled(not actorRoom)
                    let add = ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f))
                    do ImGui.endDisabled()
                    if add then
                        do Spawning.spawnActorAt shared &state (Spawning.baseEntity asset.Name) asset.Id state.Editor.SpawnAt

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
                        do Undo.commit shared &state (SetEnvironment (state.ActiveZone, previous, Some next))

            // ── Props ──
            if assets.Props.Count > 0 then
                do ImGui.text("")
                do ImGui.text("Props")
                let propRoom = canSpawn && zone.Props.Count < zone.Props.Capacity
                for asset in assets.Props.Values |> Seq.sortBy (fun a -> a.Id) do
                    do ImGui.beginDisabled(not propRoom)
                    let add = ImGui.button(asset.Name, ImGui.ImVec2(0.0f, 0.0f))
                    do ImGui.endDisabled()
                    if add then
                        do Spawning.spawnPropAt shared &state (Spawning.baseEntity asset.Name) asset.Id state.Editor.SpawnAt

    do ImGui.endWindow()
