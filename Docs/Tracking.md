# Tracking

Not implemented in MVP-A. This file records what the render foundation already fixes so that MVP-B
(face tracking) plugs in without touching rendering or output.

## Contract already in place

- `VRMCast.Core.Tracking.TrackingFrame` (PRD 8): head pose in radians, eye blink/look, mouth values,
  optional raw blendshape dictionary, optional pose/hand blocks, per-source confidence, timestamp.
- `ITrackingProvider` + `IFaceTrackingProvider` / `IPoseTrackingProvider` / `IHandTrackingProvider` /
  `IExternalTrackingProvider` (PRD 15). Providers start/stop and expose `TryGetLatest`.
- `LatestFrameBuffer<T>`: single-slot, thread-safe mailbox for worker → main thread hand-off (PRD 28).
- `LoadedAvatar.SetExpressionWeight(name, weight)` and `TryGetBone` as the only avatar entry points the
  motion solver will need.

## Plan for MVP-B (PRD 42)

1. Add MediaPipeUnityPlugin (pinned; CPU inference baseline on macOS, PRD 45 risk 1).
2. `CameraCaptureService` (WebCamTexture or plugin capture) producing a downscaled ~640×480 frame.
3. `MediaPipeFaceProvider : IFaceTrackingProvider` converting Face Landmarker output into
   `TrackingFrame` on the callback thread and publishing through `LatestFrameBuffer`.
4. `TrackingCoordinator` (main thread) reads the latest frame each Update.
5. `MotionSolver` (Core, pure): calibration offsets, smoothing, dead zones, gain, head→neck→chest→spine
   propagation. Unit-tested like the camera solver.
6. `ExpressionMapper` (Core, pure, data-driven) mapping blendshape names to avatar expression names.
7. Diagnostics: tracking FPS, inference ms, confidence.

Tracking cadence must stay independent of the render/output cadence (PRD 3.4).
