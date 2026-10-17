module SongPanel

// The Songs panel (spec 087): the editor's music bed. It offers the project's music assets
// (`Assets.musicNames`), lets the director add them to a global mix — each row playing its own streaming
// deck — and gives every row transport, a scrub, a weight and a loop toggle over one master. The authored
// mix (the row set, each weight and loop flag, and the master) is a `SetMix` step on the one history; the
// transport and the scrub are runtime and never touch it (FR-010/FR-011).
//
// The controls are ASCII-labelled: the ImGui default font carries no transport glyphs, so `▶`/`⏸`/`⏹`
// would draw as `?`. The bed transport and each row's transport share the words Play/Pause/Stop.

open Command

// ── opt-in diagnostics ──
//
// A per-action line (`FFS_SONG_DEBUG=1`), mirroring `FFS_AUDIO_DEBUG` (spec 086): it reports the pick, the
// Add click, and the commit, so a wired-up-but-silent button stays debuggable.
let private songDebug =
    match System.Environment.GetEnvironmentVariable "FFS_SONG_DEBUG" with
    | null -> false
    | v -> v <> "" && v <> "0"

let private log (message: string) : unit =
    if songDebug then do printfn "song-panel: %s" message

// ── the write path ──

/// Write one `SetMix` step on the one history. `edit` is applied to the **current** mix at commit time,
/// never to the panel's draw-time snapshot, so an edit made by one control cannot be clobbered by another
/// control drawing later in the same frame.
let private commit (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (edit: Music.Mix -> Music.Mix) : unit =
    let before = shared.Mix
    do Undo.commit shared &state (SetMix (before, edit before))

/// Play a row: resolve its path from the asset manager, and only when its asset is a playable reference —
/// a failed or absent asset is a quiet no-op, so an unavailable track never takes the panel down (FR-013).
let private playRow (shared: SharedState.SharedState) (song: string) : unit =
    if shared.Assets.Audio.Mixer = System.IntPtr.Zero then () else

    let asset = Assets.audioAsset shared.Assets song
    if not (AudioAsset.isReference asset) then () else

    Music.play shared.Assets.Audio shared.Mix shared.Rack song asset.Path

/// A weight/master slider. A completed drag records one `SetMix` (FR-010); while the slider is held the
/// rack is reconciled with the dragged value, so the mix is heard as it is balanced, not only on release.
let private mixSlider
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (label: string)
    (value: float32)
    (apply: float32 -> Music.Mix -> Music.Mix) : unit =
    let mutable v = value
    do ImGui.sliderFloat(label, &v, 0.0f, 1.0f, "%.2f", 0) |> ignore
    // Live preview while held; one `SetMix` on release, only when the value actually moved (the guard
    // matters: `isItemDeactivatedAfterEdit` can report true with no edit, and an unguarded commit would
    // push a phantom step — and, built from a stale snapshot, revert another control's change).
    if ImGui.isItemActive() then Music.reconcile shared.Assets.Audio shared.Rack (apply v shared.Mix)
    if ImGui.isItemDeactivatedAfterEdit() && v <> value then do commit shared &state (apply v)

// ── the panel ──

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    let visible = ImGui.beginWindow("Songs", System.IntPtr.Zero, 0)
    if visible then

        let mix = shared.Mix
        let rack = shared.Rack
        let names = Assets.musicNames shared.Assets

        // ── the picker: choose a music asset, then Add (one `SetMix` step, FR-001). A pick closes the
        // dropdown, so the Add button beside it is reachable — an open combo popup otherwise sits over it. ──
        let pick = state.Editor.SongPick
        let preview = if pick = "" then "(choose a song)" else pick
        if ImGui.beginCombo("Song", preview, 0) then
            for name in names do
                if ImGui.selectable name (name = pick) then
                    state.Editor <- { state.Editor with SongPick = name }
                    do ImGui.closeCurrentPopup()
                    do log (sprintf "picked '%s'" name)
            do ImGui.endCombo()
        do ImGui.sameLine(0.0f, -1.0f)
        let addable = pick <> "" && not (Music.contains pick mix)
        do ImGui.beginDisabled(not addable)
        let pressed = ImGui.smallButton "Add"
        do ImGui.endDisabled()
        if pressed then do log (sprintf "Add clicked pick='%s' addable=%b" pick addable)
        if pressed && addable then
            do commit shared &state (Music.addRow pick)
            do log (sprintf "committed add '%s'; rows now %d" pick (List.length shared.Mix.Rows))

        // ── the bed transport (FR-004): play every row from its own position, or hold every row ──
        do ImGui.separator()
        if ImGui.smallButton "Play all" then
            // Start only the rows that are not already playing, so a running row keeps its position (FR-004):
            // a paused row resumes, a stopped one starts from 0, a playing one is left running.
            for row in mix.Rows do
                if not (Music.isPlaying rack row.Asset) then playRow shared row.Asset
        do ImGui.sameLine(0.0f, 8.0f)
        if ImGui.smallButton "Hold all" then
            for row in mix.Rows do Music.pause rack row.Asset
        do ImGui.sameLine(0.0f, 8.0f)
        mixSlider shared &state "Master" mix.Master Music.setMaster

        // ── one row per song ──
        do ImGui.separator()
        if List.isEmpty mix.Rows then do ImGui.text "(no songs)"

        for row in mix.Rows do
            do ImGui.pushId row.Asset

            do ImGui.textUnformatted row.Asset

            // transport (runtime; never a step, FR-003/FR-011)
            do ImGui.sameLine(0.0f, 8.0f)
            if ImGui.smallButton "Play" then playRow shared row.Asset
            do ImGui.sameLine(0.0f, 4.0f)
            if ImGui.smallButton "Pause" then Music.pause rack row.Asset
            do ImGui.sameLine(0.0f, 4.0f)
            if ImGui.smallButton "Stop" then Music.stop rack row.Asset

            // the scrub (runtime, FR-008): spans the duration, reflects the position, seeks when dragged. It is
            // offered only once the row has a deck (the duration is unknown before the first play).
            let total = Music.duration rack row.Asset
            if total > 0.0f then
                let mutable position = Music.position rack row.Asset
                if ImGui.sliderFloat("##scrub", &position, 0.0f, total, "%.1fs", 0) then
                    do Music.seek rack row.Asset position
            else
                do ImGui.text "(play to scrub)"

            // the weight and the loop flag (authored; one step per completed edit, FR-006/FR-009)
            mixSlider shared &state "Weight" row.Weight (Music.setWeight row.Asset)
            let mutable loop = row.Loop
            if ImGui.checkbox("Loop", &loop) then
                do commit shared &state (Music.setLoop row.Asset loop)

            do ImGui.sameLine(0.0f, 8.0f)
            if ImGui.smallButton "Remove" then
                do commit shared &state (Music.removeRow row.Asset)

            do ImGui.popId()

    do ImGui.endWindow()
