module Engine

// App shell: initialise SDL3 + ImGui, run the frame loop through the screen
// manager, and tear everything down on exit.

open System
open Microsoft.FSharp.NativeInterop
open SDL3

let run () : int =
    // ── Initialise SDL and create the window ──
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

    // ── Create and claim the GPU device ──
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

    // ── Wire up the swapchain and Dear ImGui ──
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

    // ── Set up the screen manager and frame timing ──
    let manager = ScreenManager.init()

    let mutable running = true
    let mutable ev = SDL.Event()
    let nowNs () = float (SDL.GetTicksNS())
    let mutable prevTicks = nowNs ()
    let mutable lastTimingLog = 0.0
    let targetFrameNs = 1_000_000_000.0 / 60.0
    let mutable fps = 60.0

    // ── Main loop ──
    while running do
        let now = nowNs ()
        let dtMs = (now - prevTicks) / 1e6
        prevTicks <- now
        let tStart = now

        // smooth the frame rate shown in the debug overlay
        let instFps = if dtMs > 0.0 then 1000.0 / dtMs else 0.0
        fps <- fps * 0.9 + instFps * 0.1

        // ── Poll input events ──
        while SDL.PollEvent(&ev) do
            if ev.Type = uint32 SDL.EventType.Quit then
                running <- false

            ImGuiSdl3.processEvent(NativePtr.toNativeInt (&&ev)) |> ignore

        // ── Begin a new ImGui frame ──
        ImGuiSdl3.newFrame()
        ImGui.newFrame()

        // ── Advance and draw the active screen ──
        ScreenManager.update manager
        ScreenManager.draw manager
        DebugOverlay.draw fps

        // ── Render ImGui and submit the frame ──
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

        // ── Apply the frame's navigation request ──
        if ScreenManager.applyTransition manager then
            running <- false

        // ── Report frame time once per second ──
        if now - lastTimingLog >= 1_000_000_000.0 then
            printfn "frame %.2fms" dtMs
            lastTimingLog <- now

        // ── Cap to 60 fps (vsync does not pace on this driver) ──
        let sleepNs = targetFrameNs - (nowNs () - tStart)
        if sleepNs > 0.0 then
            SDL.DelayNS(uint64 sleepNs) |> ignore

    // ── Shut down ──
    SDL.WaitForGPUIdle(device) |> ignore
    ImGuiSdl3.shutdown()
    ImGui.destroyContext(IntPtr.Zero)
    SDL.ReleaseWindowFromGPUDevice(device, window)
    SDL.DestroyGPUDevice(device)
    SDL.DestroyWindow(window)
    SDL.Quit()
    printfn "SDL3 + ImGui (SDL_gpu): clean shutdown"
    0
