module Inspector

// Right panel: the single editing surface for the selection. The name is editable
// — committed on Enter or on leaving the field, as one undoable step — the transform
// is shown read-only, and actors additionally expose a toggle to visualize their
// capsule collider.

open System.Text
open Entity

// Write `name` into `buf` as a NUL-terminated UTF-8 string. At most
// `EditorState.entityNameMaxChars` characters are written, so a long name is
// truncated at a character boundary and can never overrun the buffer.
let private seedName (buf: byte[]) (name: string) : unit =
    let charCount = min name.Length EditorState.entityNameMaxChars
    let byteCount = Encoding.UTF8.GetBytes(name, 0, charCount, buf, 0)
    buf.[byteCount] <- 0uy

// The text a NUL-terminated buffer holds, up to the first NUL.
let private bufferText (buf: byte[]) : string =
    let zero = System.Array.IndexOf(buf, 0uy)
    let length = if zero < 0 then buf.Length else zero
    if length = 0 then "" else Encoding.UTF8.GetString(buf, 0, length)

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) =
    do ImGui.beginWindow("Inspector", System.IntPtr.Zero, 0) |> ignore

    let zone = GameplayState.activeZone state

    match state.Editor.Selection with
    | Some ref ->
        let entity = Zone.tryEntityAt zone ref
        match entity with
        | None -> do ImGui.text("(no selection)")
        | Some e ->

        // ── Name (editable) ──
        // One buffer serves every selected entity. Re-seeding it each frame cannot
        // disturb typing: while the field is active ImGui's own text takes priority
        // over the buffer, and it writes the final text back when the field is
        // submitted or left, which keeps the field in step with undo/redo and with
        // re-selection. The text present here at commit time belongs to the entity it
        // was typed for, which is what `NameEditRef` records: the selection can change
        // underneath an in-progress edit, and the edit must not follow it.
        do seedName state.Editor.NameBuffer e.Name
        let submitted =
            ImGui.inputText("Name", state.Editor.NameBuffer, unativeint state.Editor.NameBuffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero)
        // The edit is committed to the entity it was typed for, which is not always the
        // selected one — `Selection.renameTarget` decides that.
        let finished = submitted || ImGui.isItemDeactivatedAfterEdit()
        match Selection.renameTarget state.Editor.NameEditRef finished (bufferText state.Editor.NameBuffer) with
        | Some (target, name) -> Selection.renameRef shared &state target name
        | None -> ()
        state.Editor <- { state.Editor with NameEditRef = Some ref }

        do ImGui.text(sprintf "Position: %A" e.Transform.Position)
        do ImGui.text(sprintf "Rotation: %A" (eulerDegrees e.Transform.Rotation))
        do ImGui.text(sprintf "Scale: %A" e.Transform.Scale)

        // Trigger-only: the volume's shape and its interaction settings. Each committed
        // edit is one `TriggerSet` step — a drag commits when it is released or left, like
        // the text fields commit when they are submitted or left.
        if ref.Kind = EntityKind.Trigger then
            match Zone.tryTriggerAt zone ref with
            | ValueNone -> ()
            | ValueSome trigger ->

            let mutable radius = trigger.Radius
            do ImGui.dragFloat("Radius", &radius, 0.05f, 0.0f, 1000.0f, "%.2f", 0) |> ignore
            if ImGui.isItemDeactivatedAfterEdit() then
                Selection.updateTriggerRef shared &state ref (fun t -> { t with Radius = radius })

            let mutable height = trigger.Height
            do ImGui.dragFloat("Height", &height, 0.05f, 0.0f, 1000.0f, "%.2f", 0) |> ignore
            if ImGui.isItemDeactivatedAfterEdit() then
                Selection.updateTriggerRef shared &state ref (fun t -> { t with Height = height })

            let mutable interactable = trigger.Interactable
            if ImGui.checkbox("Interactable", &interactable) then
                Selection.updateTriggerRef shared &state ref (fun t -> { t with Interactable = interactable })

            // The prompt commits like the entity name and is attributed the same way: a
            // pending edit belongs to the trigger it was typed for.
            do seedName state.Editor.PromptBuffer trigger.Prompt
            let submitted =
                ImGui.inputText("Prompt", state.Editor.PromptBuffer, unativeint state.Editor.PromptBuffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero)
            let finished = submitted || ImGui.isItemDeactivatedAfterEdit()
            match Selection.renameTarget state.Editor.PromptEditRef finished (bufferText state.Editor.PromptBuffer) with
            | Some (owner, prompt) -> Selection.updateTriggerRef shared &state owner (fun t -> { t with Prompt = prompt })
            | None -> ()
            state.Editor <- { state.Editor with PromptEditRef = Some ref }

            // The enabled flag is the shared entity one: for a trigger it means it can
            // detect the player.
            let mutable enabled = trigger.Base.Enabled
            if ImGui.checkbox("Enabled", &enabled) then
                Selection.updateTriggerRef shared &state ref (fun t -> { t with Base = { t.Base with Enabled = enabled } })

        // Actor-only: bind this actor as the played character. A deliberate
        // play-binding action, not an undoable scene edit, and it touches no physics.
        if ref.Kind = EntityKind.Actor then
            let mutable show = (Zone.captureActor zone ref).ShowCollider
            if ImGui.checkbox("Show Collider", &show) then
                do Zone.setShowCollider zone ref show

            if ImGui.button("Set as Player", ImGui.ImVec2(0.0f, 0.0f)) then
                zone.Player <- ValueSome (Systems.playerHandleOf ref)
    | None ->
        do ImGui.text("(no selection)")

    do ImGui.endWindow()
