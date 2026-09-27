# Phase 0 Research: Interact Prompt

The feature is small, and almost all of it already exists. These decisions settle the presentation —
the only new work — and reference the `odin-game` template the spec names.

## Decision 1 — The prompt is a view module, drawn by the game's 2D layer

- **Decision**: a new `world/PromptRender.fs` holds a pure `layout` (viewport + measured size →
  `Ui.Rect`) and a `draw` (measure the string, lay it out, draw it), and `GameplayScreen.draw` calls
  it. It draws through `render/Ui.fs` (spec 038). It is not inline in the screen, and it is not an
  ImGui window.
- **Rationale**: it is the same shape as `world/DialogueRender.fs` — a view that takes plain values
  and emits `Ui` calls — and it keeps the one non-obvious part (the centring arithmetic) a pure
  function that a headless test can assert, which is what `DialogueRender.layout` does. The prompt is
  game UI, so it belongs on the game's layer, not the editor's toolkit: that is precisely why spec 038
  built the layer ("HUD elements, prompts and menus are future consumers").
- **Alternatives considered**: **inline in `GameplayScreen.draw`** (less code, but the layout is then
  untestable and the screen regrows presentation); **an ImGui window** (what exists today — it reads
  as a debug read-out and would not ship, and it is the editor's toolkit, which its own docs say is not
  for game UI).

## Decision 2 — A bare line, centred near the bottom

- **Decision**: the prompt is a single line of text, centred horizontally, sitting above a
  proportional bottom margin — the `odin-game` look, ported.
- **Rationale**: the reference is the spec's stated model, and the look is deliberate: a prompt is a
  quiet, ambient cue, not a panel. A panel would also compete visually with the dialogue panel, which
  is the layer's other consumer. The reference's numbers (a small font, ~44 px off the bottom,
  centred) become a font the layer already opens and a **proportional** bottom margin so it stays on
  screen at any window size (FR-007).
- **Alternatives considered**: a small panel/badge behind the text (heavier, and closer to a dialogue
  panel than a prompt wants to be); top placement (the reference puts its *exposition* at the top, so a
  top prompt would fight it — and our dialogue is at the bottom, so the bottom is the free strip).

## Decision 3 — Contrast by a shadow, not a panel

- **Decision**: the prompt is drawn twice — a dark, semi-transparent copy offset a pixel or two
  down-right, then the text itself — so it stays legible over bright and dark scene content (FR-008)
  without a background.
- **Rationale**: FR-008 requires readability that does not depend on the scene behind it, and the
  reference's flat grey does not guarantee that. A shadow is the lightest way to get it: the two draws
  share the glyph atlas texture and sampler, so they **coalesce into one run** and cost almost nothing,
  and it preserves the bare-text look. The layer already draws text with an arbitrary colour, so no
  new facility is needed.
- **Alternatives considered**: a translucent dark backing rectangle (robust, but reads as a small
  dialogue panel and needs `Ui.rect` plus a colour); an outline (the shader samples one texture with no
  outline pass, so this would be four or eight draws for no visual gain over a shadow).

## Decision 4 — A third text slot in the layer

- **Decision**: `render/Ui.fs`'s `textSlots` grows from 2 to 3.
- **Rationale**: the layer caches one laid-out string per slot (`TextObjects`/`TextGeoms`/`TextW`/`TextH`
  are parallel arrays of that length), and its one-slot-per-concurrent-string rule is what keeps
  re-measurement out of the frame loop (FR-010). A frame can now show a dialogue's **name plate** (slot
  0), its **line** (slot 1) **and** the prompt — but a prompt is suppressed while a line is shown
  (Decision 5), so do the three ever coexist? The name plate and the line only coexist for a portrait
  line, and the prompt is hidden then; so two slots would suffice *today* by accident. The slot is
  still added because the layer's contract is one slot per kind of string it may draw, and relying on
  "the dialogue hides the prompt so slot 1 is free" would be a hidden coupling between two features
  that happens to hold. Three slots make the capacity honest and keep the two consumers independent.
- **Alternatives considered**: reuse slot 1 for the prompt while a line is hidden (works, but couples
  the prompt to the dialogue's precedence rule — a change to that rule would silently corrupt a slot);
  re-measure per frame (violates FR-010).

## Decision 5 — The dialogue takes precedence over the prompt

- **Decision**: while a dialogue line is shown, the prompt is not drawn; it returns when the line is
  dismissed (FR-012).
- **Rationale**: both are anchored to the bottom of the screen — the dialogue panel is 88 % of the
  width and sits above a 6 % bottom margin, and a prompt sits in that same strip — so they cannot both
  be shown without overdrawing. Rather than lift the dialogue panel to make room (which would reach
  into spec 038's layout and its tests for a case that is rare), the line takes the screen: while the
  player is reading a line, a "you can open this" cue is noise. This is the conventional behaviour and
  it is one condition at the call site.
- **Alternatives considered**: **stack them** — reserve a prompt strip and lift the dialogue panel by
  it (correct and proportional, but it changes `DialogueRender.layout`'s signature, its contract and
  its data model and its tests, to make two widgets share a strip that a line almost never shares with
  a prompt); **draw both and let the prompt sit over the panel** (overdraw — explicitly out per the
  spec's edge case, and unreadable).

## Decision 6 — The prompt is the authored text, read as-is

- **Decision**: the drawn string is exactly `trigger.State.Prompt`. The game adds nothing — no key
  hint, no glyph — and does not transform it (in particular, no `Trim()`).
- **Rationale**: FR-002. There is no interact action or cue engine yet, so the game has nothing true
  to say about *how* to act; the prompt says *what* is here, and the author owns the words. The
  trigger's `Prompt` is already authored in the Inspector (spec 029). No `Trim()` because it would
  allocate a new string every frame on the draw path (FR-010) — and a trailing space is invisible
  anyway.
- **Alternatives considered**: prefixing a key hint ("[E] Open") — invents a control that does not
  exist yet; trimming at draw time — a per-frame allocation; trimming at authoring time — a change to
  the trigger's authoring path, out of scope.

## Decision 7 — The editor's ImGui read-out is removed

- **Decision**: `screen/GameplayScreen.fs`'s `drawTriggerOverlay` (the `Status` window) and its
  `anyInside` helper are deleted, along with the prompt line they print.
- **Rationale**: that overlay exists "as the editor's read-out of trigger detection, standing in for
  the cue a trigger will run once one exists" — the same stand-in this feature replaces, and it draws
  *during play*, so it would put a debug window over the game next to the real prompt. Removing it is
  a reduction in surface and is what the spec's assumption records. The trigger's authored values
  remain visible in the editor where they belong (the Inspector).
- **Alternatives considered**: keep the overlay, drop only its prompt line (leaves an "inside doorway"
  debug window over the game during play, of unclear value); keep it in edit mode only (a new mode
  split for a debug affordance nobody asked to keep).
