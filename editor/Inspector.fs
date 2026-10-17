module Inspector

// Right panel: the single editing surface for the selection. The name is editable
// — committed on Enter or on leaving the field, as one undoable step — the transform
// is shown read-only, and actors additionally expose a toggle to visualize their
// capsule collider.

open System.Numerics
open Entity

// The Inspector's transform readout, formatted only when the transform changes: a selection that is not being
// moved reuses the three strings instead of rebuilding them every frame.
let mutable private transformCache : Transform voption = ValueNone
let mutable private transformText : string * string * string = "", "", ""

let private transformLines (t: Transform) : string * string * string =
    match transformCache with
    | ValueSome cached when cached = t -> transformText
    | _ ->

    let p = t.Position
    let r = eulerDegrees t.Rotation
    let s = t.Scale
    transformText <-
        sprintf "Position: %.2f, %.2f, %.2f" p.X p.Y p.Z,
        sprintf "Rotation: %.1f, %.1f, %.1f" r.X r.Y r.Z,
        sprintf "Scale: %.2f, %.2f, %.2f" s.X s.Y s.Z
    transformCache <- ValueSome t
    transformText

// One committed text field: re-seeded, and re-pointed at `ref`, only while it is not live; committed once the
// widget is submitted or left. Returns the owner the text belongs to, the commit (if any), and liveness.
let private committedText
    (label: string)
    (buffer: byte[])
    (maxChars: int)
    (owner: EntityRef option)
    (editing: bool)
    (ref: EntityRef)
    (seed: string)
    : EntityRef option * (EntityRef * string) option * bool =
    let owner =
        if editing then owner
        else
            do ImGui.seedText buffer maxChars seed
            Some ref

    let submitted =
        ImGui.inputText(label, buffer, unativeint buffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero)
    let finished = submitted || ImGui.isItemDeactivatedAfterEdit()
    owner, Selection.renameTarget owner finished (ImGui.bufferText buffer), ImGui.isItemActive()

// One colour picker over a reused buffer, seeded from the authored colour while the picker is not live so a drag
// is never overwritten mid-gesture. Returns `(committed, live)` for the caller's own commit and state write.
let private colourPicker (label: string) (buffer: float32[]) (live: bool) (colour: Vector3) : bool * bool =
    if not live then
        buffer.[0] <- colour.X
        buffer.[1] <- colour.Y
        buffer.[2] <- colour.Z
    do ImGui.colorEdit3(label, buffer, 0) |> ignore
    ImGui.isItemDeactivatedAfterEdit(), ImGui.isItemActive()

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
            do ImGui.textUnformatted anim.Name
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
            do ImGui.textUnformatted name
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

// The active zone's environment (a zone has at most one): its name, read-only, shown above the
// zone's lighting. Both are the zone's own settings and the Inspector draws them together when
// nothing is selected, the environment saying where the scene comes from and the lighting how it is
// lit. The Outliner lists places and entities, not zone settings, so the readout lives here.
let private drawEnvironment (zone: Zone.Zone) : unit =
    let name =
        match zone.Environment with
        | None -> "(none)"
        | Some environment -> environment.Base.Name
    do ImGui.textUnformatted (sprintf "Environment: %s" name)

