module ViewportTests

// The reference grid's visibility is the viewport's, gated on editing (spec 093): these assert, with no
// window, that `Viewport.drawGrid` builds the grid while editing, empties it while playing, and restores
// it unchanged when the editor is re-entered. The device-free `DebugDraw` below supplies only the CPU
// accumulator the grid writes into (`Data`/`Count`); its GPU handles are never dereferenced on this path
// (the `test/TestSupport.fs` device-free-fixture precedent).

open Xunit

// The grid's own accumulator, without a device. `Data` is generously larger than the grid's ~5,600 floats;
// the accumulator's exact floats-per-vertex stride is private to `render/DebugDraw.fs`, so it is not mirrored.
let private gridDraw () : DebugDraw.DebugDraw =
    { Device = Unchecked.defaultof<_>
      Pipeline = Unchecked.defaultof<_>
      VertexShader = Unchecked.defaultof<_>
      FragmentShader = Unchecked.defaultof<_>
      VertexBuffer = Unchecked.defaultof<_>
      TransferBuffer = Unchecked.defaultof<_>
      Bindings = Unchecked.defaultof<_>
      Data = Array.zeroCreate 65536
      StagingBytes = Unchecked.defaultof<_>
      UniformBytes = Unchecked.defaultof<_>
      Count = 0 }

let private newState (debugDraw: DebugDraw.DebugDraw) : GameplayState.GameplayState =
    GameplayState.create
        debugDraw
        (Unchecked.defaultof<DebugDraw.DebugDraw>)
        (Unchecked.defaultof<Ui.Ui>)
        (Unchecked.defaultof<Skydome.Skydome>)

[<Fact>]
let ``entering play empties the reference grid`` () =
    let mutable state = newState (gridDraw ())
    state.Editing <- true
    Viewport.drawGrid &state
    Assert.True(state.DebugDraw.Count > 0, "the grid should be built while editing")
    state.Editing <- false
    Viewport.drawGrid &state
    Assert.Equal(0, state.DebugDraw.Count)

[<Fact>]
let ``the reference grid matches the pre-feature grid while editing`` () =
    // The grid the editor drew before this feature: the same build, from an empty accumulator.
    let reference = DebugDraw.clear (gridDraw ()) |> DebugDraw.drawGrid

    // A fresh state (the editor is active by default).
    let mutable fresh = newState (gridDraw ())
    Viewport.drawGrid &fresh
    Assert.Equal(reference.Count, fresh.DebugDraw.Count)
    Assert.True(Array.forall2 (=) reference.Data fresh.DebugDraw.Data, "the fresh grid must match the reference")

    // And a state just returned from play: the grid is restored, byte-for-byte.
    let mutable state = newState (gridDraw ())
    state.Editing <- false
    Viewport.drawGrid &state
    state.Editing <- true
    Viewport.drawGrid &state
    Assert.Equal(reference.Count, state.DebugDraw.Count)
    Assert.True(Array.forall2 (=) reference.Data state.DebugDraw.Data, "the restored grid must match the reference")
