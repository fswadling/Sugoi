module Engine

// App shell: initialise SDL3 + ImGui, run the frame loop through the screen
// manager, and tear everything down on exit.

open System
open Microsoft.FSharp.NativeInterop
open SDL3
open Screen

let run () : int =
    // ── Initialise SDL and create the window ──
    if not (SDL.Init(SDL.InitFlags.Video)) then
        do eprintfn "SDL_Init failed: %s" (SDL.GetError())
        1
    else

    let window = SDL.CreateWindow("fsharp-game", 1280, 720, SDL.WindowFlags.Resizable)

    if window = IntPtr.Zero then
        do eprintfn "SDL_CreateWindow failed: %s" (SDL.GetError())
        do SDL.Quit()
        1
    else

    // ── Create and claim the GPU device ──
    let shaderFormats : SDL.GPUShaderFormat =
        LanguagePrimitives.EnumOfValue
            (uint32 SDL.GPUShaderFormat.SPIRV ||| uint32 SDL.GPUShaderFormat.DXIL
             ||| uint32 SDL.GPUShaderFormat.MSL ||| uint32 SDL.GPUShaderFormat.MetalLib)

    let device = SDL.CreateGPUDevice(shaderFormats, true, null)

    if device = IntPtr.Zero then
        do eprintfn "SDL_CreateGPUDevice failed: %s" (SDL.GetError())
        do SDL.DestroyWindow(window)
        do SDL.Quit()
        1
    else if not (SDL.ClaimWindowForGPUDevice(device, window)) then
        do eprintfn "SDL_ClaimWindowForGPUDevice failed: %s" (SDL.GetError())
        do SDL.DestroyGPUDevice(device)
        do SDL.DestroyWindow(window)
        do SDL.Quit()
        1
    else

    // ── Wire up the swapchain and Dear ImGui ──
    // Uncapped present mode so the editor isn't locked to the display refresh
    // rate (vsync held it at 30 fps, flickering 30<->60). Prefer mailbox (no
    // tearing); fall back to immediate when mailbox isn't supported.
    let presentMode =
        if SDL.WindowSupportsGPUPresentMode(device, window, SDL.GPUPresentMode.Mailbox) then
            SDL.GPUPresentMode.Mailbox
        else
            SDL.GPUPresentMode.Immediate

    do SDL.SetGPUSwapchainParameters(device, window, SDL.GPUSwapchainComposition.SDR, presentMode) |> ignore

    do ImGui.createContext(IntPtr.Zero) |> ignore
    do ImGui.enableDocking()

    if not (ImGuiSdl3.init(window, device)) then
        do eprintfn "ImGui backend init failed: %s" (SDL.GetError())
        do ImGui.destroyContext(IntPtr.Zero)
        do SDL.ReleaseWindowFromGPUDevice(device, window)
        do SDL.DestroyGPUDevice(device)
        do SDL.DestroyWindow(window)
        do SDL.Quit()
        1
    else

    // ── Create the debug-draw line pipeline and set up the screen manager ──
    let swapchainFormat = SDL.GetGPUSwapchainTextureFormat(device, window)
    let debugDraw = DebugDraw.create device swapchainFormat
    let mutable manager = ScreenManager.init debugDraw

    let mutable running = true
    let mutable ev = SDL.Event()
    let nowNs () = float (SDL.GetTicksNS())
    let mutable prevTicks = nowNs ()
    let mutable lastTimingLog = 0.0
    let targetFrameNs = 1_000_000_000.0 / 60.0
    let mutable fps = 60.0

    // Reused color-target arrays for the scene + ImGui render passes (no
    // per-frame allocation). Invariant fields are set once here; only the
    // swapchain texture is refreshed per frame. The ImGui pass has its own
    // array, so the array lent to the screen's scene pass is never mutated.
    let sceneTargets = Array.zeroCreate<SDL.GPUColorTargetInfo> 1
    sceneTargets.[0].ClearColor <- SDL.FColor(0.13f, 0.15f, 0.18f, 1.0f)
    sceneTargets.[0].LoadOp <- SDL.GPULoadOp.Clear
    sceneTargets.[0].StoreOp <- SDL.GPUStoreOp.Store

    let imguiTargets = Array.zeroCreate<SDL.GPUColorTargetInfo> 1
    imguiTargets.[0].LoadOp <- SDL.GPULoadOp.Load
    imguiTargets.[0].StoreOp <- SDL.GPUStoreOp.Store

    // ── Main loop ──
    while running && not manager.ShouldQuit do
        let now = nowNs ()
        let dtMs = (now - prevTicks) / 1e6
        prevTicks <- now
        let tStart = now

        // smooth the frame rate shown in the debug overlay
        let instFps = if dtMs > 0.0 then 1000.0 / dtMs else 0.0
        fps <- fps * 0.9 + instFps * 0.1

        // ── Poll events (forward to ImGui; accumulate the wheel for the frame) ──
        let mutable wheel = 0.0f
        while SDL.PollEvent(&ev) do
            if ev.Type = uint32 SDL.EventType.Quit then
                running <- false

            if ev.Type = uint32 SDL.EventType.MouseWheel then
                wheel <- wheel + ev.Wheel.Y

            do ImGuiSdl3.processEvent(NativePtr.toNativeInt (&&ev)) |> ignore

        // ── Build the frame: dt plus the event-only input accumulated above ──
        let frame = { Dt = float32 (dtMs / 1000.0); Wheel = wheel }

        // ── Begin a new ImGui frame ──
        do ImGuiSdl3.newFrame()
        do ImGui.newFrame()

        // ── Advance and draw the active screen ──
        manager <- manager |> ScreenManager.update frame |> ScreenManager.draw

        do DebugOverlay.draw fps

        // ── Render ImGui and submit the frame ──
        do ImGui.render()

        let drawData = ImGui.getDrawData()
        let cmd = SDL.AcquireGPUCommandBuffer(device)

        let mutable swapchainTexture = IntPtr.Zero
        let mutable w = 0u
        let mutable h = 0u
        do SDL.WaitAndAcquireGPUSwapchainTexture(cmd, window, &swapchainTexture, &w, &h) |> ignore

        if swapchainTexture <> IntPtr.Zero then
            do ImGuiSdl3.prepareDrawData(drawData, cmd)

            // Scene pass: clear + draw the active screen's scene.
            sceneTargets.[0].Texture <- swapchainTexture
            do ScreenManager.renderScene manager { Cmd = cmd; ColorTargets = sceneTargets }

            // ImGui pass: load (preserves the scene) + draw ImGui on top.
            imguiTargets.[0].Texture <- swapchainTexture
            let imguiPass = SDL.BeginGPURenderPass(cmd, &imguiTargets, 1u, IntPtr.Zero)
            do ImGuiSdl3.renderDrawData(drawData, cmd, imguiPass)
            do SDL.EndGPURenderPass(imguiPass)

        do SDL.SubmitGPUCommandBuffer(cmd) |> ignore

        // ── Apply the frame's navigation request ──
        manager <- ScreenManager.applyTransition manager

        // ── Report frame time once per second ──
        if now - lastTimingLog >= 1_000_000_000.0 then
            do printfn "frame %.2fms" dtMs
            lastTimingLog <- now

        // ── Cap to 60 fps (vsync does not pace on this driver) ──
        let sleepNs = targetFrameNs - (nowNs () - tStart)
        if sleepNs > 0.0 then
            do SDL.DelayNS(uint64 sleepNs) |> ignore

    // ── Shut down ──
    do SDL.WaitForGPUIdle(device) |> ignore
    do ImGuiSdl3.shutdown()
    do ImGui.destroyContext(IntPtr.Zero)
    do SDL.ReleaseWindowFromGPUDevice(device, window)
    do SDL.DestroyGPUDevice(device)
    do SDL.DestroyWindow(window)
    do SDL.Quit()
    do printfn "SDL3 + ImGui (SDL_gpu): clean shutdown"
    0
