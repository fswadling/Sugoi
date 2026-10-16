# Contract: Structural Invariants

**Date**: 2026-10-16 | **Spec**: [spec.md](spec.md) | **Data model**: [data-model.md](../data-model.md)

This feature exposes no API; its contract is the set of structural invariants
the repository must now satisfy, each with its check. Any future change that
breaks one of these is a regression of this feature.

## I-1: Cluster contiguity

Every cluster's files form one contiguous block in the `fsharp-game.fsproj`
compile list, in this layer order:

```text
narrative → core → physics → render → audio → world → state → editor → screen → Shell → Program
```

**Check**: read the `<Compile Include>` list top to bottom; each folder's files
appear in one run. (`world/assets/` and `world/render/` files may interleave
freely with `world/` root files — one cluster.)

## I-2: Order soundness (compiler-enforced)

For every cross-file reference A → B, B appears no later than A in the compile
list.

**Check**: `dotnet build` — F# compiles in list order, so a violation is a
compile error by construction.

## I-3: Folder = layer

For every cross-cluster reference A → B, B's cluster is no later than A's
cluster in the layer order (I-1). Zero exceptions.

**Check**: the dependency-graph scan in `quickstart.md` prints an empty
"UPWARD CLUSTER EDGES" section.

## I-4: Unique global module names

No two files declare the same global (non-`Narrative`) module name.

**Check**:

```sh
grep -rh '^module ' --include='*.fs' . | grep -v '^module .*=' | sort | uniq -d
# expected: empty
```

## I-5: No nested-module shadowing

A nested module inside a module that consumers `open` must not share a global
module's name. (Today: `Entity.ActorEntity`, `Entity.Prop`, `Command.UndoStack`,
`Screen.Transition`, `Input.{KeyMask,Key,PrevInput}` — none collide.)

**Check**: after any new nested `module X =`, grep for a global `module X`
elsewhere; expected: none.

## I-6: The documentation describes the tree

`docs/PROJECT_STRUCTURE.md`'s layout tree names every existing source file and
no retired ones.

**Check**: diff the doc's tree against `ls` of each cluster; no surprises.
