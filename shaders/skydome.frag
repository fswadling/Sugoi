#version 450

// The skydome fragment stage (spec 092): a horizon-to-zenith gradient with a
// simple sun that follows the zone's authored light. A trimmed port of
// odin-game's skydome.frag — its procedural clouds and layered sun are deferred.

layout(location = 0) in vec3 vLocalPos;

layout(location = 0) out vec4 outColor;

// Fragment uniform slot 0 (set = 3 in SDL_gpu's convention): the zone's authored
// sky colours (linear rgb) and the direction toward the sun (from the zone's light).
layout(set = 3, binding = 0) uniform SkyUniform {
    vec4 uZenith;   // linear rgb
    vec4 uHorizon;  // linear rgb
    vec4 uLightDir; // xyz = direction toward the sun
} sky;

void main() {
    vec3 dir = normalize(vLocalPos);

    // The horizon colour at and below the horizon, blending to the zenith colour overhead.
    vec3 color = sky.uHorizon.rgb;
    if (dir.y > 0.0) {
        color = mix(sky.uHorizon.rgb, sky.uZenith.rgb, smoothstep(0.0, 0.4, dir.y));
    }

    // A simple sun disc, drawn only above the horizon.
    vec3 L = normalize(sky.uLightDir.xyz);
    float d = max(dot(dir, L), 0.0) * step(0.0, dir.y);
    vec3 sunCol = vec3(1.0, 0.95, 0.85);
    color = mix(color, sunCol, smoothstep(0.997, 0.999, d));

    // Encode to gamma at the output, as every other scene shader does (the scene
    // pass writes a non-sRGB target and colours are authored linear).
    outColor = vec4(pow(color, vec3(1.0 / 2.2)), 1.0);
}
