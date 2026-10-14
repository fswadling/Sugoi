#version 450

// Cel-shaded actor fragment shader (spec 040). Paired with mesh.vert.spv (skinning + morphs); it
// replaces the PBR mesh.frag. Two flat bands with a soft ramp over the wrapped half-Lambert,
// coloured additively from the scene light. The actor's "Face" mesh keeps a fixed band *direction*
// from an idealized front light so its banding stays constant as the actor turns, but the *amount*
// of that light switches as the face turns away from the scene sun (spec 085; see below).

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
// Half-width (in the azimuthal dot) of the face's lit/shade switch at the 90° crossing.
const float FACE_FADE_SOFTNESS = 0.05;

vec3 srgbToLinear(vec3 c) { return pow(c, vec3(2.2)); }

void main() {
    vec4 tex = texture(uAlbedoMap, vUV);
    // A factor-only material samples the shared opaque-white placeholder, so this never
    // discards for it; hair/eye cutout texels fall below the cutoff (FR-006).
    if (tex.a * cel.uBaseColor.a < ALPHA_CUTOFF) discard;
    vec3 albedo = srgbToLinear(tex.rgb) * cel.uBaseColor.rgb;

    vec3 N = normalize(vWorldNormal);
    bool isFace = cel.uFace.w > 0.5;
    // A face mesh takes an idealized front light (constant banding); every other mesh takes
    // the scene sun. The band *colours* below come from the scene either way.
    vec3 L = normalize(isFace ? cel.uFace.xyz : cel.uLightDir.xyz);
    float band = smoothstep(RAMP_THRESHOLD - RAMP_SOFTNESS,
                            RAMP_THRESHOLD + RAMP_SOFTNESS,
                            dot(N, L) * 0.5 + 0.5);

    // A face keeps its idealized band *direction*, but its band *amount* switches with the sun's
    // azimuth: the full front light while the face points more toward the sun than away from it, and
    // the shadow band's brightness once it points away — a two-tone switch at the 90° crossing, eased
    // by a small smoothstep so it does not strobe, matching the band ramp's crispness. The headings
    // are compared in azimuth (Y dropped), so the sun's elevation does not bias it.
    if (isFace) {
        vec3 faceH = vec3(cel.uFace.x, 0.0, cel.uFace.z);
        vec3 sunH = vec3(cel.uLightDir.x, 0.0, cel.uLightDir.z);
        float faceLen = length(faceH);
        float sunLen = length(sunH);
        // No azimuth to compare (a face pointing straight up/down, or a zenith sun): keep the
        // full front light rather than dividing by zero.
        float toward = (faceLen > 1e-4 && sunLen > 1e-4)
            ? dot(faceH, sunH) / (faceLen * sunLen)
            : 1.0;
        // 1 = pointing more toward the sun (full front light), 0 = more away (the lit band becomes
        // the shadow band), with the small smoothstep through the 90° crossing.
        band *= smoothstep(-FACE_FADE_SOFTNESS, FACE_FADE_SOFTNESS, toward);
    }

    vec3 color = albedo * (cel.uAmbient.rgb + cel.uLightColor.rgb * band);
    outColor = vec4(pow(color, vec3(1.0 / 2.2)), 1.0);
}
