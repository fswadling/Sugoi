#version 450

// The actor outline fragment shader (spec 041). Paired with mesh.vert.spv through a front-cull,
// depth-biased pipeline: the hull's back faces are drawn flat in the outline colour and the depth
// buffer hides everything except the ring past the silhouette.

// Locations match mesh.vert's outputs exactly; vWorldPos/vWorldNormal are unused here.
layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vWorldNormal;
layout(location = 2) in vec2 vUV;

layout(location = 0) out vec4 outColor;

// Fragment samplers are set = 2 in SDL_gpu's convention.
layout(set = 2, binding = 0) uniform sampler2D uAlbedoMap;

// Fixed this phase (spec 041, FR-009).
const vec3  OUTLINE_COLOR = vec3(0.05, 0.05, 0.06);
const float ALPHA_CUTOFF  = 0.5;

void main() {
    // Mask the outline by the material's cutout (FR-006): a transparent hair/eye texel draws no
    // outline, so the line follows the visible shape rather than boxing the card. The shared
    // opaque-white placeholder means an opaque (factor-only) material never discards.
    if (texture(uAlbedoMap, vUV).a < ALPHA_CUTOFF) discard;
    outColor = vec4(OUTLINE_COLOR, 1.0);
}
