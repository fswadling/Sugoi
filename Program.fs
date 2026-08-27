// Minimal wiring spike: validate that the three bindings resolve and build,
// and that the SDL3 + Box3D native libraries load and run.
//
// ImGui.NET is referenced by the project but intentionally not exercised here:
// it is a thin P/Invoke wrapper over a native `cimgui` that is not yet wired up.

open System
open SDL3

// Box3D's C API lives in the static class `Box3D.Box3D` (namespace and class
// share a name). F# cannot resolve the qualified `Box3D.Box3D` form because the
// name collides, so we `open Box3D` (the namespace) and then reference the
// static class by its short name `Box3D`.
open Box3D

[<EntryPoint>]
let main _argv =
    // --- Box3D: create a world, step it, tear it down (exercises the native
    //     `libbox3d` load and the function-pointer-carrying `b3WorldDef`). ---
    let mutable worldDef = Box3D.b3DefaultWorldDef()
    let worldId = Box3D.b3CreateWorld(&&worldDef)
    printfn "Box3D: world created"

    for _ in 1 .. 60 do
        Box3D.b3World_Step(worldId, 1.0f / 60.0f, 4)

    printfn "Box3D: world stepped 60 frames"
    Box3D.b3DestroyWorld(worldId)
    printfn "Box3D: world destroyed"

    // --- SDL3: init video, open a window + renderer, pump events until quit. ---
    if not (SDL.Init(SDL.InitFlags.Video)) then
        eprintfn "SDL_Init failed: %s" (SDL.GetError())
        1
    else
        let mutable window = IntPtr.Zero
        let mutable renderer = IntPtr.Zero

        if not (SDL.CreateWindowAndRenderer("fsharp-game", 1280, 720, SDL.WindowFlags.Resizable, &window, &renderer)) then
            eprintfn "SDL_CreateWindowAndRenderer failed: %s" (SDL.GetError())
            SDL.Quit()
            1
        else
            let mutable running = true
            let mutable ev = SDL.Event()

            while running do
                while SDL.PollEvent(&ev) do
                    if ev.Type = uint32 SDL.EventType.Quit then
                        running <- false

                SDL.SetRenderDrawColor(renderer, 16uy, 16uy, 24uy, 255uy) |> ignore
                SDL.RenderClear(renderer) |> ignore
                SDL.RenderPresent(renderer) |> ignore

            SDL.DestroyRenderer(renderer)
            SDL.DestroyWindow(window)
            SDL.Quit()
            printfn "SDL3: clean shutdown"
            0
