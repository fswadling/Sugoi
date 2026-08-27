#!/usr/bin/env bash
# Builds the native SDL3 + SDL_Renderer Dear ImGui backend (libcimgui_sdl3.so).
#
# Links against the libcimgui.so bundled by the ImGui.NET NuGet package. That
# library is built from cimgui's `docking_inter` branch with:
#   - imgui's docking branch source (IMGUI_HAS_DOCK / IMGUI_HAS_VIEWPORT)
#   - IMGUI_DISABLE_OBSOLETE_FUNCTIONS=1
# We must match those exactly, or ImGui aborts on a struct-layout mismatch.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

IMGUI_TAG="v1.91.6-docking"
CXX="${CXX:-g++}"

# Vendor the imgui docking source (headers + backends).
if [ ! -d imgui ]; then
    git clone --depth 1 --branch "$IMGUI_TAG" https://github.com/ocornut/imgui.git imgui
fi

# Locate the libcimgui.so shipped with ImGui.NET in the NuGet cache. This is a
# linux-x64 development build; adjust for other RIDs.
CIMGUI_SO="$(find "${HOME}/.nuget/packages/imgui.net" -path '*linux-x64*' -name 'libcimgui.so' | sort -V | tail -n 1)"
if [ -z "$CIMGUI_SO" ]; then
    echo "error: could not find libcimgui.so in the ImGui.NET NuGet cache" >&2
    echo "       run 'dotnet restore' first" >&2
    exit 1
fi

echo "Using libcimgui.so: $CIMGUI_SO"

"$CXX" -shared -fPIC -O2 -std=c++11 -DIMGUI_DISABLE_OBSOLETE_FUNCTIONS=1 \
    -Iimgui -Iimgui/backends \
    imgui/backends/imgui_impl_sdl3.cpp \
    imgui/backends/imgui_impl_sdlrenderer3.cpp \
    cimgui_sdl3.cpp \
    "$CIMGUI_SO" \
    -lSDL3 \
    -o libcimgui_sdl3.so

echo "Built libcimgui_sdl3.so"
