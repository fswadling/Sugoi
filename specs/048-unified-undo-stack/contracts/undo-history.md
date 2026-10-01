# Contract: One undo history on the gameplay state

The surfaces this feature adds or changes, and what explicitly does not change.

## `state/Command.fs` — CHANGED

**Contract**: three new cases join the union — `ZoneMove of Previous * Next`, `ZoneAdd of Previous * Name`,
`ZoneDelete of Previous * Name * Contents` — where `Contents` is `Zone.ZoneContents`, which lives in
`world/Zone.fs` with the reader and writer that build it (the next section).

Every existing case, the `Batch` gesture, the payload rule ("a payload is the authored record and cannot express a
body, a renderer or a runtime flag") and `UndoStack`'s `create`/`push` (with the transform merge) are unchanged.
`UndoStack.undo`/`redo` **stay** in this module: they step a stack against one zone and need no state, which is
what the zone-level suite drives them with. Only the *state-level* steppers are new, and they live in
`History.fs`.

## `world/Zone.fs` — CHANGED

```fsharp
type ZoneContents = { … }   // NEW — a zone's authored contents: entities by ref, environment, light, player
val captureZone : zone: Zone -> ZoneContents                                        // NEW
val occupyZone  : zone: Zone -> physics: Physics.World -> assets: Assets.AssetManager
                            -> contents: ZoneContents -> unit                       // NEW
```

**Contract**: the record a delete carries, and the two routines that read and write it, live with the zone. A
command's payload is defined where the thing it describes is (`ActorState`, `PropState`, `Environment` in
`world/Entity.fs`; `LightSettings` in `render/Texture.fs`), and `ZoneTemplate` — the same kind of composite,
recorded for a different purpose — is in `world/` too. `world/Zone.fs` is compiled **before** `state/Command.fs`,
so the `ZoneDelete` case can name the record. No zone gains a field: `captureZone`/`occupyZone` are built from the
existing `capture*`/`occupy*` family, which is untouched.

## `state/History.fs` — NEW

```fsharp
val apply       : SharedState.SharedState -> byref<GameplayState.GameplayState> -> Command -> unit
val undo        : SharedState.SharedState -> byref<GameplayState.GameplayState> -> Command -> unit
val commit      : SharedState.SharedState -> byref<GameplayState.GameplayState> -> Command -> unit
val undoHistory : SharedState.SharedState -> byref<GameplayState.GameplayState> -> unit
val redoHistory : SharedState.SharedState -> byref<GameplayState.GameplayState> -> unit
```

