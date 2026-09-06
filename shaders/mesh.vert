#version 450

const int BONES_MAX = 205;
const int BONES_INFLUENCE_MAX = 4;

layout(location = 0) in vec3  aPosition;
layout(location = 1) in vec3  aNormal;
layout(location = 2) in vec2  aUV;
layout(location = 3) in uvec4 aBoneIds;
layout(location = 4) in vec4  aBoneWeights;

layout(location = 0) out vec3 vWorldPos;
layout(location = 1) out vec3 vWorldNormal;
layout(location = 2) out vec2 vUV;

// Vertex-stage uniform slot 0 (set = 1 in SDL_gpu's convention).
layout(set = 1, binding = 0) uniform PerDraw {
    mat4 viewProj;
    mat4 model;
    mat4 normalMatrix;
} perDraw;

// Vertex-stage storage buffer slot 0 (set = 0 in SDL_gpu's convention).
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
    vWorldPos = world.xyz;
    // Inverse-transpose of the model transform for non-uniform scale, composed
    // with the (rigid) bone rotation. Bones are rotation+translation, so their
    // inverse-transpose is the rotation itself.
    vWorldNormal = mat3(perDraw.normalMatrix) * mat3(boneBlended) * aNormal;
    vUV = aUV;
    gl_Position = perDraw.viewProj * world;
}
