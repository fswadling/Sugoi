#version 450

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUV;

// Per-instance transform rows from vertex slot 1 (InputRate.Instance), one Float4
// per row: four model rows then three normal-matrix rows. SDL_gpu / GLSL read the
// rows into mat columns; see docs/RENDERING_NOTES.md §7 for the no-transpose
// convention this preserves.
layout(location = 3) in vec4 aModel0;
layout(location = 4) in vec4 aModel1;
layout(location = 5) in vec4 aModel2;
layout(location = 6) in vec4 aModel3;
layout(location = 7) in vec4 aNormal0;
layout(location = 8) in vec4 aNormal1;
layout(location = 9) in vec4 aNormal2;

layout(location = 0) out vec3 vWorldPos;
layout(location = 1) out vec3 vWorldNormal;
layout(location = 2) out vec2 vUV;

// Vertex-stage uniform slot 0 (set = 1 in SDL_gpu's convention). Shared by the
// whole static block; the per-instance data comes from the attributes above.
layout(set = 1, binding = 0) uniform PerDraw {
    mat4 viewProj;
} perDraw;

void main() {
    mat4 model = mat4(aModel0, aModel1, aModel2, aModel3);
    mat3 normalMatrix = mat3(aNormal0.xyz, aNormal1.xyz, aNormal2.xyz);
    vec4 world = model * vec4(aPosition, 1.0);
    vWorldPos = world.xyz;
    vWorldNormal = normalMatrix * aNormal;
    vUV = aUV;
    gl_Position = perDraw.viewProj * world;
}
