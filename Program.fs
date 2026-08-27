// SDL3 + Dear ImGui wiring spike.
//
// Opens an SDL3 window/renderer, initialises Dear ImGui through the
// SDL3 + SDL_Renderer backends (see ImGuiSdl3.fs and native/), and renders the
// ImGui demo window until the window is closed.

open System
open Microsoft.FSharp.NativeInterop
open SDL3
open ImGuiNET

[<EntryPoint>]
let main _argv =
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
            ImGui.CreateContext() |> ignore

            if not (ImGuiSdl3.init(window, renderer)) then
                eprintfn "ImGui backend init failed: %s" (SDL.GetError())
                ImGui.DestroyContext()
                SDL.DestroyRenderer(renderer)
                SDL.DestroyWindow(window)
                SDL.Quit()
                1
            else
                let mutable running = true
                let mutable ev = SDL.Event()

                while running do
                    while SDL.PollEvent(&ev) do
                        if ev.Type = uint32 SDL.EventType.Quit then
                            running <- false

                        ImGuiSdl3.processEvent(NativePtr.toNativeInt (&&ev)) |> ignore

                    SDL.SetRenderDrawColor(renderer, 16uy, 16uy, 24uy, 255uy) |> ignore
                    SDL.RenderClear(renderer) |> ignore

                    ImGuiSdl3.newFrame()
                    ImGui.NewFrame()
                    ImGui.ShowDemoWindow()
                    ImGui.Render()
                    let drawData = ImGui.GetDrawData()
                    ImGuiSdl3.renderDrawData(NativePtr.toNativeInt drawData.NativePtr, renderer)

                    SDL.RenderPresent(renderer) |> ignore

                ImGuiSdl3.shutdown()
                ImGui.DestroyContext()
                SDL.DestroyRenderer(renderer)
                SDL.DestroyWindow(window)
                SDL.Quit()
                printfn "SDL3 + ImGui: clean shutdown"
                0
