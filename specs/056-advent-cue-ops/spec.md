# Feature Specification: Advent Cue Ops

**Feature Branch**: `056-advent-cue-ops`

**Created**: 2026-10-03

**Status**: Implemented

**Input**: User description: "Add cue ops that read and write the named adventure flags: AddAdvent, RemoveAdvent, Await and If."

## Origin

`specs/055` added the named adventure flags — a closed `Advent` union, a `Set<Advent>` on `SharedState`, an editing
panel and an undo step — as **state and authoring**. Nothing yet lets a *cue* read or write them. This feature adds
the four ops the reference (`odin-game`) has for its advent set, so a milestone can drive a cue rather than only be
ticked by hand: set one, clear one, wait for one, and branch on one.

The reference names a milestone by a fixed-size string; this port carries the **closed union** (`world/Advent.fs`)
instead, for the reason spec 055 gives: a milestone cannot be misspelled and matching on it is exhaustive. That is
the one deliberate departure from the reference's shape.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue sets or clears a milestone (Priority: P1)

A cue contains a "add advent" op naming a milestone. When the cue reaches it, the flag is set (and "remove advent"
clears it). The op costs no time.

**Why this priority**: it is the write half, and the shape with no progress of its own. Without it a cue cannot move
the story forward.

**Independent Test**: start with an empty flag set, run a cue whose only op is `AddAdvent MetAiko`, and assert the set
now contains it and the cue is `Fin` in the same step — no device.

**Acceptance Scenarios**:

1. **Given** an empty flag set, **When** a cue reaches `AddAdvent m`, **Then** the set contains `m` and the op costs
   no time (the cue is at the next op in the same step).
2. **Given** a set containing `m`, **When** a cue reaches `RemoveAdvent m`, **Then** the set no longer contains `m`.
3. **Given** an already-set `m`, **When** a cue reaches `AddAdvent m`, **Then** it is a no-op (the set is unchanged,
   and no error).

---

### User Story 2 - A cue waits for a milestone (Priority: P1)

A cue contains an "await" op naming a milestone. When reached, the cue waits there until the flag is set — by an
earlier op, by a trigger, or by the panel — and then carries on.

**Why this priority**: it is the read half, and it is what makes the flags useful *at runtime* rather than only at
authoring time. It ranks with the write half because a story needs both.

**Independent Test**: put `Await m` in a cue, step it with an empty set (still waiting), then set `m` and step again
(it finishes) — no device.

**Acceptance Scenarios**:

1. **Given** an empty flag set, **When** a cue reaches `Await m`, **Then** the cue stays on that op (nothing after it
   runs) and running it allocates nothing.
2. **Given** a cue waiting on `m`, **When** `m` is set, **Then** the cue moves past the op and runs what follows.
3. **Given** an already-set `m`, **When** a cue reaches `Await m`, **Then** it costs no time (the cue is at the next
   op in the same step).
4. **Given** a cue waiting on `m`, **When** it is read, **Then** the running op is the await and is read-only in the
   panel.

---

### User Story 3 - A cue branches on a milestone (Priority: P2)

A cue contains an "if" op with a milestone and two branches. When reached, the branch taken is the one for whether the
flag is set, and that branch is what runs.

**Why this priority**: it turns the flags into choices. It ranks below the write and wait halves because a story can
be told without branching, but not without milestones moving.

**Independent Test**: reach an `If m then… else…` with `m` clear (the else branch runs), then with `m` set (the then
branch runs) — no device.

**Acceptance Scenarios**:

1. **Given** `m` set, **When** a cue reaches `If m` with a then-branch and an else-branch, **Then** the then-branch
   runs (and what follows the `If` runs after it).
2. **Given** `m` clear, **Then** the else-branch runs.
3. **Given** a branch that is `Fin`, **Then** the `If` costs no time and the cue carries on.
4. **Given** a branch with a timed op, **Then** that op runs in the branch's place (the `If` is replaced by it).

---

### User Story 4 - The panel authors the four ops (Priority: P3)

The cue panel's kind list offers the four ops; each carries a milestone chosen from the closed list (not typed), and an
"if" has two child rows, each headed by a `then`/`else` label, the author edits; a running await is read-only.

**Why this priority**: the authoring surface. The engine works before it is editable.

**Independent Test**: assert the kinds, the defaults, the labels, the children of an `If`, and that a running await is
locked.

**Acceptance Scenarios**:

1. **Given** the kind list, **When** a node is re-kinded to "Add advent"/"Remove advent"/"Await"/"If", **Then** it is
   made with a default milestone, and "If" is made with two `Fin` children.
2. **Given** any of the four, **When** its milestone is changed, **Then** exactly that node changes as one undo step.
3. **Given** a running await, **When** its row is drawn, **Then** it is labelled as the op it came from and is
   read-only.
4. **Given** an `If`, **When** its rows are drawn, **Then** each branch is headed by a `then`/`else` label row, so the
   two branches are told apart (the label is shown but is not a selectable node).

---

### Edge Cases

- **Awaiting an already-set milestone**: over before it begins — the cue carries on in the same step.
- **A milestone set earlier in the same step**: an `AddAdvent m` before `Await m` satisfies it in that step.
- **A milestone cleared while an await waits**: the await stays waiting (membership is re-checked each step; clearing
  is not remembered).
- **An `If` whose taken branch is `Fin`**: nothing to run — the `If` costs no time.
- **An `If` whose taken branch is timed**: the branch begins in the `If`'s place; the `If` is gone.
- **Nesting**: `Await`/`If` inside a `Sequence`, `Parallel` or `Fork`, and an `If` inside a branch, are all legal.
- **A flag write by a running cue is not undoable**: undo acts on the authored cue, never on what it did (the existing
  invariant, `specs/046`).
