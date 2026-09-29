#version 450

const int BONES_MAX = 205;
const int BONES_INFLUENCE_MAX = 4;
const int MORPHS_MAX = 8;

layout(location = 0) in vec3  aPosition;
layout(location = 1) in vec3  aNormal;
layout(location = 2) in vec2  aUV;
layout(location = 3) in uvec4 aBoneIds;
layout(location = 4) in vec4  aBoneWeights;
// The outline's averaged extrusion direction (spec 041): the per-vertex normal averaged across
// coincident vertices, so the hull does not gap at the model's hard edges.
layout(location = 5) in vec3  aSmoothNormal;

layout(location = 0) out vec3 vWorldPos;
layout(location = 1) out vec3 vWorldNormal;
layout(location = 2) out vec2 vUV;

// Vertex-stage uniform slot 0 (set = 1 in SDL_gpu's convention).
layout(set = 1, binding = 0) uniform PerDraw {
    mat4 viewProj;
    mat4 model;
    mat4 normalMatrix;
} perDraw;

// Vertex-stage uniform slot 1: the actor's morph weights, pushed per actor.
layout(set = 1, binding = 1) uniform MorphWeights {
    vec4 weights[2];  // up to MORPHS_MAX weights, indexed weights[m / 4][m % 4]
    vec4 info;        // info.x = the asset's morph count
} morphWeights;

// Vertex-stage uniform slot 2 (set = 1 in SDL_gpu's convention): the outline extrusion, pushed per
// pass. x = world-space width (0 = no offset), yzw unused.
layout(set = 1, binding = 2) uniform OutlineParams {
    vec4 uOutline;
} outline;

// Vertex-stage storage buffer slot 0 (set = 0 in SDL_gpu's convention).
layout(set = 0, binding = 0) readonly buffer Bones {
    mat4 bones[];
} bone;

// Vertex-stage storage buffer slot 1: one morph target set's offsets, two vec4 per
// (vertex, morph) — a position offset and a normal offset — indexed
// (gl_VertexIndex * morphCount + m) * 2.
layout(set = 0, binding = 1) readonly buffer Morphs {
    vec4 morphData[];
} morph;

void main() {
    // Displace by the morph targets this actor weighs, before the bones pose the vertex.
    vec3 position = aPosition;
    vec3 normal = aNormal;
    vec3 smoothNormal = aSmoothNormal;
    int morphCount = min(int(morphWeights.info.x), MORPHS_MAX);
    for (int m = 0; m < morphCount; ++m) {
        int sampleIndex = (gl_VertexIndex * morphCount + m) * 2;
        float weight = morphWeights.weights[m / 4][m % 4];
        position += morph.morphData[sampleIndex].xyz * weight;
        normal += morph.morphData[sampleIndex + 1].xyz * weight;
        smoothNormal += morph.morphData[sampleIndex + 1].xyz * weight;
    }

    mat4 boneBlended = mat4(0.0);
    for (int i = 0; i < BONES_INFLUENCE_MAX; ++i) {
        int boneId = min(int(aBoneIds[i]), BONES_MAX - 1);
        boneBlended += bone.bones[boneId] * aBoneWeights[i];
    }

    mat4 modelBone = perDraw.model * boneBlended;
    vec4 world = modelBone * vec4(position, 1.0);
    vWorldPos = world.xyz;
    // Inverse-transpose of the model transform for non-uniform scale, composed
    // with the (rigid) bone rotation. Bones are rotation+translation, so their
    // inverse-transpose is the rotation itself.
    vWorldNormal = mat3(perDraw.normalMatrix) * mat3(boneBlended) * normal;
    vUV = aUV;
    vec4 clip = perDraw.viewProj * world;

    // The outline hull (spec 041): a fixed world-space shell, so it shrinks with distance exactly like
    // the actor (perspective) — a physical ink line, not a constant-pixel overlay. The cel surface
    // pushes width 0, so this is inert for it.
    if (outline.uOutline.x > 0.0) {
        vec3 worldDir = normalize(mat3(perDraw.normalMatrix) * mat3(boneBlended) * smoothNormal);
        world.xyz += worldDir * outline.uOutline.x;
        clip = perDraw.viewProj * world;
    }

    gl_Position = clip;
}
