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

    // ── Create the debug-draw line pipelines (grid behind the scene, gizmos
    //    depth-tested in front), the mesh pipeline + depth texture, and the
    //    screen manager ──
    let swapchainFormat = SDL.GetGPUSwapchainTextureFormat(device, window)
    // The GPU context is a bootstrap value: it is read for `DepthFormat` and handed
    // to shared state at init. Thereafter the run loop reaches it through the shared
    // state (a resized depth target would otherwise be written to a stale copy).
    let gpu = Gpu.create device swapchainFormat
    let debugDraw = DebugDraw.create device swapchainFormat gpu.DepthFormat false
    let gizmoDraw = DebugDraw.create device swapchainFormat gpu.DepthFormat true
    let mutable manager = ScreenManager.init gpu debugDraw gizmoDraw

    let mutable running = true
    let mutable ev = SDL.Event()
    let nowNs () = float (SDL.GetTicksNS())
    let mutable prevTicks = nowNs ()
    let mutable lastTimingLog = 0.0
    let targetFrameNs = 1_000_000_000.0 / 60.0
    let mutable fps = 60.0
    let mutable prevInput = Input.PrevInput.zero

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

    // ── Per-phase frame timing ──
    // `lap` closes the current phase and starts the next; totals accumulate over the
    // 1s log window and reset when reported. Cheap enough (one tick read per phase)
    // to leave in permanently, and it splits managed work from GPU recording.
    let phaseNames = [| "input"; "update"; "ui"; "imgui"; "acquire"; "record"; "submit" |]
    let phaseNs = Array.zeroCreate<float> phaseNames.Length
    let mutable phaseFrames = 0.0
    let mutable phaseFrameMs = 0.0
    let mutable mark = 0.0
    let lap (i: int) : unit =
        let next = nowNs ()
        phaseNs.[i] <- phaseNs.[i] + (next - mark)
        mark <- next

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
        mark <- nowNs ()
        let mutable wheel = 0.0f
        while SDL.PollEvent(&ev) do
            if ev.Type = uint32 SDL.EventType.Quit then
                running <- false

            if ev.Type = uint32 SDL.EventType.MouseWheel then
                wheel <- wheel + ev.Wheel.Y

            do ImGuiSdl3.processEvent(NativePtr.toNativeInt (&&ev)) |> ignore

        // ── Gather input (mouse + keyboard + edge detection) once per frame ──
        let struct (nextPrev, input) = Input.gather prevInput wheel
        prevInput <- nextPrev

        // ── Begin a new ImGui frame ──
        // The backend NewFrame runs first so io.DisplaySize (window size) is
        // valid before the frame is built.
        do ImGuiSdl3.newFrame()

        // ── Build the frame: dt, window size, and the gathered input ──
        let size = ImGui.displaySize()
        let frame = { Dt = float32 (dtMs / 1000.0); WindowW = size.x; WindowH = size.y; Input = input }

        do ImGui.newFrame()
        do lap 0

        // ── Advance and draw the active screen ──
        do ScreenManager.update frame &manager
        do lap 1
        do ScreenManager.draw &manager

        do DebugOverlay.draw fps
        do lap 2

        // ── Render ImGui and submit the frame ──
        do ImGui.render()

        let drawData = ImGui.getDrawData()
        do lap 3

        let cmd = SDL.AcquireGPUCommandBuffer(device)

        let mutable swapchainTexture = IntPtr.Zero
        let mutable w = 0u
        let mutable h = 0u
        do SDL.WaitAndAcquireGPUSwapchainTexture(cmd, window, &swapchainTexture, &w, &h) |> ignore
        do lap 4

        if swapchainTexture <> IntPtr.Zero then
            do ImGuiSdl3.prepareDrawData(drawData, cmd)

            // Scene pass: clear + draw the active screen's scene (color + depth).
            let shared = manager.Shared
            do Gpu.ensureDepthTexture &shared.Gpu w h
            let mutable depthTarget = Gpu.depthTargetInfo shared.Gpu
            sceneTargets.[0].Texture <- swapchainTexture
            do ScreenManager.renderScene &manager { Cmd = cmd; ColorTargets = sceneTargets; DepthTarget = depthTarget }

            // ImGui pass: load (preserves the scene) + draw ImGui on top.
            imguiTargets.[0].Texture <- swapchainTexture
            let imguiPass = SDL.BeginGPURenderPass(cmd, &imguiTargets, 1u, IntPtr.Zero)
            do ImGuiSdl3.renderDrawData(drawData, cmd, imguiPass)
            do SDL.EndGPURenderPass(imguiPass)

        // End of recording. The submit below blocks when the GPU is still busy, so it
        // is measured separately from CPU command recording.
        do lap 5

        do SDL.SubmitGPUCommandBuffer(cmd) |> ignore

        // ── Apply the frame's navigation request ──
        do ScreenManager.applyTransition &manager
        do lap 6
        phaseFrames <- phaseFrames + 1.0
        phaseFrameMs <- phaseFrameMs + dtMs

        // ── Report frame + per-phase times once per second ──
        if now - lastTimingLog >= 1_000_000_000.0 then
            let perFrame (total: float) = total / phaseFrames / 1e6
            let phases =
                phaseNames
                |> Array.mapi (fun i name -> sprintf "%s %.2f" name (perFrame phaseNs.[i]))
                |> String.concat "  "
            do printfn "avg %.2fms (last %.2f) | %s | phases %.2f | actors %d props %d parts %d | draws %d instances %d"
                (phaseFrameMs / phaseFrames) dtMs phases (perFrame (Array.sum phaseNs))
                (int manager.Gameplay.Zone.Actors.Count)
                (int manager.Gameplay.Zone.Props.Count)
                (match manager.Gameplay.Zone.Environment with
                 | Some environment -> int environment.Parts.Count
                 | _ -> 0)
                manager.Gameplay.Batch.DrawCalls
                manager.Gameplay.Batch.InstanceCount
            do Array.fill phaseNs 0 phaseNs.Length 0.0
            phaseFrames <- 0.0
            phaseFrameMs <- 0.0
            lastTimingLog <- now

        // ── Cap to 60 fps (vsync does not pace on this driver) ──
        let sleepNs = targetFrameNs - (nowNs () - tStart)
        if sleepNs > 0.0 then
            do SDL.DelayNS(uint64 sleepNs) |> ignore

    // ── Shut down ──
    do SDL.WaitForGPUIdle(device) |> ignore
    do DebugDraw.release gizmoDraw
    do DebugDraw.release debugDraw
    do Gpu.destroy manager.Shared.Gpu
    do ImGuiSdl3.shutdown()
    do ImGui.destroyContext(IntPtr.Zero)
    do SDL.ReleaseWindowFromGPUDevice(device, window)
    do SDL.DestroyGPUDevice(device)
    do SDL.DestroyWindow(window)
    do SDL.Quit()
    do printfn "SDL3 + ImGui (SDL_gpu): clean shutdown"
    0
