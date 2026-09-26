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

// The actor's animation slots: the clips it is blending, each with a weight, and the
// controls to add and remove them (spec 036, FR-014 to FR-020). A direct write to the
// runtime renderer — no command, nothing persisted (FR-019) — through the same
// `ActorRender.setClipWeight` the gameplay caller uses (FR-020). Only called for an
// actor, so no other kind of selection shows the section (FR-014).
let private drawAnimations (zone: Zone.Zone) (ref: EntityRef) : unit =
    let actor = &zone.Actors.Get(handleOf ref)
    let anims = actor.Renderer.Asset.Model.Model.Anims
    let span = actor.Renderer.AnimationSlots.Slots

    if not (ImGui.collapsingHeader("Animations", 0)) then () else

    if anims.Length = 0 then
        do ImGui.text("(asset has no clips)")
    else

    // ── held clips: a row each, with a weight drag and a remove ──
    let mutable held = 0
    for i in 0 .. span.Length - 1 do
        let slot = span.[i]
        if slot.Alive then
            held <- held + 1
            let anim = anims.[slot.Value.AnimIndex]
            do ImGui.pushId anim.Name
            do ImGui.text anim.Name
            do ImGui.sameLine(0.0f, 8.0f)
            let mutable weight = slot.Value.Weight
            if ImGui.dragFloat("Weight", &weight, 0.01f, 0.0f, 1.0f, "%.2f", 0) then
                do ActorRender.setClipWeight &actor.Renderer anim.Name weight
            do ImGui.sameLine(0.0f, 8.0f)
            if ImGui.smallButton "Remove" then
                do ActorRender.removeClip &actor.Renderer anim.Name
            do ImGui.popId()

    if held = 0 then
        do ImGui.text("(holds no clips)")

    if held >= ActorRender.animationSlotCapacity then
        do ImGui.text("(maximum 8 clips)")
    else

    // ── add: a button per clip the asset defines and the actor does not hold ──
    let mutable addable = 0
    for a in 0 .. anims.Length - 1 do
        let mutable already = false
        for i in 0 .. span.Length - 1 do
            let slot = span.[i]
            if slot.Alive && slot.Value.AnimIndex = a then already <- true
        if not already then
            addable <- addable + 1
            if ImGui.smallButton anims.[a].Name then
                do ActorRender.setClipWeight &actor.Renderer anims.[a].Name 0.0f

    if addable = 0 then
        do ImGui.text("(all clips added)")

