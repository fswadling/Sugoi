#version 450

const int BONES_MAX = 205;
const int BONES_INFLUENCE_MAX = 4;

layout(location = 0) in vec3  aPosition;
layout(location = 1) in vec3  aNormal;
layout(location = 2) in vec2  aUV;
layout(location = 3) in uvec4 aBoneIds;
layout(location = 4) in vec4  aBoneWeights;

layout(location = 0) out vec4 vColor;

// Vertex-stage uniform slot 0 (set = 1 in SDL_gpu's convention).
layout(set = 1, binding = 0) uniform PerDraw {
    mat4 viewProj;
    mat4 model;
    vec4 lightDir;
    vec4 baseColor;
} perDraw;

// Vertex-stage storage buffer slot 0 (set = 0 in SDL_gpu's convention). The
// bone palette must be a storage buffer, not a pushed uniform: SDL_gpu's
// Vulkan backend binds each pushed uniform slot with a 4 KiB range
// (MAX_UBO_SECTION_SIZE), so any bone past index 63 would read as zero.
layout(set = 0, binding = 0) readonly buffer Bones {
    mat4 bones[];
} bone;

void main() {
    mat4 boneBlended = mat4(0.0);
    for (int i = 0; i < BONES_INFLUENCE_MAX; ++i) {
        int boneId = min(int(aBoneIds[i]), BONES_MAX - 1);
        boneBlended += bone.bones[boneId] * aBoneWeights[i];
    }

    mat4 modelBone = perDraw.model * boneBlended;
    vec4 world = modelBone * vec4(aPosition, 1.0);
    vec3 n = normalize(mat3(modelBone) * aNormal);
    vec3 l = normalize(perDraw.lightDir.xyz);
    float diffuse = max(dot(n, l), 0.0);
    float ambient = 0.25;
    float light = ambient + diffuse * (1.0 - ambient);
    vColor = vec4(perDraw.baseColor.rgb * light, perDraw.baseColor.a);
    gl_Position = perDraw.viewProj * world;
}
