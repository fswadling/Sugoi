# Data Model: The Cast — a Roster Hoisted from the Engine and Bound to Actor Assets

Three things are being kept apart that were previously two: **who** a character is (the hoisted
roster), **which look** of her the game has art for (the look vocabulary), and **which asset**
plays that look (the binding). The engine sees only the first; the game sees all three.

```mermaid
graph TD
    R["cast/Roster.fs<br/>Character — fieldless, closed, four cases"] -->|"the engine names characters"| N["narrative/<br/>Event, Fact,
    R -->|"the game names characters"| S["Casting.Slot<br/>= member + look"]
    L["world/Casting.fs<br/>Look — shared, closed"] --> S
    S --> B["Cast.Bindings : Dictionary&lt;Slot, string&gt;<br/>sparse: an entry is the existence check"]
    B -->|"resolves to an id"| A["Assets.Actors<br/>a discovered actor asset"]
    B --> P["editor/CastPanel.fs<br/>the tree the author edits"]
```

## The hoisted roster — `cast/Roster.fs`

```fsharp
module Roster

/// A member of the cast: everyone who can exist in this story. Fieldless and closed, so a
/// character is a value rather than a name, matching on one is exhaustive, and a misspelling
/// is not a character. The engine keys its constructs on this type and the game binds it to a
/// model, which is why it is declared here rather than in either of them (spec 032).
type Character =
    | Aiko | Akane | Shinji | Yuki

/// The renderable name of a member — the case name, so canonical text, the panel and the code
/// all spell a character the same way. A match rather than `string`/`ToString`, for the reason
/// the vocabulary's renderers are matches (research D30).
let characterName (character: Character) : string =
    match character with
    | Aiko -> nameof Aiko
    | Akane -> nameof Akane
    ...

/// Everyone in the roster, in declaration order, enumerated from the type rather than listed
/// again so a case cannot be left out of it — which is what the panel walks.
let characters : Character list = ...
```

Moved out of `narrative/Vocabulary.fs` unchanged in behaviour, doc comment rewritten to say why
it is here. **`Roster` may not reference the assets, entity, rendering, or physics modules** —
that restriction is the whole reason the file exists separately from `world/Casting.fs`, and it
is what FR-004 protects.

## The look vocabulary and the slot — `world/Casting.fs`

```fsharp
module Casting

/// Which look of a character a slot names: one of the visual variants the game has art for.
/// A closed union so a look is a value and not a string, and shared by every member, because
/// whether a member has a look is a fact about the art rather than about her type.
[<RequireQualifiedAccess>]
type Look =
    | SchoolUniform
    | Casual

/// The renderable name of a look, matched over the cases for the same reason `characterName`
/// is rather than formatted.
let lookName (look: Look) : string = ...

/// Every look, enumerated from the type so a case cannot be left out of the panel. Computed
/// once, as `Vocabulary.characters` is, so drawing the panel reflects nothing per frame.
let allLooks : Look list = ...

/// The unit an actor asset is assigned to: one member and one look.
[<Struct>]
type Slot =
    { Character : Character       // Roster.Character
      Look : Look }

/// Which actor asset plays which slot. Sparse deliberately: an absent entry is the statement
/// that no model exists for that slot, and is therefore also the existence check.
type Cast =
    { Bindings : Dictionary<Slot, string> }

/// The binding's question: the asset id for a slot, or nothing when the member has no model
/// for that look. An expected absence, so an `option` rather than a sentinel.
let tryAsset (slot: Slot) (cast: Cast) : string option = ...
```

- The map is keyed by a struct value with structural equality, so a slot is a value and not a
  string — nothing about a member's identity goes through text.
- Two members may hold the same asset id: a stand-in model is a legitimate assignment, and the
  map does not forbid it.
- `Look`'s cases are the seed content the discussion named (Akane's uniform and casual models).
  Adding a look is one case, after which `lookName`'s match is a compile error until it is named
  and `allLooks` reports it with no edit.

## Where the binding lives — `state/SharedState.fs`

```fsharp
type SharedState =
    { mutable Gpu : Gpu.Gpu
      Assets : Assets.AssetManager
      Cast : Casting.Cast              // added: session-scoped authoring state, beside Assets
      Physics : Physics.World }
```

The binding sits beside the loaded assets because that is what it refers to, and because both
are cross-screen, session-scoped values. It is **not** on `EditorState` (which is transient UI
state) and **not** in any zone (it outlives every zone).

| Where the binding can be written | How |
|---|---|
| `CastPanel` assigning a slot | `shared.Cast.Bindings.[slot] <- assetId` |
| `CastPanel` clearing a slot | `shared.Cast.Bindings.Remove slot \|> ignore` |
| every zone's `UndoStack` | **never** — an assignment is not a scene edit (FR-011) |

## Resolution and its outcomes

| Situation | Outcome |
|---|---|
| slot has an entry, and the id names a loaded asset | the panel shows `Assets.Actors.[id].Name`; a later consumer uses `Assets.Actors.[id]` |
| slot has no entry | `tryAsset` answers `None`; the panel draws the leaf as unassigned (FR-010) |
| the panel offers what may be assigned | only `Assets.Actors` entries are listed, so FR-008 holds by construction |
| an entry's id names no loaded asset | raises at the read — a bug to report, not a row to skip (Decision 10) |
| two members bound to one asset | both resolve to it; not a conflict |

## The panel's data, and what it does not hold

- The panel holds **no actor-asset selection of its own**: a slot's current value is read from
  the binding each frame, and the combo's current item is that value. There is therefore no
  selection state to keep in step with the map, and no new `EditorState` field.
- The panel holds **no roster or look list of its own**: it walks `Roster.characters` and
  `Casting.allLooks`.
- Editor state added by this feature: none. `Editor.panelNames` gains the panel's name for
  Ctrl+Tab cycling.

## Relationships and compile order

- `cast/Roster.fs` is **first** in `fsharp-game.fsproj`, and consumes nothing.
- `narrative/*` is second: it now consumes `Roster` instead of declaring the roster. Every other
  dependency of the engine is unchanged, and it still consumes nothing from the game.
- `world/Casting.fs` consumes `Roster` and `System.Collections.Generic`; it consumes no other
  world module, so it can sit directly after `world/Assets.fs`.
- `state/SharedState.fs` consumes `Casting`; `editor/CastPanel.fs` consumes `Casting`,
  `SharedState`, `GameplayState` and `ImGui`, and is compiled immediately before
  `editor/Editor.fs`.
- `test/{RosterTests,CastingTests}.fs` consume the two new modules and need no graphics device,
  so they run headless with the rest of the suite.

## Validation rules

- The roster has exactly one declaration in the solution (SC-001); no file lists member names a
  second time.
- `allLooks` and `lookName` agree over every case, and adding a look reports both (FR-005).
- A member has a look if and only if the binding has an entry for the slot (FR-006).
- Only an id drawn from `Assets.Actors` can be written into the binding (FR-008).
- No assignment path reaches an `UndoStack` (FR-011).
- The engine's facts, rules and history are unaffected by any change to the binding (FR-012).
- The engine's sources compile with no reference to `Assets`, `Casting`, `Zone`, or any render
  or physics module (FR-004).