// The actor's morph slots: the morphs it is weighing, each with a weight, and the controls
// to add and remove them (spec 037, FR-009 to FR-011). A direct write to the runtime renderer
// — no command, nothing persisted — through the same `ActorRender.setMorphWeight` a gameplay
// caller uses. Only called for an actor, so no other kind of selection shows the section.
let private drawMorphs (zone: Zone.Zone) (ref: EntityRef) : unit =
    let actor = &zone.Actors.Get(handleOf ref)
    let span = actor.Renderer.MorphSlots.Slots

    if not (ImGui.collapsingHeader("Morphs", 0)) then () else

    match actor.Renderer.Asset.Morphs with
    | None -> do ImGui.text("(asset has no morphs)")
    | Some morphs ->

    // ── held morphs: a row each, with a weight drag and a remove ──
    let mutable held = 0
    for i in 0 .. span.Length - 1 do
        let slot = span.[i]
        if slot.Alive then
            held <- held + 1
            let name = morphs.Names.[slot.Value.MorphIndex]
            do ImGui.pushId name
            do ImGui.text name
            do ImGui.sameLine(0.0f, 8.0f)
            let mutable weight = slot.Value.Weight
            if ImGui.dragFloat("Weight", &weight, 0.01f, 0.0f, 1.0f, "%.2f", 0) then
                do ActorRender.setMorphWeight &actor.Renderer name weight
            do ImGui.sameLine(0.0f, 8.0f)
            if ImGui.smallButton "Remove" then
                do ActorRender.removeMorph &actor.Renderer name
            do ImGui.popId()

    if held = 0 then
        do ImGui.text("(holds no morphs)")

    if held >= Gpu.MorphsMax then
        do ImGui.text("(maximum 8 morphs)")
    else

    // ── add: a button per morph the asset carries and the actor does not hold ──
    let mutable addable = 0
    for m in 0 .. morphs.Count - 1 do
        let mutable already = false
        for i in 0 .. span.Length - 1 do
            let slot = span.[i]
            if slot.Alive && slot.Value.MorphIndex = m then already <- true
        if not already then
            addable <- addable + 1
            if ImGui.smallButton morphs.Names.[m] then
                do ActorRender.setMorphWeight &actor.Renderer morphs.Names.[m] 0.0f

    if addable = 0 then
        do ImGui.text("(all morphs added)")

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
        // One buffer serves every selected entity, and `NameEditRef` records the entity its
        // text belongs to. The buffer is re-seeded — which is also what re-points that ref —
        // only while the field is *not* live: overwriting a field that is being edited would
        // both lose the edit and hand the commit below the newly selected entity's name, to be
        // applied to the old one. The panels draw outliner-first, so changing the selection
        // with the field live is one click away, and that ordering is what used to rename the
        // previous selection to the new one's name.
        if not state.Editor.NameEditing then
            do seedName state.Editor.NameBuffer e.Name
            state.Editor <- { state.Editor with NameEditRef = Some ref }

        let submitted =
            ImGui.inputText("Name", state.Editor.NameBuffer, unativeint state.Editor.NameBuffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero)
        let finished = submitted || ImGui.isItemDeactivatedAfterEdit()
        // The commit goes to the entity the buffer's text belongs to — `NameEditRef`, the one it
        // was typed for, which is not always the selected one.
        match Selection.renameTarget state.Editor.NameEditRef finished (bufferText state.Editor.NameBuffer) with
        | Some (target, name) -> Selection.renameRef shared &state target name
        | None -> ()
        state.Editor <- { state.Editor with NameEditing = ImGui.isItemActive() }

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

            let mutable radius = trigger.State.Radius
            do ImGui.dragFloat("Radius", &radius, 0.05f, 0.0f, 1000.0f, "%.2f", 0) |> ignore
            if ImGui.isItemDeactivatedAfterEdit() then
                Selection.updateTriggerRef shared &state ref (fun t -> { t with Radius = radius })

            let mutable height = trigger.State.Height
            do ImGui.dragFloat("Height", &height, 0.05f, 0.0f, 1000.0f, "%.2f", 0) |> ignore
            if ImGui.isItemDeactivatedAfterEdit() then
                Selection.updateTriggerRef shared &state ref (fun t -> { t with Height = height })

            let mutable interactable = trigger.State.Interactable
            if ImGui.checkbox("Interactable", &interactable) then
                Selection.updateTriggerRef shared &state ref (fun t -> { t with Interactable = interactable })

            // The prompt commits like the entity name and has the same seed rule: it is
            // re-seeded, and re-pointed at the selected trigger, only while it is not live.
            if not state.Editor.PromptEditing then
                do seedName state.Editor.PromptBuffer trigger.State.Prompt
                state.Editor <- { state.Editor with PromptEditRef = Some ref }

            let submitted =
                ImGui.inputText("Prompt", state.Editor.PromptBuffer, unativeint state.Editor.PromptBuffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero)
            let finished = submitted || ImGui.isItemDeactivatedAfterEdit()
            match Selection.renameTarget state.Editor.PromptEditRef finished (bufferText state.Editor.PromptBuffer) with
            | Some (owner, prompt) -> Selection.updateTriggerRef shared &state owner (fun t -> { t with Prompt = prompt })
            | None -> ()
            state.Editor <- { state.Editor with PromptEditing = ImGui.isItemActive() }

            // The enabled flag is the shared entity one: for a trigger it means it can
            // detect the player.
            let mutable enabled = trigger.State.Base.Enabled
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

            do drawAnimations zone ref
            do drawMorphs zone ref
    | None ->
        do ImGui.text("(no selection)")

    do ImGui.endWindow()
