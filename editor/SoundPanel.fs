module SoundPanel

// The Sounds panel (spec 089): the editor's sound-effect tester. It lists every sound asset the asset
// manager discovered (`Assets.soundAssets`) with a Play and a Stop control per row and one Stop all, so
// the director can browse the effects and hear each one. It is a **runtime audition surface**: a play is
// a one-shot on the shared voice pool and never authors anything, records a history step, or persists —
// playing is the whole of its effect (FR-006).
//
// The controls are ASCII-labelled, as the Songs panel's are: the ImGui default font carries no glyphs.
// The panel references `AudioAsset.*` qualified (it does not `open AudioAsset`), so `Sound.play` is
// unambiguously the `Sound` module rather than the `AudioAsset.Sound` union case.

/// Play a sound: only when its decoded buffer is resident — a failed or absent asset is a quiet no-op,
/// so an unavailable sound never takes the panel down (FR-007/FR-008).
let private play (shared: SharedState.SharedState) (asset: AudioAsset.AudioAsset) : unit =
    if AudioAsset.isResident asset then
        do Sound.play shared.Voices asset.Id (AudioAsset.sound asset)

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    let visible = ImGui.beginWindow("Sounds", System.IntPtr.Zero, 0)
    if visible then

        let sounds = Assets.soundAssets shared.Assets

        // ── Stop all: silence every sounding one-shot (FR-005) ──
        do ImGui.beginDisabled(sounds.IsEmpty)
        if ImGui.smallButton "Stop all" then do Sound.stopAll shared.Voices
        do ImGui.endDisabled()
        do ImGui.separator()

        if sounds.IsEmpty then do ImGui.text "(no sounds)"

        for asset in sounds do
            // A unique widget id per row, so two assets that share a Name do not collide.
            do ImGui.pushId asset.Id

            do ImGui.textUnformatted asset.Name
            // The row's state: a resident sound plays; a failed one is listed and marked, and Play is a no-op.
            match asset.Residency with
            | AudioAsset.Failed _ ->
                do ImGui.sameLine(0.0f, 8.0f)
                do ImGui.text "(failed)"
            | _ -> ()

            do ImGui.sameLine(0.0f, 8.0f)
            do ImGui.beginDisabled(not (AudioAsset.isResident asset))
            if ImGui.smallButton "Play" then do play shared asset
            do ImGui.endDisabled()
            do ImGui.sameLine(0.0f, 4.0f)
            if ImGui.smallButton "Stop" then do Sound.stop shared.Voices asset.Id

            do ImGui.popId()

    do ImGui.endWindow()
