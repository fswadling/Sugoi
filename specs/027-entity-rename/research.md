# Research: Entity Rename

Decisions derived from the feature spec, the existing command/undo and entity
code, the cimgui binding and its vendored ImGui source, and the precedents set by
`specs/024-multiple-zones` (its naming field and its "prevent invalid actions in
the UI" decision) and `specs/026-waypoints` (a name is a label, not an identity).

## Decision 1 — A rename is a new `Rename` undo command

- **Decision**: add one case to `Command`:
  `Rename of Ref : EntityRef * Before : string * After : string`. `apply` writes
  the after-name, `undo` writes the before-name. Nothing else changes in the
  command vocabulary or the undo stack.
- **Rationale**: a rename is a scene edit, so it must be undoable like a
  transform, and the undo stack is already the mechanism. Carrying both names in
  the command makes undo correct without re-reading anything, exactly as
  `Transform` carries both transforms. One command per *committed* rename (Decision
  7) means one undo step per rename with no merge rule: `UndoStack.push` merges
  only consecutive `Transform` commands to the same ref, so a rename naturally
  forms its own step, and typing does not create a step per keystroke.
- **Alternatives considered**: reusing `Transform` by widening it to carry a name
  (rejected — it would make every transform command carry a name it does not use,
  and would let the merge rule fold renames into transforms); a separate rename
  history list (rejected — a second undo mechanism beside the existing one);
  recording the whole entity before/after (rejected — two strings are what is
  needed; the rest is provably unchanged, which `setName` guarantees).

## Decision 2 — `Zone.setName` writes the base name in place; `Entity.Name` stays immutable

- **Decision**: add `setName : zone:Zone -> ref:EntityRef -> name:string -> unit`
  to `world/Zone.fs`, dispatching on the ref's kind over the four stored kinds
  (Actor, Prop, Part, Waypoint) and reassigning the entity's payload as
  `{ p with Base = { p.Base with Name = name } }`, with no physics work and a
  no-op for a stale/absent ref or an unstored kind. `Entity.Name` stays a plain
  `string`, not `mutable`.
- **Rationale**: `setTransform` already establishes this shape — one branch per
  kind, writing the base and leaving other kinds alone — and the payload
  reassignment is what `setTransform` and `setMeshTransform` already do for
  transforms. Since `tryEntityAt` returns a *copy*, a mutable field would not
  remove the need for the per-kind byref write anyway, so mutability would buy
  nothing and would weaken the record (a name could then be changed by any caller
  that holds a payload, bypassing a command). Keeping the write inside `Zone`
  preserves the module's role as the only thing that touches entity storage, which
  is also what makes `renameRef`'s command able to apply through `Command.apply`.
- **Alternatives considered**: making `Name` mutable (rejected — no saving, and it
  invites unrecorded edits); a generic `/` SRTP helper to collapse the four
  branches (rejected — F# has no structural "has a `Base` field" constraint, so it
  would be more code and less legible than the dispatch `setTransform` already
  uses); writing the name through a `Rename`-specific path in the editor (rejected
  — the command's apply/undo must be the single definition of the mutation, or
  undo and the UI would drift).

## Decision 3 — An empty name is refused: prevented in the UI, a precondition in the operation

- **Decision**: the Inspector never calls the rename operation with an empty or
  whitespace-only name (the commit is gated), and `Selection.renameRef` raises
  (`invalidOp`) if it is given one. A committed name is trimmed.
- **Rationale**: this is `specs/024-multiple-zones` Decision 12 applied unchanged —
  an empty name is an expected *user* action, so the fix is to not commit it, and
  once the UI prevents it an invalid call is a programming bug, which the
  constitution says should fail loudly rather than silently return a default. The
  check and the commit happen in the same single-threaded frame, so the gate is
  reliable. Trimming is part of the same rule: a name that differs from what the
  author sees only by whitespace is not a name.
- **Alternatives considered**: treating an empty name as a no-op in the operation
  (rejected — it would make a genuine bug, a caller passing an empty label,
  silent); storing the empty name (rejected — an entity with a blank label cannot
  be identified in the Outliner, and the zones feature already refuses this);
  rejecting whitespace inside a name as well (rejected — spaces are ordinary in a
  label, and only the edges are invisible to the author).

## Decision 4 — Names are not unique, and nothing checks

- **Decision**: no uniqueness validation, no duplicate detection, and no
  auto-suffixing. Duplicating an entity may produce two entities with the same
  name.
- **Rationale**: entities are addressed by `EntityRef` (kind, index, generation),
  which is what selection, undo, commands, and every lookup in this codebase use;
  `Zone.tryEntityAt` resolves a ref, never a name. A name is therefore a label for
  the author, and making it unique would invent a constraint nothing needs and
  that a rename could then violate (raising on a legitimate user action, or
  silently rewriting the name the author typed). `specs/026-waypoints` states the
  same conclusion for waypoints.
- **Alternatives considered**: requiring unique names (rejected — nothing resolves
  by name, and it would turn an ordinary edit into an error case); auto-suffixing
  (`Name 2`) (rejected — it rewrites what the author typed); warning on duplicates
  (rejected — a UI surface for a non-problem).

## Decision 5 — One reused buffer, re-seeded every frame, is safe and correct

- **Decision**: `EditorState` gains one reused `byte[]` for the name field. The
  Inspector encodes the selected entity's current name into it **every frame**,
  calls `inputText`, and reads the buffer back to commit.
- **Rationale**: re-seeding every frame looks like it would fight the user's
  typing, but the vendored ImGui is explicit that it cannot. In
  `native/cimgui/imgui/imgui_widgets.cpp`, `InputTextEx` copies the caller's
  buffer into its internal state only on the frame the field **becomes** active
  ("From the moment we focused we are normally ignoring the content of 'buf'
  (unless we are in read-only mode)"), and the write-back side notes "As soon as
  the InputText() is active, our stored in-widget value gets priority over any
  underlying modification of the user buffer" and "Make sure we always reapply the
  live buffer back to the input/user buffer before clearing ActiveId ... This
  allows the user to use InputText() without maintaining any user-side storage."
  So while the field is active our seeding is ignored, and on the frame it is
  submitted or left the final text is written back into our buffer *before*
  `InputTextEx` returns — which is exactly the value we need to commit. Re-seeding
  every frame is therefore what keeps the field in step with undo/redo,
  re-selection, and a zone switch, with no extra state and no way to disturb a
  typist. It also allocates nothing: the encode writes straight into the buffer
  (Decision 10).
- **Alternatives considered**: seeding only when the selection changes and
  tracking whether the field is active with an extra flag (rejected — two more
  pieces of state to keep in step, to avoid a write that the engine documents as
  harmless); a per-entity buffer keyed by ref (rejected — unbounded state for a
  field that serves one entity at a time); reading the entity's name into a fresh
  array each frame (rejected — the same result with an allocation).

## Decision 6 — A pending edit is attributed to the entity it was typed for

- **Decision**: `EditorState` also records which ref the buffer currently belongs
  to (`NameEditRef : EntityRef option`). On commit, the text is applied to *that*
  ref, not to whatever is selected, and the field then takes ownership of the
  current selection.
- **Rationale**: this is the feature's sharpest hazard, and it is a consequence of
  panel order. `Editor.draw` draws the Outliner **before** the Inspector, so a
  click on another entity's row changes the selection *within the same frame*,
  before the name field is drawn. If the field had focus with uncommitted text,
  that frame would: draw the field for the newly selected entity with the same
  widget id, see the click deactivate it, and receive the *previous* entity's text
  in the buffer — a naive implementation would rename the newly selected entity
  with the old entity's name. Attributing the buffer to the ref it was seeded for
  makes that impossible: the text is committed to the entity it was typed for (or
  dropped if that entity is gone, since `setName` no-ops on a dead ref), and the
  field re-owns the current selection from the next frame. The invariant is
  maintained by setting `NameEditRef` in the same step as the seed, so at the
  start of every frame it names the entity the buffer's text belongs to.
- **Alternatives considered**: reusing the buffer with no attribution, and
  reseeding only when the selection changes (rejected — on a mid-edit selection
  change the engine's in-widget text wins over the seed, so the old entity's text
  would be committed to the new entity); a unique widget label per ref so a
  selection change makes a different field (rejected — it avoids the id continuity
  but not the flush, and leaves the edited text arriving in the buffer with no
  observable signal to attribute it); committing only on Enter and never on focus
  loss (rejected — clicking away is the common gesture, and it would discard the
  edit).

## Decision 7 — Commit on submit or on leaving the field after an edit, never mid-typing

- **Decision**: commit when `inputText` returns true with
  `ImGuiInputTextFlags_EnterReturnsTrue` (Enter), or when
  `isItemDeactivatedAfterEdit()` is true (the field was left after being edited).
  `ImGuiInputTextFlags_AutoSelectAll` is set so that opening the field selects the
  existing name and typing replaces it. Nothing is committed per keystroke.
- **Rationale**: these are ImGui's two signals for "the author finished with this
  value". `IsItemDeactivatedAfterEdit` is specifically "made active, subsequently
  deactivated, and the value was modified in between" — its implementation
  (`ImGui::IsItemDeactivated() && g.DeactivatedItemData.HasBeenEditedBefore`, with
  `HasBeenEditedBefore` set only through `MarkItemEdited`) means that focusing the
  field and clicking away without typing does **not** commit, so no empty undo
  step is created by merely visiting the field. Auto-select-all makes renaming a
  single gesture rather than a careful select-and-replace, and because a mere
  select-all is not an edit, it cannot cause a spurious commit either. Both
  symbols are already exported by the vendored `libcimgui.so` (`igInputText`,
  `igIsItemDeactivatedAfterEdit`; verified with `nm`), so the binding grows by one
  P/Invoke and two flag constants with no native rebuild.
- **Alternatives considered**: committing on any change while typing (rejected —
  one undo step per keystroke); a custom text-editing callback for finer commit
  control (rejected — a callback plus native marshalling for a behaviour the two
  signals already give); committing only on Enter (rejected — clicking away would
  silently discard the edit); free-text `CharsNoBlank` (rejected — spaces are
  ordinary in labels).

## Decision 8 — The rename is a `Selection` operation; the Inspector just calls it

- **Decision**: `Selection.renameRef (shared) (state) (ref) (name)` mirrors
  `deleteRef`: read the entity (no-op when it is gone), build the `Rename` command
  from the current and new names, `Command.apply` it, and push it — leaving the
  selection and drag state alone. The Inspector calls it and nothing else.
  `Inspector.draw` takes `shared` (as `Outliner.draw` and `AssetBrowser.draw`
  already do) so it can reach `Command.apply`; its one call site in
  `Editor.draw` is updated.
- **Rationale**: `Selection` is already "the edits that act on an entity by ref"
  (`deleteRef`, `duplicateRef`), and a rename is the third such edit; keeping it
  there means the apply-then-push pattern has one home instead of being spelled
  out in the Inspector. It also makes the whole data path headless-testable, which
  matters because the Inspector itself is not. Passing `shared` matches the other
  panels and keeps `Command.apply` as the single definition of the mutation.
- **Alternatives considered**: doing the mutate-and-push inline in the Inspector
  (rejected — it would restate what `Command.apply` does for `Rename`, and diverge
  the moment apply changes); putting it in `Spawning.fs` (rejected — that module is
  explicitly the place entities are *created*); a `Clipboard`-style edit module
  (rejected — a new module for one function).

## Decision 9 — Typing must not reach the editor's shortcuts (already true; recorded as a dependency)

- **Decision**: no new code. `Editor.handleShortcuts` already returns early while
  `ImGui.wantTextInput()` is true, so while the name field has focus the tool
  keys, snapping, focus, delete, duplicate, nudge, undo/redo, and the
  backtick/Escape transitions are all inert.
- **Rationale**: without this, a name containing `f`, `3`, `q`, or the Delete key
  would focus the camera, switch tools, toggle snapping, or delete the selected
  entity — a rename feature that could destroy its own subject. The guard exists
  because the Outliner's zone-name field has the same requirement; this feature
  depends on it rather than adding a second guard, and the requirement is stated
  in the spec (FR-012) so it is not silently assumed.
- **Alternatives considered**: a per-widget flag on `EditorState` saying "a text
  field has focus" (rejected — a second source of truth for what ImGui already
  reports).

## Decision 10 — Buffer sizing: 256 bytes with a 63-character limit

- **Decision**: the name buffer is 256 bytes and the field accepts at most 63
  characters. Seeding encodes directly into the buffer:
  `Encoding.UTF8.GetBytes(name, 0, min(name.Length, maxChars), buf, 0)` and then
  terminates it at the returned count.
- **Rationale**: the buffer must hold a NUL-terminated UTF-8 string and ImGui
  takes its size in bytes, while F# strings are UTF-16 — so a byte-sized buffer
  with a character-based limit has to be slack enough for UTF-8's worst case (3
  bytes for a BMP character, and a surrogate pair is 2 UTF-16 units for 4 bytes).
  256 bytes for 63 characters covers the 3-bytes-per-unit worst case (189) with
  room to spare, which means seeding never needs to clamp mid-string and never
  produces an invalid partial character, and `GetBytes(…, 0, charCount, buf, 0)`
  writes straight into the buffer, so seeding allocates nothing. Clamping the
  *character* count is what makes truncation safe; clamping a byte count would
  split a multi-byte character.
- **Alternatives considered**: a 64-byte buffer with `GetBytes(name)` into a
  temporary array and a byte-wise copy (rejected — allocates per frame, and can
  split a character); a byte-count clamp (rejected — it can split a character and
  produce a replacement character on commit); no limit at all (rejected — ImGui
  needs a fixed buffer size, and an unbounded name has no UI meaning).
- **Note**: the Outliner's zone-name field keeps its own 64-byte buffer and
  `zoneNameBufferSize`; this feature does not touch it. Its decode helper
  (`zoneName`, "read up to the first NUL") is duplicated in the Inspector rather
  than extracted, because two call sites is below the project's rule of three.

## No unresolved clarifications

Every choice here is settled by the spec, the existing command/entity/editor code,
or the vendored ImGui source. The two questions an author might have expected to be
asked — whether an empty name is allowed and whether names must be unique — are
answered "no" and "no" on the strength of existing project precedent (Decisions 3
and 4) rather than left open, so no `NEEDS CLARIFICATION` items carry into design.
