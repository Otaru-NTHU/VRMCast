# Tracking

Status: MVP-B (face tracking). Pose (MVP-C), hands and external providers are not implemented.

## Setup

The tracking engine is MediaPipeUnityPlugin (homuler) v0.16.3, distributed as a 290 MB UPM tarball that is
not committed. `Packages/manifest.json` references `file:com.github.homuler.mediapipe-0.16.3.tgz`; run

```
Scripts/setup-mediapipe.sh
```

once after cloning (it downloads the tarball into `Packages/` and verifies its SHA-256). The Face Landmarker
model with blendshapes ships inside the package (`PackageResources/MediaPipe/face_landmarker_v2_with_blendshapes.bytes`,
2.3 MB) and is referenced from the scene, so no separate model download is needed. Without the package the
project still compiles: the MediaPipe provider lives in its own assembly (`VRMCast.Tracking.MediaPipe`) that is
only built when the package is present, and the UI explains what to run.

## Pipeline (PRD 7, 29)

```
CameraCaptureService (WebCamTexture, 1280x720@30, selectable device, permission + recovery)
   | main thread, every 1/30 s
MediaPipeFaceProvider.Tick   -> TextureFrame (<= 640 px wide, blit-downscaled) -> FaceLandmarker.DetectAsync (CPU)
   | MediaPipe worker thread
FaceFrameBuilder (Core)      -> TrackingFrame (52 blendshapes, head basis -> pitch/yaw/roll, eyes, mouth)
   | LatestFrameBuffer (single slot, lock)
TrackingCoordinator.Tick     -> CalibrationSampler (while calibrating) -> MotionSolver.Update(dt)
   | main thread, every render frame
AvatarDriver                 -> LoadedAvatar.ApplyPose / SetLookAt / SetExpressionWeight
   |
UniVRM LateUpdate            (VRM 1.0: control rig -> skeleton, LookAt, expressions, SpringBone)
```

Tracking cadence (30 fps target, CPU inference) is independent of the render cadence; the solver interpolates
whatever the tracker last produced (PRD 3.4). No MediaPipe type crosses into the avatar layer (PRD 8).

## Conventions

- `TrackingFrame` is in the user's frame: Left/Right blendshapes are the user's own sides; yaw positive is the
  user turning toward their own left; pitch positive looks down; roll positive tilts the top of the head toward
  the image's right; LookX positive looks toward the user's right; LookY positive looks up.
- Head angles come from the facial transformation matrix (columns 1 and 2 = face up and forward axes in
  camera space after the plugin's z-flip). If a sign turns out wrong on a machine, Advanced settings expose
  invert toggles per axis; the defaults were derived analytically and must be confirmed in QA.
- `MotionSolver` converts to the avatar's frame. Mirror mode (default) makes the avatar behave like a mirror:
  user's left eye drives the avatar's right eye, yaw keeps its screen side, roll flips. Non-mirror flips yaw,
  roll and LookX instead.
- Bones are posed in normalized space (identity = T-pose facing +Z). VRM 0.x skeletons are normalized at import;
  VRM 1.0 is posed through the control rig. The idle pose lowers the upper arms by 70° so the avatar does not
  stand in a T-pose.
- Head rotation is distributed head 55 % / neck 25 % / chest 12 % / spine 8 % (sums to the tracked angle; the
  PRD's larger per-bone shares over-rotate the face and are left to tuning).
- Eye direction uses the avatar's own LookAt (VRM 1.0 `SetYawPitchManually`, VRM 0.x `VRMLookAtHead`), so it
  works for bone-based and expression-based eyes alike.

## Calibration (PRD 12)

Calibrate samples 0.75 s of frames, rejects frames without a confident face, needs at least 8 accepted frames,
and stores the median head angles, eye offsets and resting mouth values. Head angles and eye look are offset by
the calibration; jawOpen and smile are re-normalized above the resting value so the mouth closes at rest.

## Expression mapping (PRD 10)

`ExpressionMapping` rows (source, destination, gain, threshold, min, max, smoothing, invert, enabled) are data.
Basic mode: blinks, jawOpen → aa, smile → happy. Advanced adds pucker → ou, funnel → oh, brow-down → angry,
frown → sad, eye-wide / browInnerUp → surprised. Multiple sources merge with max; every row smooths
independently (frame-rate independent time constants). Destinations are canonical VRM 1.0 names; VRM 0.x
translates them (happy → Joy, blinkLeft → Blink_L, …). The editor UI for editing rows is MVP-D work; the data
model and defaults exist now.

## Loss of tracking

After 0.35 s without a face the solver glides head, eyes and expressions back to neutral over ~0.8 s instead
of snapping (PRD 37.3). Camera loss is reported in the CAMERA section and retried every 2 s without switching
devices (PRD 6.3).

## Diagnostics

Tracking result fps, inference latency (submit → callback), dropped frames and face confidence are in the
Diagnostics section and in the Copy Diagnostics report.

## Not in MVP-B

Pose Landmarker / upper body (MVP-C), microphone and hybrid lip sync (MVP-C), the mapping editor UI, hand tracking,
external providers (VMC/OSC/ARKit).
