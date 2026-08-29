#version 450

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec4 aColor;

layout(location = 0) out vec4 vColor;

layout(set = 1, binding = 0) uniform Uniforms {
    mat4 viewProj;
} u;

void main() {
    gl_Position = u.viewProj * vec4(aPosition, 1.0);
    vColor = aColor;
}
