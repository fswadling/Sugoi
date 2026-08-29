module Viewport

// The 3D viewport is the docking workspace's central (passthru) node, not an
// ImGui window. The grid + orbit camera land here in the DebugDraw render step
// (T008/T009): render to the swapchain before ImGui and read mouse input.