- **A milestone no rule names**: unrepresentable — the union is closed, so a cue cannot name one that does not exist.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cue vocabulary MUST add `AddAdvent of Advent`, `RemoveAdvent of Advent`, `Await of Advent` and
  `If of Advent * thenCue: Cue * elseCue: Cue`.
- **FR-002**: The events MUST carry the closed `Advent` union (`world/Advent.fs`), not a string, so a milestone cannot
  be misspelled and matching on it is exhaustive (`specs/055`).
- **FR-003**: `AddAdvent` and `RemoveAdvent` MUST add/remove the milestone in `SharedState.Advents` where the op is
  reached, and MUST hold no state record (the no-progress shape, `specs/046` FR-007).
- **FR-004**: Reaching `AddAdvent m` when `m` is already set, or `RemoveAdvent m` when it is not, MUST be a no-op, not
  an error.
- **FR-005**: `Await m` MUST complete exactly when `m` is in `SharedState.Advents`, re-checked each step; until then
  the cue MUST stay on the op.
- **FR-006**: `Await` MUST have a begun form (`AwaitState`) so that the running op is distinguishable from an authored
  one and the panel can lock it (FR-010, FR-011 of `specs/046`). This is the one case where a `…State` case carries no
  number the authored form does not — it marks a *blocking* op as begun — and it is recorded as such.
- **FR-007**: `If m then else` MUST run the then-branch when `m` is set and the else-branch otherwise, and the chosen
  branch MUST be what runs in the `If`'s place.
- **FR-008**: An `Await` in flight MUST allocate nothing per step, and adding these ops MUST NOT change the shape of
  the run or the walk (`specs/046` FR-011/FR-012; FR-017).
- **FR-009**: The events MUST be assertable **headlessly** — the flag set is plain data on `SharedState` — with no
  window, GPU or device (`specs/046` FR-016).
- **FR-010**: The panel MUST offer "Add advent", "Remove advent", "Await" and "If"; let the author choose the milestone
  from the closed list (`Advent.all`); show an `If`'s two branches as indented child rows, each headed by a `then`/`else`
  label row so the two are told apart; label each row as the op it is; and treat a running await as read-only
  (`specs/047`).
- **FR-011**: A flag a running cue writes MUST NOT be an undoable authored edit — undo and redo act on the authored
  cue, not on what a run did (`specs/046`, invariant 3).
- **FR-012**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with new tests covering the stories and edge
  cases above as pure logic requiring no GPU.

### Key Entities *(include if feature involves data)*

- **Advent** (existing, `world/Advent.fs`): the closed union of story milestones.
- **The flag set** (existing, `SharedState.Advents : Set<Advent>`): mutable project state, what the ops read and write.
- **The four ops** (`world/Cue.fs`): `AddAdvent`/`RemoveAdvent` (no progress), `Await`/`AwaitState` (a condition),
  `If` (a two-branch choice).
- **Cue engine** (existing, `world/CueEngine.fs`): performs the flag reads/writes where the walk reaches them; it
  already carries `SharedState`.
- **Cue panel control** (existing, `editor/CueTree.fs`): the four kinds and the milestone chooser.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue sets and clears milestones exactly: after `AddAdvent m` the set contains `m` and after
  `RemoveAdvent m` it does not (0 deviations across the acceptance scenarios).
- **SC-002**: An awaiting cue costs the game loop nothing: 0 bytes per step while it waits.
- **SC-003**: An `Await` for an already-set milestone, and an `AddAdvent` for one already set, are each over in 0 time
  with no error.
- **SC-004**: An `If` takes exactly the branch its milestone selects, every time (0 wrong branches across the
  scenarios), and its untaken branch never runs.
- **SC-005**: The four ops are demonstrable headlessly: `dotnet build` is clean and `dotnet test` passes.
- **SC-006**: The panel offers exactly the four kinds and a closed-list milestone chooser — 0 free-text milestone
  fields.

## Assumptions

- **The events carry `Advent`, not a string.** `world/Cue.fs` gains a dependency on `world/Advent.fs` (a pure module,
  compiled earlier), so its "`Prime` and `System.Numerics` only" note is widened by one pure module. The alternative —
  a string name resolved at runtime — is rejected: it reintroduces the stringly-typed milestone spec 055 exists to
  remove.
- **`AwaitState` is a begun-marker.** `specs/046`'s convention is "a `…State` case appears where the run form holds a
  number the authored form does not"; a blocking op with no computed number is the one exception, and the state case
  exists so the walk and the panel agree on which op is running (the reference does the same).
- **`If`'s branches default to `Fin`** and are edited by selecting and re-kinding the child row, exactly as a
  `Parallel`'s or `Fork`'s children are; no new "add child" affordance is introduced.
- **The flags are project state**, on `SharedState` (`specs/055` FR-002), so the ops write there and a zone switch does
  not disturb a waiting cue's view of them.
- **A running cue's flag writes are not undone** — the existing invariant (`specs/046`), unchanged: undo restores the
  authored cue, not the world the run changed.
- **No trigger→cue wiring and no scenario library** — phases 10–11; `Await` is satisfied by anything that sets the
  flag, including the panel today.
- **Documentation**: `docs/ROADMAP.md` phase 10 and the `world/Cue.fs`/`world/CueEngine.fs` headers are corrected to
  name the advent ops.