**Contract**: the state-level command runner — one command in, carried out on the state. `apply`/`undo` route an
entity or zone-value command to **the active zone** and a zone step to the zone work; no command carries a zone,
because recording the moves is what keeps the active zone the one the step belongs to. `commit` is apply-then-push
(the editor's write path for a zone step); `undoHistory`/`redoHistory` step the one history. The module is
compiled *after* `state/GameplayState.fs` — the project's second use of its one deliberate inversion — because a
zone step creates, removes and switches a zone. `Command` (the type) does **not** move: moving it would be cyclic,
since `EditorState` names `UndoStack`, which names `Command`.

## `state/GameplayState.fs` — CHANGED

```fsharp
UndoByZone : Dictionary<string, UndoStack>          // REMOVED
```

**Contract**: the per-zone side map and the two lines in `enterZone` that parked and loaded a stack are gone; a
zone switch no longer touches the history. `enterZone` keeps dropping the zone-scoped selection/drag/nudge and
re-framing the camera — those are about what is being edited, not about the history. **No new removal function is
added**: `deleteZone` (guard, switch away when active, release, drop) is what a `ZoneDelete` applies and what a
`ZoneAdd`'s undo calls — the last-zone guard cannot re-fire during an undo, because it tests the count before the
removal. `switchZone`, `selectZone`, `addZone` and `deleteZone` keep their signatures and guards; what changes is
that the Outliner reaches the add/delete through `History.commit` so the step is recorded.

## `state/EditorState.fs` — CHANGED IN MEANING ONLY

**Contract**: `Undo : UndoStack` keeps its type and becomes **the** history rather than the active zone's.
`CueHistory` and the cue's four other fields are untouched: the cue keeps its own history (spec 047). No field is
added or removed.

## `editor/Editor.fs` — CHANGED

**Contract**: `undoStep`/`redoStep` call `History.undoHistory`/`redoHistory` instead of `UndoStack.undo`/`redo`
with the active zone. The `Ctrl+Z`/`Ctrl+Y` bindings, the Edit menu and the `idle` gate (not while dragging, not
while a text field has focus) are unchanged.

## `editor/Outliner.fs` — CHANGED

**Contract**: the Zones section's three controls become steps. A row click on a zone that is **not** active
commits a `ZoneMove`; a click on the active zone keeps its current behaviour (clears the Inspector's subject,
records nothing). *Del* commits a `ZoneDelete` whose contents were captured first. *Add Zone* commits a `ZoneAdd`.
The disabled-state gates (empty/duplicate name, last zone) are unchanged, and so is the pending-name buffer.

## `editor/CastPanel.fs`, `editor/ZoneRegistryPanel.fs` — CHANGED (headers only)

**Contract**: the comment that cited the per-zone stacks as the reason the cast binding and the registry are
direct writes is gone — those stacks no longer exist. Each header now gives the reason that stands on its own: a
binding is not a scene edit, and a registration is one click to make again. Neither panel's behaviour changes:
whether they should now *become* undoable is a separate decision (spec Assumptions).

## `fsharp-game.fsproj` — CHANGED

**Contract**: one `Compile` entry — `state/History.fs`, listed **after** `world/CueEngine.fs` and
`state/GameplayState.fs` (it adds, removes and switches zones) and before `editor/Spawning.fs`, the first editor
file. The comment says why, as the cue engine's does.

## `test/GameplayStateTests.fs` — CHANGED, plus new coverage

**Contract**: the `UndoByZone` assertion in the delete test is replaced by an assertion that no history is parked
on a zone. New headless tests cover the feature's own stories: an edit in two zones separated by a move undone in
order with the active zone following (SC-001); an add and its undo leaving the same zones and active zone
(SC-002); a delete and its undo round-tripping a zone's placed prop, waypoint, trigger, environment part and light
at the same refs, plus a zone's contents being asserted to carry its actors and its player designation (SC-003 —
restoring an *actor* needs a renderer, so that half is the quickstart's step 3); and undo through the whole
history returning the project to its starting zones and entities (SC-004). No device, no window.

## `docs/` — CHANGED, and the prior specs — NOT CHANGED

The living design documents move with the feature:

| File | Change |
|---|---|
| `docs/EDITOR_DESIGN.md` | principle 4 (the exceptions list), D-010 (the cue's history), D-011 (the command/undo model), D-022 and D-023 restated for one history |
| `docs/ARCHITECTURE.md` | "the undo history [is] held per zone" corrected |
| `docs/PROJECT_STRUCTURE.md` | `state/History.fs` listed for what it is, and `world/Zone.fs` for the contents record it now owns |
| `editor/CastPanel.fs`, `editor/ZoneRegistryPanel.fs` | headers corrected — they describe current behaviour |
| `docs/ROADMAP.md` | nothing to change: it does not mention the per-zone history |

**The prior specs are not edited.** `specs/024-multiple-zones`, `specs/030-entity-command-families`,
`specs/032-cast-binding`, `specs/043-zone-light-settings` and `specs/045-undoable-player-designation` are historical
artefacts — each records the design at the moment it was written, and editing one afterwards is how the record stops
being trustworthy. The statements this feature supersedes are recorded in [research.md](./research.md) **Decision
10**, in a table of *what was written → what this feature does instead*, together with the list of what is **not**
superseded.

## Explicitly unchanged

- **The per-kind entity commands** — their payloads, `apply` and `undo` bodies, and `Batch` are untouched.
- **`editor/Spawning.fs`, `editor/Selection.fs`, `editor/Inspector.fs`** — they apply a command and push it onto
  `state.Editor.Undo`, which is now the one history; no line changes. (A `commit` helper exists for the zone
  steps; migrating these sites to it would be churn with no behaviour change, so it is not done here.)
- **The cue's history** (`specs/047`) — unchanged; moving the cue onto this history is the next feature.
- **The physics world, the render path, `SharedState`, and `world/Systems.fs`** — untouched.
- **The dialogs' panels, keys and menus** — only the Outliner's Zones section changes where it writes.
