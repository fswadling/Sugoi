# Research: Remove Editor Scroll Zoom

## D1 — Remove the editor's wheel zoom and the input path that fed it

**Decision**: delete the `input.Wheel` dolly in `Editor.updateCamera` (`Camera.dolly &state.Camera
(MathF.Pow(0.9f, input.Wheel))`), and remove the wheel plumbing that existed only for it: the engine's
per-frame wheel accumulation (`if ev.Type = MouseWheel then wheel <- wheel + ev.Wheel.Y`), the `wheel`
parameter on `Input.gather`, and the `Wheel` field on `Input`.

**Rationale**: the wheel was accumulated engine-wide and applied to the camera unconditionally, so scrolling
**any** panel also dollied the camera — the reported collision. The events already reach ImGui through
`ImGuiSdl3.processEvent`, so panels scroll without the app-side value; once the camera is its only reader gone,
the field and its production are dead.

**Alternatives considered**: keep the field and simply stop using it (rejected — a set-but-never-read input is
dead code, against Minimalism); gate the dolly on "the pointer is over the viewport" (rejected — the user asked
to remove the zoom, and the viewport is a pass-through panel with no ImGui capture to test against).

## D2 — Panel scrolling is ImGui's own, and is verified untouched

**Decision**: change nothing about how wheel events reach the UI. The engine's event loop keeps forwarding every
event to `ImGuiSdl3.processEvent`; only the *separate* accumulation that fed `Input.Wheel` is removed.

**Rationale**: the collision exists precisely because there were **two** consumers — ImGui's scroll (desired)
and the camera dolly (unwanted). Removing the second leaves the first intact.

## D3 — Remove `Camera.dolly` too

**Decision**: delete `Camera.dolly` (and its two unit tests). It has exactly one production caller
(`Editor.updateCamera`) and no other reader.

**Rationale**: once D1 lands, `dolly` is an unused production function — dead code. Minimalism removes it. A
future zoom, if ever wanted, is a new control with its own input, chosen on purpose (spec Assumptions, "removal
over retention").

**Alternatives considered**: keep `dolly` as a camera primitive for a possible future zoom (rejected — that is
the just-in-case the constitution warns against; the two tests assert only the removed behaviour).

## D4 — No replacement control

**Decision**: add no key binding, slider or other zoom.

**Rationale**: the spec removes a control; fly is the navigation tool and the author does not want a scroll
zoom. Nothing else changes about camera navigation (orbit and fly are untouched).
