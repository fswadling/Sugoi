#version 450

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUV;

layout(location = 0) out vec4 vColor;

// Vertex-stage uniform slot 0 (set = 1 in SDL_gpu's convention).
layout(set = 1, binding = 0) uniform PerDraw {
    mat4 viewProj;
    mat4 model;
    vec4 lightDir;
    vec4 baseColor;
} perDraw;

void main() {
    vec4 world = perDraw.model * vec4(aPosition, 1.0);
    vec3 n = normalize(mat3(perDraw.model) * aNormal);
    vec3 l = normalize(perDraw.lightDir.xyz);
    float diffuse = max(dot(n, l), 0.0);
    float ambient = 0.25;
    float light = ambient + diffuse * (1.0 - ambient);
    vColor = vec4(perDraw.baseColor.rgb * light, perDraw.baseColor.a);
    gl_Position = perDraw.viewProj * world;
}
