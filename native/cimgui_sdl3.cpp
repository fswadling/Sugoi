// Thin C ABI over Dear ImGui's SDL3 platform backend + SDL_gpu renderer
// backend, so they can be P/Invoked from F#.
//
// Compiled into `libcimgui.so` alongside cimgui itself (see build.sh). All
// functions use C linkage; pointers are passed opaquely as void* / handles.

#include <SDL3/SDL.h>
#include <SDL3/SDL_gpu.h>

#include "imgui.h"
#include "imgui_internal.h"
#include "imgui_impl_sdl3.h"
#include "imgui_impl_sdlgpu3.h"

extern "C" {

// Initialise both the platform (SDL3) and renderer (SDL_gpu) backends. The
// swapchain color-target format is queried from the device, so the caller only
// needs the window and GPU device handles.
bool cimgui_sdl3_init(SDL_Window* window, SDL_GPUDevice* device)
{
    if (!ImGui_ImplSDL3_InitForSDLGPU(window))
        return false;

    ImGui_ImplSDLGPU3_InitInfo init_info = {};
    init_info.Device = device;
    init_info.ColorTargetFormat = SDL_GetGPUSwapchainTextureFormat(device, window);
    init_info.MSAASamples = SDL_GPU_SAMPLECOUNT_1;
    init_info.SwapchainComposition = SDL_GPU_SWAPCHAINCOMPOSITION_SDR;
    init_info.PresentMode = SDL_GPU_PRESENTMODE_VSYNC;

    return ImGui_ImplSDLGPU3_Init(&init_info);
}

void cimgui_sdl3_shutdown()
{
    // Match the official example: platform backend first, then renderer.
    ImGui_ImplSDL3_Shutdown();
    ImGui_ImplSDLGPU3_Shutdown();
}

// Prepare the backends for a new frame. Call before ImGui::NewFrame().
void cimgui_sdl3_new_frame()
{
    ImGui_ImplSDLGPU3_NewFrame();
    ImGui_ImplSDL3_NewFrame();
}

// Feed one SDL event to the platform backend. Returns true if ImGui wants it.
bool cimgui_sdl3_process_event(const SDL_Event* event)
{
    return ImGui_ImplSDL3_ProcessEvent(event);
}

// Upload ImGui's vertex/index buffers. MUST be called before the render pass.
void cimgui_sdl3_prepare_draw_data(ImDrawData* draw_data, SDL_GPUCommandBuffer* command_buffer)
{
    ImGui_ImplSDLGPU3_PrepareDrawData(draw_data, command_buffer);
}

// Emit ImGui draw commands. MUST be called inside the render pass.
void cimgui_sdl3_render_draw_data(ImDrawData* draw_data, SDL_GPUCommandBuffer* command_buffer, SDL_GPURenderPass* render_pass)
{
    ImGui_ImplSDLGPU3_RenderDrawData(draw_data, command_buffer, render_pass, nullptr);
}

// Enable docking + keyboard nav and disable layout persistence. Call once,
// right after the ImGui context is created, so the default dock layout is
// rebuilt deterministically each run (see cimgui_setup_dock_layout).
void cimgui_enable_docking()
{
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;
    io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
    io.IniFilename = nullptr; // no persistent layout
}

// Build the default editor dock layout: Outliner (left), Inspector (right),
// Asset Browser (bottom), and a passthru central node for the 3D viewport.
void cimgui_setup_dock_layout(ImGuiID dockspace_id)
{
    ImGui::DockBuilderRemoveNode(dockspace_id);
    // Keep the passthru flag so the central (viewport) node stays transparent
    // from frame 1; DockBuilderAddNode otherwise re-creates the node with
    // KeepAliveOnly and drops the flag DockSpaceOverViewport set.
    ImGui::DockBuilderAddNode(dockspace_id, ImGuiDockNodeFlags_DockSpace | ImGuiDockNodeFlags_PassthruCentralNode);
    ImGui::DockBuilderSetNodeSize(dockspace_id, ImGui::GetMainViewport()->Size);

    ImGuiID main = dockspace_id;
    ImGuiID left = ImGui::DockBuilderSplitNode(main, ImGuiDir_Left, 0.20f, nullptr, &main);
    ImGuiID right = ImGui::DockBuilderSplitNode(main, ImGuiDir_Right, 0.25f, nullptr, &main);
    ImGuiID bottom = ImGui::DockBuilderSplitNode(main, ImGuiDir_Down, 0.25f, nullptr, &main);

    // Mark the remaining center node as the central (passthru) node so the 3D
    // viewport, rendered to the swapchain behind ImGui, shows through it.
    if (ImGuiDockNode* center = ImGui::DockBuilderGetNode(main))
        center->SetLocalFlags(center->LocalFlags | ImGuiDockNodeFlags_CentralNode);

    ImGui::DockBuilderDockWindow("Outliner", left);
    ImGui::DockBuilderDockWindow("Inspector", right);
    ImGui::DockBuilderDockWindow("Asset Browser", bottom);
    ImGui::DockBuilderFinish(dockspace_id);
}

} // extern "C"
