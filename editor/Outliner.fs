module Outliner

// Left tree panel: a **Zones** section (the project's zones, add/select/delete) above
// the active zone's entities in collapsible sections — actors, props, triggers, and
// waypoints. Each entity row selects on click and carries inline Del/Dup buttons.
// Triggers/waypoints have no zone storage yet.

open System.Text
open Entity

// The pending zone name in the text buffer, up to the first NUL byte; "" when empty.
let private zoneName (buf: byte[]) : string =
    let zero = System.Array.IndexOf(buf, 0uy)
    let length = if zero < 0 then buf.Length else zero
    if length = 0 then "" else Encoding.UTF8.GetString(buf, 0, length)

let private clearName (buf: byte[]) : unit =
    Array.fill buf 0 buf.Length 0uy

let private entityRow (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (name: string) (id: string) (ref: EntityRef) : unit =
    if ImGui.button(sprintf "%s##%s" name id, ImGui.ImVec2(0.0f, 0.0f)) then
        state.Editor <- { state.Editor with Selection = Some ref }

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton(sprintf "Del##del_%s" id) then
        Selection.deleteRef shared &state ref

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton(sprintf "Dup##dup_%s" id) then
        Selection.duplicateRef shared &state ref

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Outliner", System.IntPtr.Zero, 0) |> ignore

    // ── Zones ──
    // The project's zones, active one marked. Adding is offered only for a
    // non-empty, unused name; deleting is offered only while another zone remains.
    if ImGui.collapsingHeader("Zones", 0) then
        let names = state.Zones.Keys |> Seq.sort |> Seq.toArray
        for name in names do
            if state.Zones.ContainsKey name then
                let marker = if name = state.ActiveZone then "* " else "  "
                if ImGui.button(sprintf "%s%s##zone_%s" marker name name, ImGui.ImVec2(0.0f, 0.0f)) then
                    GameplayState.switchZone shared &state name
                if state.Zones.Count > 1 then
                    do ImGui.sameLine(0.0f, -1.0f)
                    if ImGui.smallButton(sprintf "Del##delzone_%s" name) then
                        GameplayState.deleteZone shared &state name

        do ImGui.text("")
        do ImGui.inputText("##newzone", state.Editor.NewZoneName, unativeint state.Editor.NewZoneName.Length, 0, System.IntPtr.Zero, System.IntPtr.Zero) |> ignore
        let pending = zoneName state.Editor.NewZoneName
        let canAdd = pending <> "" && not (state.Zones.ContainsKey pending)
        do ImGui.beginDisabled(not canAdd)
        if ImGui.button("Add Zone", ImGui.ImVec2(0.0f, 0.0f)) then
            GameplayState.addZone shared &state pending
            clearName state.Editor.NewZoneName
        do ImGui.endDisabled()
        do ImGui.text("")

    let zone = GameplayState.activeZone state

    // The currently loaded environment (a zone has at most one).
    let environmentName =
        match zone.Environment with
        | None -> "(none)"
        | Some environment -> environment.Base.Name
    do ImGui.text(sprintf "Environment: %s" environmentName)
    do ImGui.text("")

    if ImGui.collapsingHeader("Actors", 0) then
        if zone.Actors.Count = 0u then
            do ImGui.text("(none)")
        else
            let slots = zone.Actors.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    entityRow shared &state slot.Value.Base.Name (sprintf "actor%d" i) (actorRef (uint32 i) slot.Generation)

    if ImGui.collapsingHeader("Props", 0) then
        if zone.Props.Count = 0u then
            do ImGui.text("(none)")
        else
            let slots = zone.Props.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    entityRow shared &state slot.Value.Base.Name (sprintf "prop%d" i) (propRef (uint32 i) slot.Generation)

    if ImGui.collapsingHeader("Parts", 0) then
        match zone.Environment with
        | None -> do ImGui.text("(none)")
        | Some environment ->

        if environment.Parts.Count = 0u then
            do ImGui.text("(none)")
        else
            let slots = environment.Parts.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    entityRow shared &state slot.Value.Base.Name (sprintf "part%d" i) (partRef environment.Epoch (uint32 i) slot.Generation)

    if ImGui.collapsingHeader("Triggers", 0) then
        do ImGui.text("(none)")

    if ImGui.collapsingHeader("Waypoints", 0) then
        do ImGui.text("(none)")

    do ImGui.endWindow()
