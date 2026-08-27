// Thin C ABI over Dear ImGui's SDL3 + SDL_Renderer backends, so they can be
// P/Invoked from F#.
//
// Compiled into `libcimgui_sdl3.so` and linked against the `libcimgui.so`
// bundled by ImGui.NET (which provides the `ImGui::*` symbols). Because that
// library's SONAME is `cimgui.so`, the dynamic linker reuses the already-loaded
// core instead of loading a second copy, so ImGui.NET and this backend share
// one ImGui context.
#include <SDL3/SDL.h>

#include "imgui.h"
#include "imgui_impl_sdl3.h"
#include "imgui_impl_sdlrenderer3.h"

extern "C" {

// Initialise both the platform (SDL3) and renderer (SDL_Renderer) backends.
bool cimgui_sdl3_init(SDL_Window* window, SDL_Renderer* renderer)
{
    if (!ImGui_ImplSDL3_InitForSDLRenderer(window, renderer))
        return false;
    if (!ImGui_ImplSDLRenderer3_Init(renderer))
        return false;
    return true;
}

void cimgui_sdl3_shutdown()
{
    ImGui_ImplSDLRenderer3_Shutdown();
    ImGui_ImplSDL3_Shutdown();
}

// Prepare the backends for a new frame. Call before ImGui::NewFrame().
void cimgui_sdl3_new_frame()
{
    ImGui_ImplSDLRenderer3_NewFrame();
    ImGui_ImplSDL3_NewFrame();
}

// Feed one SDL event to the platform backend. Returns true if ImGui wants it.
bool cimgui_sdl3_process_event(const SDL_Event* event)
{
    return ImGui_ImplSDL3_ProcessEvent(event);
}

// Submit ImGui's draw data to the SDL_Renderer. Call after ImGui::Render().
void cimgui_sdl3_render_draw_data(ImDrawData* draw_data, SDL_Renderer* renderer)
{
    ImGui_ImplSDLRenderer3_RenderDrawData(draw_data, renderer);
}

} // extern "C"
