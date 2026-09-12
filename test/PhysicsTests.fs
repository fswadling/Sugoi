module PhysicsTests

open Xunit
open System
open System.Numerics
open Entity
open Gltf
open Physics

// A data-only submesh for collider tests (the GPU fields are never read).
let private submesh (positions: float32 array) (indices: int array) : EnvironmentAsset.StaticSubmesh =
    { EnvironmentAsset.Mesh = Unchecked.defaultof<StaticMesh.StaticMesh>
      EnvironmentAsset.Material = Unchecked.defaultof<Texture.Material>
      EnvironmentAsset.Positions = positions
      EnvironmentAsset.Indices = indices }

[<Fact>]
let ``mesh collider merges a submesh range with index offsets`` () =
    let submeshes =
        [| submesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |]
           submesh [| 9.0f; 9.0f; 9.0f;  8.0f; 9.0f; 9.0f;  9.0f; 8.0f; 9.0f;  8.0f; 8.0f; 9.0f |] [| 0; 2; 1;  1; 2; 3 |] |]
    let collider = Zone.meshCollider submeshes 0 2
    Assert.Equal(21, collider.Positions.Length)
    Assert.Equal(9.0f, collider.Positions.[9])
    Assert.Equal<int>([| 0; 1; 2;  3; 5; 4;  4; 5; 6 |] :> seq<int>, collider.Indices :> seq<int>)

[<Fact>]
let ``mesh collider raises on an empty range`` () =
    Assert.Throws<Exception>(fun () -> Zone.meshCollider Array.empty 0 0 |> ignore) |> ignore

[<Fact>]
let ``capsule collider uses a fixed radius and full Z height`` () =
    let b : Bounds = { Min = Vector3(-1.0f, -1.0f, 0.0f); Max = Vector3(1.0f, 1.0f, 4.0f) }
    let collider = Zone.capsuleCollider b
    Assert.Equal(0.35f, collider.Radius)     // fixed actor radius
    Assert.Equal(1.65f, collider.HalfHeight) // height/2 (2.0) - radius (0.35)

[<Fact>]
let ``capsule center sits above the feet at the entity position`` () =
    let collider : CapsuleCollider = { Radius = 0.35f; HalfHeight = 0.473f }
    let transform : Transform = { Position = Vector3(1.0f, 0.0f, 2.0f); Rotation = Quaternion.Identity; Scale = Vector3.One }
    let center = Zone.capsuleCenter transform collider
    Assert.Equal(1.0f, center.X)
    Assert.True(abs (center.Y - 0.823f) < 1e-5f, sprintf "expected y near 0.823, got %f" center.Y)
    Assert.Equal(2.0f, center.Z)

// A 2x2 quad in the z=0 plane, front face toward +z.
let private quadVertices : Vector3 array =
    [| Vector3(-1.0f, -1.0f, 0.0f); Vector3(1.0f, -1.0f, 0.0f); Vector3(1.0f, 1.0f, 0.0f); Vector3(-1.0f, 1.0f, 0.0f) |]
let private quadIndices : int array = [| 0; 1; 2;  0; 2; 3 |]

[<Fact>]
let ``raycast hits a mesh body`` () =
    let world = Physics.create ()
    let body = Physics.createMeshBody world Vector3.Zero Quaternion.Identity quadVertices quadIndices
    match Physics.raycast world (Vector3(0.0f, 0.0f, 10.0f)) (Vector3(0.0f, 0.0f, -100.0f)) with
    | Some (hit, _) -> Assert.Equal(body, hit)
    | None -> failwith "expected a hit"

[<Fact>]
let ``raycast misses when pointing away`` () =
    let world = Physics.create ()
    ignore (Physics.createMeshBody world Vector3.Zero Quaternion.Identity quadVertices quadIndices)
    match Physics.raycast world (Vector3(0.0f, 0.0f, 10.0f)) (Vector3(100.0f, 0.0f, 0.0f)) with
    | None -> ()
    | Some _ -> failwith "expected a miss"

// Inward-facing 10x10x10 cube shell — the "art wall" room: 12 triangles whose
// front faces point at the interior (Jolt raycasts ignore triangle back faces).
let private roomVertices : Vector3 array =
    let v x y z = Vector3(x, y, z)
    [| v -5.0f -5.0f -5.0f;  v 5.0f -5.0f -5.0f;  v 5.0f 5.0f -5.0f;  v -5.0f 5.0f -5.0f   // -z face
       v -5.0f -5.0f  5.0f;  v 5.0f -5.0f  5.0f;  v 5.0f 5.0f  5.0f;  v -5.0f 5.0f  5.0f   // +z face
       v -5.0f -5.0f -5.0f;  v -5.0f -5.0f 5.0f;  v -5.0f 5.0f 5.0f;  v -5.0f 5.0f -5.0f   // -x face
       v  5.0f -5.0f -5.0f;  v 5.0f -5.0f  5.0f;  v 5.0f 5.0f  5.0f;  v  5.0f 5.0f -5.0f   // +x face
       v -5.0f -5.0f -5.0f;  v 5.0f -5.0f -5.0f;  v 5.0f -5.0f 5.0f;  v -5.0f -5.0f 5.0f   // -y face
       v -5.0f  5.0f -5.0f;  v 5.0f  5.0f -5.0f;  v 5.0f  5.0f 5.0f;  v -5.0f  5.0f 5.0f |] // +y face
