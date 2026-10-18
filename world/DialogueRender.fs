module DialogueRender

// The dialogue view: it turns the screen's dialogue state into `Ui` draw calls. This is the
// only module that knows the whole chain — a speaker string, her actor asset (by name, via the
// asset manager), the asset's portraits, the font — and so the only place that maps an `Emotion`
// to a portrait field (FR-005). Compiled after `world/assets/Assets.fs` and `render/Ui.fs`.
//
// The layout arithmetic here is pure (viewport + a measured content height → three rects),
// so it is asserted headlessly (`test/DialogueTests.fs`); the measuring and the drawing need
// the font and a device, so those are the walkthrough.

open System.Numerics

// ── panel layout (pure) ──

/// Every figure is a fraction of the viewport, so the panel and its contents stay on screen
/// at any size (FR-004, SC-003). The panel is a fixed fraction of the viewport in width and
/// content-sized in height.
let private panelWidth (viewportW: float32) : float32 = viewportW * 0.88f
let private sideMargin (viewportW: float32) : float32 = viewportW * 0.06f
let private padX (viewportW: float32) : float32 = viewportW * 0.012f
let private padY (viewportH: float32) : float32 = viewportH * 0.022f
let private bottomMargin (viewportH: float32) : float32 = viewportH * 0.06f
let private topMargin (viewportH: float32) : float32 = viewportH * 0.04f
/// The portrait's column. Reserved whether or not a portrait is actually drawn, so the
/// text's left edge and wrap width do not move between kinds (spec Clarifications Q3).
let private portraitColumn (viewportW: float32) : float32 = viewportW * 0.14f
let private portraitGap (viewportW: float32) : float32 = viewportW * 0.015f

/// The width the text is wrapped to. A function of the viewport alone, because the portrait
/// column is always reserved.
let innerWidth (viewportW: float32) : float32 =
    panelWidth viewportW - 2.0f * padX viewportW - portraitColumn viewportW - portraitGap viewportW

[<Struct>]
type PanelLayout =
    { Panel : Ui.Rect
      Portrait : Ui.Rect
      Text : Ui.Rect }

/// The panel, the portrait and the text box, from the viewport and the measured content height
/// (the name plate and the line together), or `ValueNone` when the line would not fit on screen — the
/// caller reports that and leaves the line undrawn rather than drawing it clipped (FR-004,
/// spec Clarifications Q4). Every rect is in pixels with the origin at the top-left; the bottom
/// edge is anchored above a proportional margin. A `voption` rather than an `option`: the draw path
/// runs every frame, and `Some` on a struct record allocates (FR-010).
let layout (viewportW: float32) (viewportH: float32) (hasPortrait: bool) (textW: float32) (textH: float32) : PanelLayout voption =
    let padY = padY viewportH
    let column = if hasPortrait then portraitColumn viewportW else 0.0f
    let panelH = max textH column + 2.0f * padY
    let panelX = sideMargin viewportW
    let panelY = viewportH - bottomMargin viewportH - panelH
    if panelY < topMargin viewportH then ValueNone
    else

    let panel : Ui.Rect = { X = panelX; Y = panelY; W = panelWidth viewportW; H = panelH }
    let portrait : Ui.Rect = { X = panelX + padX viewportW; Y = panelY + padY; W = column; H = column }
    let text : Ui.Rect =
        { X = panelX + padX viewportW + portraitColumn viewportW + portraitGap viewportW
          Y = panelY + padY
          W = textW
          H = textH }
    ValueSome { Panel = panel; Portrait = portrait; Text = text }

// ── presentation ──

// The name plate sits above the line, inside the same text box; its height is folded into
// the measured content height the layout is given.
let private nameGap = 6.0f
let private borderThickness = 2.0f
let private nameSlot = 0
let private lineSlot = 1

// A thought draws the panel inverted — a light panel with a dark border — so it reads apart
// from a dialogue or portrait line (FR-002, spec Clarifications Q2). Every field is an RGBA
// colour (`W` is alpha): the kind's whole palette, with no geometry in it.
[<Struct>]
type private Colours =
    { Panel : Vector4
      Border : Vector4
      Text : Vector4 }

let private dialogueColours : Colours =
    { Panel = Vector4(0.04f, 0.045f, 0.06f, 0.80f)
      Border = Vector4(1.0f, 1.0f, 1.0f, 0.35f)
      Text = Vector4(0.95f, 0.95f, 0.95f, 1.0f) }

let private thoughtColours : Colours =
    { Panel = Vector4(0.90f, 0.90f, 0.92f, 0.95f)
      Border = Vector4(0.15f, 0.15f, 0.18f, 0.55f)
      Text = Vector4(0.10f, 0.10f, 0.12f, 1.0f) }

