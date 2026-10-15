#version 450

// The skydome vertex stage (spec 092): a full sphere drawn behind the scene. The
// sphere is centred on the camera, so the view-projection carries the camera's
// rotation but no translation (see render/Skydome.fs), and the local position is
// enough for the fragment stage to shade the sky by direction.

layout(location = 0) in vec3 aPosition;

layout(location = 0) out vec3 vLocalPos;

// Vertex-stage uniform slot 0 (set = 1 in SDL_gpu's convention).
layout(set = 1, binding = 0) uniform PerDraw {
    mat4 viewProj;
} perDraw;

void main() {
    vLocalPos = aPosition;
    gl_Position = perDraw.viewProj * vec4(aPosition, 1.0);
}
