module CommandTests

open Xunit
open System.Numerics
open Entity
open Zone
open Command
open Assets

let private baseEntity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// Headless: an empty asset id means "no model", so spawnProp/setActorWithAsset
// skip renderer creation and the undo/redo logic is exercised without a GPU
// device or loaded asset.
let private assets = Assets.scan "" 0n

// ── Command.apply / undo round-trips ──

[<Fact>]
let ``Spawn apply adds the entity and undo removes it`` () =
    let zone = Zone.create ()
    let ref = { Kind = EntityKind.Prop; Index = 0u }
    let cmd = Spawn (ref, baseEntity "spawned", "")
    Command.apply zone assets cmd
    Assert.Equal("spawned", (Zone.entityAt zone ref).Value.Name)
    Command.undo zone assets cmd
    Assert.True(Zone.entityAt zone ref |> Option.isNone)

[<Fact>]
let ``Delete apply removes the entity and undo restores it`` () =
    let zone = Zone.create ()
    let ref = Zone.spawnProp zone assets (baseEntity "doomed") ""
    let entity = (Zone.entityAt zone ref).Value
    let cmd = Delete (ref, entity, "")
    Command.apply zone assets cmd
    Assert.True(Zone.entityAt zone ref |> Option.isNone)
    Command.undo zone assets cmd
    let restored = Zone.entityAt zone ref
    Assert.True(restored |> Option.isSome)
    Assert.Equal("doomed", restored.Value.Name)

[<Fact>]
let ``Duplicate apply adds a copy and undo removes it`` () =
    let zone = Zone.create ()
    let ref = { Kind = EntityKind.Prop; Index = 0u }
    let cmd = Duplicate (ref, baseEntity "copy", "")
    Command.apply zone assets cmd
    Assert.True(Zone.entityAt zone ref |> Option.isSome)
    Command.undo zone assets cmd
    Assert.True(Zone.entityAt zone ref |> Option.isNone)

[<Fact>]
let ``Transform apply moves the entity and undo restores it`` () =
    let zone = Zone.create ()
    let ref = Zone.spawnProp zone assets (baseEntity "moved") ""
    let before = (Zone.entityAt zone ref).Value.Transform
    let after = { before with Position = before.Position + Vector3(1.0f, 0.0f, 0.0f) }
    let cmd = Transform (ref, before, after)
    Command.apply zone assets cmd
    Assert.Equal(after.Position, (Zone.entityAt zone ref).Value.Transform.Position)
    Command.undo zone assets cmd
    Assert.Equal(before.Position, (Zone.entityAt zone ref).Value.Transform.Position)

[<Fact>]
let ``Transform apply and undo no-op on an absent entity`` () =
    let zone = Zone.create ()
    let ref = { Kind = EntityKind.Prop; Index = 0u }
    let cmd =
        Transform (ref, transformIdentity,
                   { transformIdentity with Position = Vector3(1.0f, 0.0f, 0.0f) })
    Command.apply zone assets cmd
    Command.undo zone assets cmd
    Assert.True(Zone.entityAt zone ref |> Option.isNone)

// ── UndoStack coalescing + undo/redo ──

[<Fact>]
let ``push coalesces consecutive transforms for the same ref into one step`` () =
    let zone = Zone.create ()
    let ref = Zone.spawnProp zone assets (baseEntity "drag") ""
    let t0 = (Zone.entityAt zone ref).Value.Transform
    let t1 = { t0 with Position = t0.Position + Vector3(1.0f, 0.0f, 0.0f) }
    let t2 = { t1 with Position = t1.Position + Vector3(1.0f, 0.0f, 0.0f) }
    let stack = UndoStack.create ()
    let stack = UndoStack.push stack (Transform (ref, t0, t1))
    let stack = UndoStack.push stack (Transform (ref, t1, t2))
    Assert.Equal(1, List.length stack.Applied)
    let stack = UndoStack.undo stack zone assets
    Assert.Equal(t0.Position, (Zone.entityAt zone ref).Value.Transform.Position)
    Assert.True(stack.Applied |> List.isEmpty)

[<Fact>]
let ``push does not coalesce transforms for different refs`` () =
    let zone = Zone.create ()
    let r1 = Zone.spawnProp zone assets (baseEntity "a") ""
    let r2 = Zone.spawnProp zone assets (baseEntity "b") ""
    let t = transformIdentity
    let stack = UndoStack.create ()
    let stack = UndoStack.push stack (Transform (r1, t, { t with Position = Vector3(1.0f, 0.0f, 0.0f) }))
    let stack = UndoStack.push stack (Transform (r2, t, { t with Position = Vector3(2.0f, 0.0f, 0.0f) }))
    Assert.Equal(2, List.length stack.Applied)

[<Fact>]
let ``a new non-coalescing edit clears the redo stack`` () =
    let zone = Zone.create ()
    let ref = Zone.spawnProp zone assets (baseEntity "a") ""
    let stack = UndoStack.create ()
    let stack = UndoStack.push stack (Spawn ({ Kind = EntityKind.Prop; Index = 9u }, baseEntity "x", ""))
    let stack = UndoStack.push stack (Transform (ref, transformIdentity, { transformIdentity with Position = Vector3(1.0f, 0.0f, 0.0f) }))
    let stack = UndoStack.undo stack zone assets
    Assert.Equal(1, List.length stack.Redo)
    let stack = UndoStack.push stack (Spawn ({ Kind = EntityKind.Prop; Index = 10u }, baseEntity "y", ""))
    Assert.True(stack.Redo |> List.isEmpty)

[<Fact>]
let ``undo and redo on an empty stack are no-ops`` () =
    let zone = Zone.create ()
    let stack = UndoStack.create ()
    let stack = UndoStack.undo stack zone assets
    let stack = UndoStack.redo stack zone assets
    Assert.True(stack.Applied |> List.isEmpty)
    Assert.True(stack.Redo |> List.isEmpty)

[<Fact>]
let ``spawn undo redo round-trips through the stack`` () =
    let zone = Zone.create ()
    let entity = baseEntity "spawned"
    let ref = Zone.spawnProp zone assets entity ""
    let stack = UndoStack.create ()
    let stack = UndoStack.push stack (Spawn (ref, entity, ""))
    Assert.Equal("spawned", (Zone.entityAt zone ref).Value.Name)
    let stack = UndoStack.undo stack zone assets
    Assert.True(Zone.entityAt zone ref |> Option.isNone)
    let stack = UndoStack.redo stack zone assets
    Assert.Equal("spawned", (Zone.entityAt zone ref).Value.Name)

[<Fact>]
let ``delete undo redo round-trips through the stack`` () =
    let zone = Zone.create ()
    let ref = Zone.spawnProp zone assets (baseEntity "a") ""
    let entity = (Zone.entityAt zone ref).Value
    let cmd = Delete (ref, entity, "")
    let stack = UndoStack.create ()
    Command.apply zone assets cmd
    let stack = UndoStack.push stack cmd
    Assert.True(Zone.entityAt zone ref |> Option.isNone)
    let stack = UndoStack.undo stack zone assets
    Assert.Equal("a", (Zone.entityAt zone ref).Value.Name)
    let stack = UndoStack.redo stack zone assets
    Assert.True(Zone.entityAt zone ref |> Option.isNone)
