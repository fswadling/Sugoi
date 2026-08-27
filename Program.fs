// SDL3 + Dear ImGui wiring spike, using the SDL_gpu renderer backend.
//
// Opens an SDL3 window, creates an SDL_gpu device, initialises Dear ImGui
// through the SDL3 platform + SDL_gpu renderer backends (see ImGuiSdl3.fs and
// native/), and renders the ImGui demo window in its own GPU render pass.

open System
open Microsoft.FSharp.NativeInterop
open SDL3

[<EntryPoint>]
let main _argv =
    if not (SDL.Init(SDL.InitFlags.Video)) then
        eprintfn "SDL_Init failed: %s" (SDL.GetError())
        1
    else
        let window = SDL.CreateWindow("fsharp-game", 1280, 720, SDL.WindowFlags.Resizable)

        if window = IntPtr.Zero then
            eprintfn "SDL_CreateWindow failed: %s" (SDL.GetError())
            SDL.Quit()
            1
        else
            let shaderFormats : SDL.GPUShaderFormat =
                LanguagePrimitives.EnumOfValue
                    (uint32 SDL.GPUShaderFormat.SPIRV ||| uint32 SDL.GPUShaderFormat.DXIL
                     ||| uint32 SDL.GPUShaderFormat.MSL ||| uint32 SDL.GPUShaderFormat.MetalLib)

            let device = SDL.CreateGPUDevice(shaderFormats, true, null)

            if device = IntPtr.Zero then
                eprintfn "SDL_CreateGPUDevice failed: %s" (SDL.GetError())
                SDL.DestroyWindow(window)
                SDL.Quit()
                1
            else if not (SDL.ClaimWindowForGPUDevice(device, window)) then
                eprintfn "SDL_ClaimWindowForGPUDevice failed: %s" (SDL.GetError())
                SDL.DestroyGPUDevice(device)
                SDL.DestroyWindow(window)
                SDL.Quit()
                1
            else
                SDL.SetGPUSwapchainParameters(device, window, SDL.GPUSwapchainComposition.SDR, SDL.GPUPresentMode.VSync) |> ignore

                ImGui.createContext(IntPtr.Zero) |> ignore

                if not (ImGuiSdl3.init(window, device)) then
                    eprintfn "ImGui backend init failed: %s" (SDL.GetError())
                    ImGui.destroyContext(IntPtr.Zero)
                    SDL.ReleaseWindowFromGPUDevice(device, window)
                    SDL.DestroyGPUDevice(device)
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

                        ImGuiSdl3.newFrame()
                        ImGui.newFrame()
                        ImGui.showDemoWindow(IntPtr.Zero)
                        ImGui.render()

                        let drawData = ImGui.getDrawData()
                        let cmd = SDL.AcquireGPUCommandBuffer(device)

                        let mutable swapchainTexture = IntPtr.Zero
                        let mutable w = 0u
                        let mutable h = 0u
                        SDL.WaitAndAcquireGPUSwapchainTexture(cmd, window, &swapchainTexture, &w, &h) |> ignore

                        if swapchainTexture <> IntPtr.Zero then
                            ImGuiSdl3.prepareDrawData(drawData, cmd)

                            let mutable ti = SDL.GPUColorTargetInfo()
                            ti.Texture <- swapchainTexture
                            ti.ClearColor <- SDL.FColor(0.45f, 0.55f, 0.60f, 1.0f)
                            ti.LoadOp <- SDL.GPULoadOp.Clear
                            ti.StoreOp <- SDL.GPUStoreOp.Store
                            ti.MipLevel <- 0u
                            ti.LayerOrDepthPlane <- 0u

                            let mutable colorTargets = [| ti |]
                            let renderPass = SDL.BeginGPURenderPass(cmd, &colorTargets, 1u, IntPtr.Zero)
                            ImGuiSdl3.renderDrawData(drawData, cmd, renderPass)
                            SDL.EndGPURenderPass(renderPass)

                        SDL.SubmitGPUCommandBuffer(cmd) |> ignore

                    SDL.WaitForGPUIdle(device) |> ignore
                    ImGuiSdl3.shutdown()
                    ImGui.destroyContext(IntPtr.Zero)
                    SDL.ReleaseWindowFromGPUDevice(device, window)
                    SDL.DestroyGPUDevice(device)
                    SDL.DestroyWindow(window)
                    SDL.Quit()
                    printfn "SDL3 + ImGui (SDL_gpu): clean shutdown"
                    0