// The active zone's lighting (spec 043, FR-003): the Inspector's fallback subject, drawn when no
// entity is selected. Each control commits one `SetLight` step when it is released or left — the
// rule the trigger fields below use — so tuning the sun is reversible (FR-006) without putting a
// step on the stack per frame.
let private drawLighting (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (zone: Zone.Zone) : unit =
    if not (ImGui.collapsingHeader("Lighting", 0)) then () else

    // ── the sun's angles: bounded, so the derived direction is never degenerate (FR-009) ──
    let mutable pitch = zone.Light.Pitch
    do ImGui.dragFloat("Sun Pitch", &pitch, 0.5f, 10.0f, 90.0f, "%.1f", 0) |> ignore
    if ImGui.isItemDeactivatedAfterEdit() then
        do Selection.setLight shared &state (fun light -> { light with Pitch = pitch })

    let mutable yaw = zone.Light.Yaw
    do ImGui.dragFloat("Sun Yaw", &yaw, 1.0f, 0.0f, 360.0f, "%.1f", 0) |> ignore
    if ImGui.isItemDeactivatedAfterEdit() then
        do Selection.setLight shared &state (fun light -> { light with Yaw = yaw })

    // ── the light colour: a shared picker over the reused buffer ──
    let committed, live = colourPicker "Light Colour" state.Editor.LightColorBuffer state.Editor.LightColorEditing zone.Light.Color
    if committed then
        let buffer = state.Editor.LightColorBuffer
        do Selection.setLight shared &state (fun light -> { light with Color = Vector3(buffer.[0], buffer.[1], buffer.[2]) })
    state.Editor <- { state.Editor with LightColorEditing = live }

    let mutable ambient = zone.Light.Ambient
    do ImGui.dragFloat("Ambient", &ambient, 0.01f, 0.1f, 1.5f, "%.2f", 0) |> ignore
    if ImGui.isItemDeactivatedAfterEdit() then
        do Selection.setLight shared &state (fun light -> { light with Ambient = ambient })

// The active zone's sky (spec 092, FR-009): the Inspector's fallback subject, beside the lighting. Each
// control commits one `SetSkydome` step when it is toggled or released, as the light's controls do; the
// colour pickers use the shared scaffold and its reused buffers.
let private drawSkydome (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (zone: Zone.Zone) : unit =
    if not (ImGui.collapsingHeader("Sky", 0)) then () else

    let mutable enabled = zone.Skydome.Enabled
    if ImGui.checkbox("Enabled", &enabled) then
        do Selection.setSkydome shared &state (fun sky -> { sky with Enabled = enabled })

    let committed, live = colourPicker "Zenith Colour" state.Editor.ZenithColorBuffer state.Editor.ZenithColorEditing zone.Skydome.Zenith
    if committed then
        let buffer = state.Editor.ZenithColorBuffer
        do Selection.setSkydome shared &state (fun sky -> { sky with Zenith = Vector3(buffer.[0], buffer.[1], buffer.[2]) })
    state.Editor <- { state.Editor with ZenithColorEditing = live }

    let committed, live = colourPicker "Horizon Colour" state.Editor.HorizonColorBuffer state.Editor.HorizonColorEditing zone.Skydome.Horizon
    if committed then
        let buffer = state.Editor.HorizonColorBuffer
        do Selection.setSkydome shared &state (fun sky -> { sky with Horizon = Vector3(buffer.[0], buffer.[1], buffer.[2]) })
    state.Editor <- { state.Editor with HorizonColorEditing = live }

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) =
    let visible = ImGui.beginWindow("Inspector", System.IntPtr.Zero, 0)
    if visible then

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
            let owner, commit, editing =
                committedText "Name" state.Editor.NameBuffer EditorState.entityNameMaxChars state.Editor.NameEditRef state.Editor.NameEditing ref e.Name
            match commit with
            | Some (target, name) -> Selection.renameRef shared &state target name
            | None -> ()
            state.Editor <- { state.Editor with NameEditRef = owner; NameEditing = editing }

            let position, rotation, scale = transformLines e.Transform
            do ImGui.textUnformatted position
            do ImGui.textUnformatted rotation
            do ImGui.textUnformatted scale

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
                let owner, commit, editing =
                    committedText "Prompt" state.Editor.PromptBuffer EditorState.entityNameMaxChars state.Editor.PromptEditRef state.Editor.PromptEditing ref trigger.State.Prompt
                match commit with
                | Some (target, prompt) -> Selection.updateTriggerRef shared &state target (fun t -> { t with Prompt = prompt })
                | None -> ()
                state.Editor <- { state.Editor with PromptEditRef = owner; PromptEditing = editing }

                // The enabled flag is the shared entity one: for a trigger it means it can
                // detect the player.
                let mutable enabled = trigger.State.Base.Enabled
                if ImGui.checkbox("Enabled", &enabled) then
                    Selection.updateTriggerRef shared &state ref (fun t -> { t with Base = { t.Base with Enabled = enabled } })

                // The trigger's authored cue (spec 058), edited inline through the shared control. Every
                // accepted edit is one `TriggerSet` step via `Selection.updateTriggerRef`.
                do ImGui.text("Cue")
                do CueEditor.drawTrigger shared &state ref

            // Actor-only: bind this actor as the played character. It is a zone's own edit like the zone's
            // light (spec 045), so it is one undoable step and the control reads the designation back — an
            // undo with nothing to see would not be much of an undo. It still touches no physics.
            if ref.Kind = EntityKind.Actor then
                let actor = Zone.captureActor zone ref

                let mutable show = actor.ShowCollider
                if ImGui.checkbox("Show Collider", &show) then
                    do Zone.setShowCollider zone ref show

                if zone.Player = ValueSome (Systems.playerHandleOf ref) then
                    // Already the player: say so rather than offering the action again.
                    do ImGui.beginDisabled(true)
                    do ImGui.text("This actor is the zone's player")
                    do ImGui.endDisabled()
                elif ImGui.button("Set as Player", ImGui.ImVec2(0.0f, 0.0f)) then
                    do Selection.setPlayer shared &state ref

                do drawAnimations zone ref
                do drawMorphs zone ref
        | None ->
            // No entity selected: the Inspector falls back to the active zone, whose environment and
            // lighting are the zone-level settings it edits (spec 043, FR-003).
            do drawEnvironment zone
            do drawLighting shared &state zone
            do drawSkydome shared &state zone

    do ImGui.endWindow()
