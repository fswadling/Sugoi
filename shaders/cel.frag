#version 450

// Cel-shaded actor fragment shader (spec 040). Paired with mesh.vert.spv (skinning + morphs); it
// replaces the PBR mesh.frag. Two flat bands with a soft ramp over the wrapped half-Lambert,
// coloured additively from the scene light, with the actor's "Face" mesh lit by an idealized front
// light so its banding stays constant as the actor turns.

layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vWorldNormal;
layout(location = 2) in vec2 vUV;

layout(location = 0) out vec4 outColor;

// Fragment samplers are set = 2 in SDL_gpu's convention.
layout(set = 2, binding = 0) uniform sampler2D uAlbedoMap;

// Fragment uniform slot 0 (set = 3 in SDL_gpu's convention). Lean: cel needs no
// normal/metallic-roughness/emissive factors and no camera position. 80 bytes.
layout(set = 3, binding = 0) uniform CelUniform {
    vec4 uBaseColor;    // rgb + alpha
    vec4 uLightDir;     // xyz toward the scene sun
    vec4 uLightColor;   // xyz
    vec4 uAmbient;      // xyz
    vec4 uFace;         // xyz = idealized face light dir; w = isFace (1/0)
} cel;

// Fixed this phase (FR-012).
const float RAMP_THRESHOLD = 0.5;
const float RAMP_SOFTNESS  = 0.05;
const float ALPHA_CUTOFF   = 0.5;

vec3 srgbToLinear(vec3 c) { return pow(c, vec3(2.2)); }

void main() {
    vec4 tex = texture(uAlbedoMap, vUV);
    // A factor-only material samples the shared opaque-white placeholder, so this never
    // discards for it; hair/eye cutout texels fall below the cutoff (FR-006).
    if (tex.a * cel.uBaseColor.a < ALPHA_CUTOFF) discard;
    vec3 albedo = srgbToLinear(tex.rgb) * cel.uBaseColor.rgb;

    vec3 N = normalize(vWorldNormal);
    // A face mesh takes an idealized front light (constant banding); every other mesh takes
    // the scene sun. The band *colours* below come from the scene either way.
    vec3 L = normalize(cel.uFace.w > 0.5 ? cel.uFace.xyz : cel.uLightDir.xyz);
    float band = smoothstep(RAMP_THRESHOLD - RAMP_SOFTNESS,
                            RAMP_THRESHOLD + RAMP_SOFTNESS,
                            dot(N, L) * 0.5 + 0.5);

    vec3 color = albedo * (cel.uAmbient.rgb + cel.uLightColor.rgb * band);
    outColor = vec4(pow(color, vec3(1.0 / 2.2)), 1.0);
}