// The four edges of a border, so the panel outline is four thin rectangles.
let private drawBorder (ui: byref<Ui.Ui>) (r: Ui.Rect) (c: Vector4) : unit =
    let t = borderThickness
    let top : Ui.Rect = { X = r.X; Y = r.Y; W = r.W; H = t }
    let bottom : Ui.Rect = { X = r.X; Y = r.Y + r.H - t; W = r.W; H = t }
    let left : Ui.Rect = { X = r.X; Y = r.Y; W = t; H = r.H }
    let right : Ui.Rect = { X = r.X + r.W - t; Y = r.Y; W = t; H = r.H }
    do Ui.rect &ui top c
    do Ui.rect &ui bottom c
    do Ui.rect &ui left c
    do Ui.rect &ui right c

/// The speaker's name when the line's kind carries one. A `voption` so the per-frame draw path
/// allocates no `Some` (FR-010).
let private speaker (kind: Dialogue.Kind) : string voption =
    match kind with
    | Dialogue.Kind.PortraitDialogue (speaker, _) -> ValueSome speaker
    | _ -> ValueNone

/// The one place an expression names a portrait field — exhaustive over the closed `Emotion`,
/// so a sixth expression fails to compile here (FR-005).
let portraitOf (emotion: Dialogue.Emotion) (p: ActorAsset.ActorPortraits) : Texture.Texture =
    match emotion with
    | Dialogue.Emotion.Neutral -> p.Neutral
    | Dialogue.Emotion.Happy -> p.Happy
    | Dialogue.Emotion.Sad -> p.Sad
    | Dialogue.Emotion.Angry -> p.Angry
    | Dialogue.Emotion.Surprised -> p.Surprised

/// Why a line was not drawn. A closed set rather than a string, so the once-per-change check is a
/// reference comparison and no message is built on the hot path (FR-010).
type MissReason =
    | NotMissed
    | NoAsset
    | NoPortraits
    | TooTallForViewport

/// The outcome of resolving a *(speaker, expression)* pair. `[<Struct>]` so the draw path allocates
/// nothing — `Result`/`Option` would allocate an object per call (FR-010). The fields are named
/// because a struct union requires same-named fields to share a type.
[<Struct>]
type PortraitResolution =
    | Resolved of texture: Texture.Texture
    | Missed of reason: MissReason

/// Resolve a *(speaker, emotion)* pair to a portrait: the speaker string names an actor asset (its
/// declared `Name`) through the asset manager's index, and the asset carries the set (`specs/074`).
/// Nothing names a portrait or a file. An **expected** miss — the author may name a speaker before her
/// asset exists — so it is a value rather than an exception, and the caller leaves the line undrawn
/// (FR-005, FR-006).
///
/// The lookup is a contains + indexer on the scan-time name index, so it enumerates nothing and adds no
/// per-frame allocation (FR-010); the index resolves a shared name to the lowest `Id`, so the choice is
/// deterministic without any per-frame sorting.
let tryPortrait (assets: Assets.AssetManager) (speaker: string) (emotion: Dialogue.Emotion) : PortraitResolution =
    if not (assets.ActorsByName.ContainsKey speaker) then Missed NoAsset
    else

    match assets.ActorsByName.[speaker].Portraits with
    | Some p -> Resolved (portraitOf emotion p)
    | None -> Missed NoPortraits

// ── whether a line can be drawn ──

/// Whether the kind reserves the portrait column — reserved even when no portrait is drawn, so the
/// text's left edge and wrap width do not move between kinds (spec Clarifications Q3).
let private hasPortraitOf (kind: Dialogue.Kind) : bool =
    match kind with
    | Dialogue.Kind.PortraitDialogue _ -> true
    | _ -> false

/// Measure the line at this viewport: `(wrapped width, name-plate height, content height)`, the
/// content height being the name plate, its gap and the line together. Measured through `ui`'s text
/// slots, which cache by line and wrap width, so a stable line is measured once. Shared by the view
/// and the authoring panel, so both agree on whether a line fits.
let private content (ui: byref<Ui.Ui>) (viewportW: float32) (kind: Dialogue.Kind) (text: string) : struct (float32 * float32 * float32) =
    let wrapWidth = int (max 1.0f (innerWidth viewportW))
    // The name plate is measured unwrapped; two slots, so neither rebuilds the other's glyph geometry.
    let nameH =
        match speaker kind with
        | ValueNone -> 0.0f
        | ValueSome n ->
            let struct (_, nh) = Ui.setLine &ui nameSlot n 0
            float32 nh
    let struct (lineW, lineH) = Ui.setLine &ui lineSlot text wrapWidth
    let gap = if (speaker kind).IsSome then nameGap else 0.0f
    struct (float32 lineW, nameH, nameH + gap + float32 lineH)

