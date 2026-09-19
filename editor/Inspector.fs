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