let private roomIndices : int array =
    [| 0; 2; 3;  0; 1; 2
       4; 6; 5;  4; 7; 6
       8; 11; 10;  8; 10; 9
       12; 14; 15;  12; 13; 14
       16; 18; 17;  16; 19; 18
       20; 21; 22;  20; 22; 23 |]

// A 2x2 quad prop in the z=2 plane, front face toward the room's centre (-z).
let private propVertices : Vector3 array =
    [| Vector3(-1.0f, -1.0f, 2.0f); Vector3(1.0f, -1.0f, 2.0f); Vector3(1.0f, 1.0f, 2.0f); Vector3(-1.0f, 1.0f, 2.0f) |]
let private propIndices : int array = [| 0; 2; 1;  0; 3; 2 |]

// Regression: a ray cast from inside the room used to hit the room shell's box
// collider (its AABB encloses the interior) at fraction 0, winning every pick.
[<Fact>]
let ``ray from inside a hollow room hits the prop, not the room shell`` () =
    let world = Physics.create ()
    let room = Physics.createMeshBody world Vector3.Zero Quaternion.Identity roomVertices roomIndices
    let prop = Physics.createMeshBody world Vector3.Zero Quaternion.Identity propVertices propIndices

    // A ray at the prop hits the prop at its plane, not the wall behind it.
    match Physics.raycast world Vector3.Zero (Vector3(0.0f, 0.0f, 100.0f)) with
    | Some (hit, fraction) ->
        Assert.Equal(prop, hit)
        Assert.True(abs (fraction - 0.02f) < 1e-4f, sprintf "expected fraction near 0.02, got %f" fraction)
    | None -> failwith "expected a hit"

    // A ray away from the prop crosses the interior and hits the far wall.
    match Physics.raycast world Vector3.Zero (Vector3(0.0f, 100.0f, 0.0f)) with
    | Some (hit, _) -> Assert.Equal(room, hit)
    | None -> failwith "expected a hit"

// A flat floor quad at y = -5, front faces up (+Y).
let private floorVertices : Vector3 array =
    [| Vector3(-5.0f, -5.0f, -5.0f); Vector3(5.0f, -5.0f, -5.0f); Vector3(5.0f, -5.0f, 5.0f); Vector3(-5.0f, -5.0f, 5.0f) |]
let private floorIndices : int array = [| 0; 2; 1;  0; 3; 2 |]

// Actor capsules are dynamic with rotation locked: the body must fall under gravity,
// land on the floor, and remain upright.
[<Fact>]
let ``dynamic capsule falls and rests upright on the floor`` () =
    let world = Physics.create ()
    ignore (Physics.createMeshBody world Vector3.Zero Quaternion.Identity floorVertices floorIndices)
    let body = Physics.createCapsuleBody world (Vector3(0.0f, -3.0f, 0.0f)) 0.5f 0.35f
    for _ in 1 .. 300 do
        Physics.step world (1.0f / 60.0f)
    let (position, rotation) = Physics.getBodyTransform world body
    Assert.True(abs (position.Y + 4.15f) < 0.1f, sprintf "expected rest y near -4.15, got %f" position.Y)
    Assert.True(abs rotation.X + abs rotation.Y + abs rotation.Z < 1e-3f, sprintf "expected an upright body, got %A" rotation)

// Regression: `PhysicsSystem` keeps raw pointers to the object-layer filters, so the
// `World` must hold their managed wrappers alive. When it didn't, a collection freed
// them, the filter pointers dangled, and bodies silently fell through all geometry.
[<Fact>]
let ``collision still works after a garbage collection`` () =
    let world = Physics.create ()
    ignore (Physics.createMeshBody world Vector3.Zero Quaternion.Identity floorVertices floorIndices)
    do GC.Collect()
    do GC.WaitForPendingFinalizers()
    do GC.Collect()
    let body = Physics.createCapsuleBody world (Vector3(0.0f, -3.0f, 0.0f)) 0.5f 0.35f
    for _ in 1 .. 300 do
        Physics.step world (1.0f / 60.0f)
    let (position, _) = Physics.getBodyTransform world body
    Assert.True(abs (position.Y + 4.15f) < 0.1f, sprintf "expected rest y near -4.15, got %f" position.Y)