/// Why the line is taller than the viewport, or `NotMissed`.
let private heightMiss (ui: byref<Ui.Ui>) (viewportW: float32) (viewportH: float32) (dialogue: Dialogue.Dialogue) : MissReason =
    let struct (lineW, _, contentH) = content &ui viewportW dialogue.Kind dialogue.Text
    match layout viewportW viewportH (hasPortraitOf dialogue.Kind) lineW contentH with
    | ValueNone -> TooTallForViewport
    | ValueSome _ -> NotMissed

/// Why `dialogue` would not draw as it stands, or `NotMissed`: the portrait cannot be resolved, or
/// the line is taller than the viewport. The single place the view and the authoring panel agree, so
/// the panel can explain an absence in the same terms the view left it undrawn (FR-004, FR-005).
/// Expected while authoring, so a value rather than an exception (FR-006, SC-002).
let missReason (assets: Assets.AssetManager) (ui: byref<Ui.Ui>) (viewportW: float32) (viewportH: float32) (dialogue: Dialogue.Dialogue) : MissReason =
    match dialogue.Kind with
    | Dialogue.Kind.PortraitDialogue (speaker, emotion) ->
        match tryPortrait assets speaker emotion with
        | Missed reason -> reason
        | Resolved _ -> heightMiss &ui viewportW viewportH dialogue
    | _ -> heightMiss &ui viewportW viewportH dialogue

/// Draw the shown line over the scene. A no-op with `Shown = false`, so nothing is measured or
/// emitted and the frame is the pre-feature frame (FR-003, SC-001). A line that cannot resolve or
/// does not fit is left undrawn; the authoring panel reports why (`missReason`), so the view is
/// silent and keeps no state.
let draw (assets: Assets.AssetManager) (viewportW: float32) (viewportH: float32) (ui: byref<Ui.Ui>) (dialogue: Dialogue.Dialogue) : unit =
    if not dialogue.Shown then () else

    let colours =
        match dialogue.Kind with
        | Dialogue.Kind.Thought -> thoughtColours
        | _ -> dialogueColours

    let hasPortrait = hasPortraitOf dialogue.Kind

    // Resolve the portrait **before** anything is measured or drawn, so a line that cannot resolve
    // draws nothing at all rather than a half-drawn panel. A miss is expected while authoring
    // (FR-005, FR-006, SC-002). A `voption`, not a mutable local: `ValueNone` is either "this kind
    // draws no portrait" or "the portrait did not resolve", and `hasPortrait` tells them apart —
    // only the second leaves the line undrawn.
    let portrait =
        match dialogue.Kind with
        | Dialogue.Kind.PortraitDialogue (speaker, emotion) ->
            match tryPortrait assets speaker emotion with
            | Resolved texture -> ValueSome texture
            | Missed _ -> ValueNone
        | _ -> ValueNone

    if hasPortrait && portrait.IsNone then () else

    let name = speaker dialogue.Kind
    let struct (lineW, nameH, contentH) = content &ui viewportW dialogue.Kind dialogue.Text
    let nameGeom = ui.TextGeoms.[nameSlot]
    let lineGeom = ui.TextGeoms.[lineSlot]
    let gap = if name.IsSome then nameGap else 0.0f

    // A line too tall for the viewport is left undrawn rather than clipped, and is not a crash: it is
    // reachable by ordinary typing (FR-004, spec Clarifications Q4). The panel reports it.
    match layout viewportW viewportH hasPortrait lineW contentH with
    | ValueNone -> ()
    | ValueSome layout ->

    do Ui.rect &ui layout.Panel colours.Panel
    do drawBorder &ui layout.Panel colours.Border

    // The portrait, when the kind draws one. Already resolved above, never named (FR-005).
    match portrait with
    | ValueSome texture -> do Ui.image &ui layout.Portrait texture Vector4.One
    | ValueNone -> ()

    match name with
    | ValueSome _ ->
        let nameRect : Ui.Rect = { X = layout.Text.X; Y = layout.Text.Y; W = innerWidth viewportW; H = nameH }
        do Ui.text &ui nameRect nameGeom colours.Text
    | ValueNone -> ()

    let lineRect : Ui.Rect =
        { X = layout.Text.X; Y = layout.Text.Y + nameH + gap; W = innerWidth viewportW; H = contentH - nameH - gap }
    do Ui.text &ui lineRect lineGeom colours.Text
