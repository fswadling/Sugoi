# Contract: Cast and Binding

Public surface added or changed by this feature. Signatures are illustrative F#. The change is
that the roster is declared once outside the engine, and the game binds a member and a look to a
discovered actor asset.

## Roster — `cast/Roster.fs` (new, compiled first)

```fsharp
module Roster

// Moved out of `narrative/Vocabulary.fs`, unchanged in behaviour.
type Character =                       // fieldless, closed, four cases
    | Aiko | Akane | Shinji | Yuki

val characterName : character:Character -> string     // a match over the cases, `nameof`
val characters : Character list                       // enumerated from the type
```

- **Not** `[<RequireQualifiedAccess>]`: the cases are used bare today (`Shinji`) and stay so.
- **Asset-free.** This module may not reference `Casting`, `Assets`, `Zone`, or any render or
  physics module. That is what keeps FR-004 structural rather than conventional.

## Vocabulary — `narrative/Vocabulary.fs`

```fsharp
// Removed: `Character`, `characterName`, `characters` — now `Roster`'s.
// Unchanged: `Trait`, `Event`, `Fact`, `FactKind`, `traitName`, `factParticipants`,
//            `factKind`, `sortParticipants`.
```

- Every file that named a character gains `open Roster` (`ParticipantIndex`, `Canonical`, `Facts`,
  `Rules`, `Engine`, `Harness` as the compiler reports them); `Canonical`'s four character
  renderings become `Roster.characterName`.
- No alias or re-export is left in `narrative/`: the engine should read as consuming a roster it
  does not own, and deleting the members is what enumerates the sites (Decision 8).
- `narrative/Units/Cast.fs` is unchanged. The name is unaffected by this feature, since the
  game's module is `Casting`; renaming that unit to `Traits.fs` — which is what it holds — is a
  tidy for a later change, not this one.

## Casting — `world/Casting.fs` (new)

```fsharp
module Casting

[<RequireQualifiedAccess>]
type Look =
    | SchoolUniform
    | Casual

val lookName : look:Look -> string          // a match over the cases, `nameof`
val allLooks : Look list                    // enumerated from the type, once

[<Struct>]
type Slot =
    { Character : Roster.Character
      Look : Look }

type Cast =
    { Bindings : Dictionary<Slot, string> }   // slot -> actor asset id; sparse by design

val tryAsset : slot:Slot -> cast:Cast -> string option
```

- `lookName` and `allLooks` are the look vocabulary's two readers: a **match** so adding a case
  is a compile error until it is named, and an **enumeration** so adding a case reaches the panel
  with no edit. Neither is called per frame beyond the leaves drawn.
- No `create`, `assign`, or `clear`: each would have exactly one call site. `SharedState.create`
  constructs the record inline, and the panel writes `Bindings.[slot] <- id` /
  `Bindings.Remove slot |> ignore` directly (Decision 9).
- `tryAsset` is the binding's question, and it is what a later consumer — placement, the
  simulation, a cue — will call.

## SharedState — `state/SharedState.fs`

```fsharp
type SharedState =
    { mutable Gpu : Gpu.Gpu
      Assets : Assets.AssetManager
      Cast : Casting.Cast             // added
      Physics : Physics.World }
```

- `Casting.Cast` holds a mutable dictionary, so the record itself need not be a `mutable` field;
  entries are added and removed in place.
- The binding is session state beside the assets it refers to. It is not in `EditorState` and not
  in any zone, and no code pushes it onto an `UndoStack` (FR-011).

## CastPanel — `editor/CastPanel.fs` (new)

```fsharp
module CastPanel

// The same shape as `AssetBrowser.draw` and `Outliner.draw`.
val draw : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> unit
```

- Opens an ImGui window named `Cast`, docked as a tab in the **left** node beside the Outliner,
  as `docs/EDITOR_DESIGN.md`'s Workspace table specifies. Per member in `Roster.characters`, a
  collapsing header labelled with `Roster.characterName` (closed by default); under it, one row
  per `Look` in `Casting.allLooks` showing `Casting.lookName` and a combo of
  `shared.Assets.Actors` sorted by id, plus a `(none)` item that clears the slot.
- It **assigns in place** rather than routing through the Inspector, which is
  `docs/EDITOR_DESIGN.md` D-022: a slot is not a selectable item in the one-selection model, and
  there are eight of them.
- `igPushID_Str` is pushed with the member's name for each header, so the repeated look labels are
  distinct; `igPopID` balances it.
- The combo's preview is the assigned asset's `Name`, or `(none)` when `Casting.tryAsset` answers
  `None`. Only what the combo lists can be written, so a slot cannot name an asset that does not
  exist (FR-008).
- Draws nothing else, and its only writes are to `shared.Cast` — **superseded on this line by
  `specs/033-cast-spawn`**, which gives a bound slot a Spawn control: the panel now also creates an
  actor, through `Spawning`, and records the command that reverses it. The rest of this section
  stands unchanged: the panel assigns in place, and assigning is still a direct write rather than an
  undoable command.

## Editor — `editor/Editor.fs`

```fsharp
let private panelNames = [| "Outliner"; "Inspector"; "Asset Browser"; "Cast" |]
// and, in `draw`, after `AssetBrowser.draw`:
do CastPanel.draw shared &state
```

## ImGui — `render/ImGui.fs` (added)

Every export below was confirmed present in the shipped `native/libcimgui.so` with `nm -D`, so
these are P/Invokes only — no `.so` change for the widgets.

```fsharp
[<DllImport("cimgui", EntryPoint = "igPushID_Str", CallingConvention = CallingConvention.Cdecl)>]
extern void pushId(string strId)

[<DllImport("cimgui", EntryPoint = "igPopID", CallingConvention = CallingConvention.Cdecl)>]
extern void popId()

[<DllImport("cimgui", EntryPoint = "igBeginCombo", CallingConvention = CallingConvention.Cdecl)>]
extern bool beginCombo(string label, string previewValue, int flags)

[<DllImport("cimgui", EntryPoint = "igEndCombo", CallingConvention = CallingConvention.Cdecl)>]
extern void endCombo()

[<DllImport("cimgui", EntryPoint = "igSelectable_Bool", CallingConvention = CallingConvention.Cdecl)>]
extern bool selectableBool(string label, bool selected, int flags, ImVec2 size)
```

- `igTreeNodeEx_Str` and `igTreePop` are also available and verified, and are **not** needed: a
  collapsing header is a tree node for this panel's purposes and needs no pop.
- Reused as they are: `igCollapsingHeader_TreeNodeFlags`, `igText`, `igSameLine`, `ImVec2`.

## Native — `native/cimgui_sdl3.cpp`

```c
// in cimgui_setup_dock_layout, beside the existing DockBuilderDockWindow calls:
ImGui::DockBuilderDockWindow("Cast", left);   // a tab beside the Outliner
```

- Requires `./native/build.sh` (network + SDL3 headers; `native/cimgui` is not vendored in the
  tree). Nothing else in the feature depends on it: without the line the panel is complete and
  floats at a default position on each run, because layout persistence is deliberately disabled.

## Not changed

- `narrative/`'s constructs, rules, units, facts, history, and every existing test assertion. The
  engine's output is identical (FR-012).
- `world/Assets.fs`, `world/Zone.fs`, `world/Entity.fs`, `state/Command.fs`, and every zone's undo
  history. This feature adds no command, no entity field, and no zone state.
- The player designation, the Outliner, the Inspector, the Asset Browser, and the viewport.
