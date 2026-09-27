#version 450

// The 2D layer's fragment stage: one texture (the glyph atlas, a portrait, or the
// shared white placeholder) tinted by the vertex colour. Blending is the
// pipeline's, not the shader's.

layout(location = 0) in vec2 vUV;
layout(location = 1) in vec4 vColor;

layout(location = 0) out vec4 outColor;

// Fragment samplers are set = 2 in SDL_gpu's convention.
layout(set = 2, binding = 0) uniform sampler2D uTexture;

void main() {
    outColor = texture(uTexture, vUV) * vColor;
}
