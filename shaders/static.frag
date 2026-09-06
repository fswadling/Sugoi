#version 450

layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vWorldNormal;
layout(location = 2) in vec2 vUV;

layout(location = 0) out vec4 outColor;

// Fragment samplers are set = 2 in SDL_gpu's convention.
layout(set = 2, binding = 0) uniform sampler2D uAlbedoMap;
layout(set = 2, binding = 1) uniform sampler2D uNormalMap;
layout(set = 2, binding = 2) uniform sampler2D uMetallicRoughnessMap;
layout(set = 2, binding = 3) uniform sampler2D uEmissiveMap;

// Fragment uniform slot 0 (set = 3 in SDL_gpu's convention).
layout(set = 3, binding = 0) uniform MaterialUniform {
    vec4 uBaseColor;    // rgb + alpha
    vec4 uFactors;      // x = metallic, y = roughness
    vec4 uEmissive;     // rgb emissive
    vec4 uFlags;        // x = hasAlbedo, y = hasNormal, z = hasMetallicRoughness, w = hasEmissive
    vec4 uLightDir;     // xyz toward the sun
    vec4 uLightColor;   // xyz
    vec4 uAmbient;      // xyz
    vec4 uCameraPos;    // xyz
} mat;

const float PI = 3.14159265359;

vec3 srgbToLinear(vec3 c) { return pow(c, vec3(2.2)); }

float D_GGX(float NdotH, float roughness) {
    float a  = roughness * roughness;
    float a2 = a * a;
    float d  = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d);
}

vec3 F_Schlick(float VdotH, vec3 F0) {
    return F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);
}

float G_Smith(float NdotV, float NdotL, float roughness) {
    float k  = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    float g1 = NdotV / (NdotV * (1.0 - k) + k);
    float g2 = NdotL / (NdotL * (1.0 - k) + k);
    return g1 * g2;
}

void main() {
    vec3 albedo = mat.uFlags.x > 0.5
        ? srgbToLinear(texture(uAlbedoMap, vUV).rgb) * mat.uBaseColor.rgb
        : mat.uBaseColor.rgb;

    vec3 N = normalize(vWorldNormal);
    if (mat.uFlags.y > 0.5) {
        // Derivative-based tangent frame (assets carry no TANGENT attribute).
        vec3 tn = texture(uNormalMap, vUV).rgb * 2.0 - 1.0;
        vec3 dp1 = dFdx(vWorldPos);
        vec3 dp2 = dFdy(vWorldPos);
        vec2 duv1 = dFdx(vUV);
        vec2 duv2 = dFdy(vUV);
        vec3 T = normalize(dp1 * duv2.y - dp2 * duv1.y);
        vec3 B = normalize(dp2 * duv1.x - dp1 * duv2.x);
        T = normalize(T - N * dot(N, T));
        B = cross(N, T);
        N = normalize(T * tn.x + B * tn.y + N * tn.z);
    }

    float roughness = clamp(mat.uFactors.y * (mat.uFlags.z > 0.5 ? texture(uMetallicRoughnessMap, vUV).g : 1.0), 0.05, 1.0);
    float metallic  = mat.uFactors.x * (mat.uFlags.z > 0.5 ? texture(uMetallicRoughnessMap, vUV).b : 1.0);

    vec3 V = normalize(mat.uCameraPos.xyz - vWorldPos);
    vec3 L = normalize(mat.uLightDir.xyz);
    vec3 H = normalize(V + L);

    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float VdotH = max(dot(V, H), 0.0);

    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 F = F_Schlick(VdotH, F0);
    float D = D_GGX(NdotH, roughness);
    float G = G_Smith(NdotV, NdotL, roughness);
    vec3 specular = (D * F * G) / max(4.0 * NdotV * NdotL, 1e-4);

    vec3 kd = (1.0 - F) * (1.0 - metallic);
    vec3 direct = (kd * albedo / PI + specular) * mat.uLightColor.rgb * NdotL;
    vec3 ambient = albedo * mat.uAmbient.rgb;
    vec3 emissive = mat.uFlags.w > 0.5
        ? srgbToLinear(texture(uEmissiveMap, vUV).rgb) * mat.uEmissive.rgb
        : mat.uEmissive.rgb;

    vec3 color = direct + ambient + emissive;
    color = pow(color, vec3(1.0 / 2.2));
    outColor = vec4(color, mat.uBaseColor.a);
}
