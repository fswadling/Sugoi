#version 450

// Screen-space sprite shader for the game's 2D layer (`render/Ui.fs`) — the
// dialogue panel is its first consumer. Vertices arrive in pixels with the origin
// at the top-left; the pushed ortho matrix maps them to clip space.

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec4 aColor;

layout(location = 0) out vec2 vUV;
layout(location = 1) out vec4 vColor;

// Vertex-stage uniform slot 0 (set = 1 in SDL_gpu's convention). One ortho matrix,
// pushed per batch as `DebugDraw` pushes its view-projection.
layout(set = 1, binding = 0) uniform Uniforms {
    mat4 ortho;
} u;

void main() {
    gl_Position = u.ortho * vec4(aPosition, 0.0, 1.0);
    vUV = aUV;
    vColor = aColor;
}
