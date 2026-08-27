#!/usr/bin/env bash
# Builds the native Dear ImGui library (libcimgui.so) with:
#   - cimgui C API (`ig*` wrappers)
#   - imgui core (docking branch)
#   - SDL3 platform backend (imgui_impl_sdl3)
#   - SDL_gpu renderer backend (imgui_impl_sdlgpu3 + shaders)
#   - our F#-facing C shim (cimgui_sdl3.cpp)
#
# cimgui's generated `cimgui.cpp` must match its bundled imgui submodule, so we
# vendor the whole cimgui repo (with submodule) rather than imgui alone.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

CIMGUI_URL="https://github.com/cimgui/cimgui.git"
CIMGUI_BRANCH="docking_inter"
CXX="${CXX:-g++}"

# Vendor cimgui (with its imgui docking submodule) if not already present.
if [ ! -d cimgui ]; then
    git clone --depth 1 --branch "$CIMGUI_BRANCH" --recurse-submodules "$CIMGUI_URL" cimgui
fi

echo "Building libcimgui.so ..."

"$CXX" -shared -fPIC -O2 -std=c++17 \
    -Icimgui -Icimgui/imgui -Icimgui/imgui/backends \
    cimgui/cimgui.cpp \
    cimgui/imgui/imgui.cpp \
    cimgui/imgui/imgui_draw.cpp \
    cimgui/imgui/imgui_demo.cpp \
    cimgui/imgui/imgui_tables.cpp \
    cimgui/imgui/imgui_widgets.cpp \
    cimgui/imgui/backends/imgui_impl_sdl3.cpp \
    cimgui/imgui/backends/imgui_impl_sdlgpu3.cpp \
    cimgui_sdl3.cpp \
    -lSDL3 \
    -o libcimgui.so

echo "Built libcimgui.so"
