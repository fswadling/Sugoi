module AssetBrowser

// Bottom panel: lists the assets discovered from disk.

let draw (shared: SharedState.SharedState) =
    ImGui.beginWindow("Asset Browser", System.IntPtr.Zero, 0) |> ignore

    if shared.Assets.Length = 0 then
        do ImGui.text("(no assets)")
    else
        for asset in shared.Assets do
            do ImGui.text(sprintf "%s (%A)" asset.Name asset.Kind)

    do ImGui.endWindow()
