# Tracking

Status: MVP-C+ (face tracking, microphone / hybrid lip sync, upper body, arms, finger curl). External providers are not implemented.

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
- Head angles come from the facial transformation matrix. The plugin converts MediaPipe's right-handed matrix
  as S·M·S (z negated on both sides), so column 1 is the face's up axis in Unity camera space but column 2
  points out of the back of the head; the provider negates it to get the forward axis (verified on device:
  without the negation yaw sits at ±180° and wraps after calibration). Advanced settings still expose invert
  toggles per axis.
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

## Lip sync (PRD 13, 22)

`MicrophoneCaptureService` records a looping 1 s clip at 16 kHz through Unity's Microphone API and reads the new
samples every frame into `AudioLevelMeter` (Core): RMS → dBFS → noise gate with 3 dB hysteresis → normalized
level × sensitivity → attack (30 ms) / release (120 ms) envelope. Nothing is played back or routed; the
microphone used here is independent of OBS's audio (PRD 22).

`HybridLipSolver` (Core) rewrites the vowel expressions (aa/ih/ou/ee/oh) after the expression mapper:

- Camera: untouched.
- Microphone: aa = envelope.
- Hybrid (default, D-008): amplitude = 0.45 × camera opening + 0.55 × envelope while a face is present, envelope
  alone otherwise. While the gate hears silence the camera opening is limited to 30 % so landmark noise never
  opens the mouth (PRD 13.3). The camera's vowel proportions (aa : ou : oh) are kept; the amplitude is rescaled.
  With no microphone running, Hybrid behaves exactly like Camera.

## Upper body (PRD 14)

`MediaPipePoseProvider` runs Pose Landmarker (lite model, CPU, LIVE_STREAM) at 20 fps on frames ≤ 480 px wide,
sharing the camera with the face tracker. `PoseFrameBuilder` (Core) copies the 33 world landmarks and derives
torso angles from shoulders and hips: roll (left shoulder lower = lean toward the user's left), yaw (left shoulder
farther = turn toward the user's left), pitch (shoulders closer to the camera than hips = lean forward).
`BodyPoseSolver` applies a dead zone, gain, clamps (25° / 35° / 20°), smoothing and the same mirror rule as the
head, and glides to neutral when the body is lost. Calibrate also captures the neutral torso (median window).

**Arms** (modes "Upper Body + Arms" and "+ Fingers"): `ArmPoseSolver` (Core) produces upper-arm and forearm
directions in avatar space, smoothed, and returns an arm to the 70° rest pose when nothing tracks it.

- *Hand-anchored (preferred).* Whenever the hand landmarker has seen a hand in the last 0.3 s, its wrist is the
  target. The wrist's image position and the palm's apparent size, compared with the metric palm size from the
  hand world landmarks, give the hand's depth under a pinhole model with an assumed 60° horizontal field of view;
  the shoulders' metric width fixes the shoulder depth the same way. The resulting shoulder→wrist vector is scaled
  from the user's reach (learned from the pose, default 0.55 m) to the avatar's (bone lengths measured at load)
  and a two-bone IK places the elbow, using the pose elbow as the bend hint when it is visible and "down, slightly
  outward and back" otherwise. This is what keeps the arm natural when only the hand is clearly seen.
- *Switches.* "Solve arms from the palm position (IK)" (default on) turns the hand-anchored path off so the pose
  landmarks alone drive the arms; "Wrists follow the palm orientation" (default off until validated on more
  models) enables the hand-bone rotation from the palm axes.
- *Pose landmarks (fallback).* Without a hand, the shoulder → elbow and elbow → wrist world vectors are used
  directly, as before; an elbow visibility below 0.55 rests the arm and a hidden wrist keeps it straight.

Mirror mode makes the avatar the user's reflection: the user's left arm, seen on the left of the screen, drives
the avatar's *right* arm (the avatar faces the viewer, so its right side is screen-left) and image-right maps
to the avatar's +X. Copy mode (mirror off) drives left with left and flips the sign. `AvatarDriver` converts
the directions into local bone rotations with `FromToRotation` under the spine/chest chain, so the arms follow
the torso, and orients each hand bone from the palm axes (fingers direction and palm normal, wrist bend capped
at 85°) when the hand landmarker saw the hand.

**Which side is which.** Observed on the unmirrored macOS FaceTime feed: the pose model and the face
blendshapes name sides as they appear in a selfie mirror (its "left" is the user's real right), while the hand
landmarker names the real hands. `PoseFrameBuilder` therefore swaps the pose labels by default
(`BodyTrackingSettings.SwapSides`, "Swap body and arm sides" in the BODY section, default on) exactly like
`FaceFrameBuilder` does for the blendshapes, so every solver downstream works with the user's real sides and the
invert switches are no longer needed for a normal camera. `HandSideResolver` assigns each hand detection in three steps: continuity with the
hand tracked in the previous frame (nearest wrist within 0.18 image widths, so a hand keeps its side while it
moves and the detector may reorder its output), then the pose wrists (a wrist within 0.12 that is clearly nearer
than the other overrides), then MediaPipe's handedness label for a brand-new hand that no wrist claims (observed anatomical on this feed;
`HandSideResolver.LabelsAnatomical`). A brand-new detection farther than 0.35 image widths from every visible
pose wrist is a stray and is dropped, and two detections within 0.05 of each other are the same hand reported
twice, so a single raised hand can no longer drive both arms. Two detections never share a side. A camera that delivers a mirrored picture flips every label the same way and nothing in the
geometry can tell: the BODY section's "Swap left and right (arms and fingers)" toggle undoes that for both.

**Eyes and mouth sides.** MediaPipe's blendshape names ("eyeBlinkLeft", "mouthSmileLeft", "eyeLookOutLeft" …)
are image sides in a selfie mirror, so on the unmirrored feed `FaceFrameBuilder` swaps every Left/Right pair
before anything reads them (`FaceTrackingSettings.SwapEyes`, default on, "Swap eye sides" in the TRACKING
section). After that the frame is anatomical and the ordinary mirror rule (user's left eye → avatar's right
eye, on the same side of the screen) applies. Blink gain defaults to 2.5 because MediaPipe rarely reports a
closed eye above 0.5, especially with glasses.

**Overlay.** While tracking runs, the camera preview shows "P:L"/"P:R" at the pose wrists and "H:L→R"-style
tags at the hands (raw label → resolved side); it is the fastest way to see which side each tracker decided.

**Fingers** (mode "Upper Body + Arms + Fingers", default): `MediaPipeHandProvider` runs the Hand Landmarker
(`hand_landmarker.bytes`, up to two hands, CPU, 15 fps, 640 px input) and publishes 21 world landmarks plus the
wrist and palm-knuckle image positions per hand. `FingerCurl` (Core) sums the bend angles at the MCP, PIP and
DIP joints (thumb: MCP + IP) and maps 25°–195° to a curl of 0..1 (thumb 15°–100°); because single-camera depth
makes bends toward the camera read shallow, it also measures the tip-to-knuckle distance against the finger's
length (1 straight, about 0.45 closed) and keeps the larger of the two. Neither depends on the hand's orientation. `FingerCurlSolver` smooths per hand, assigns hands with the same mirror rule as the arms, and
relaxes a hand to a 0.1 curl 0.4 s after it disappears. `AvatarDriver.OnAvatarLoaded` captures, in the import
T-pose (palms down), each phalanx's rest rotation and the local axis that swings the finger toward the palm (the
thumb also toward the little finger); at runtime each of the 15 bones per hand rotates about that axis by
curl × (70° / 90° / 60°) for fingers and (20° / 40° / 55°) for the thumb. Finger spread is not tracked.

The BODY section has "Invert body tilt / turn / lean" switches (stored in the profile) for a camera or model whose
torso conventions disagree with the defaults, and a "Fingers:" status line that says at every moment why fingers
move or not (mode off, model missing, hand model starting, no finger bones on the VRM, no hand seen, or the
current curl per hand with the finger-bone count). The same facts are in the Copy Diagnostics report
(`Hands:` and `Body angles:` lines).

`AvatarDriver` applies the torso rotation half to Spine and half to Chest and subtracts it from the head chain,
because the head angles are camera-relative. Body tracking is on by default (PRD 34 `body_mode: upper_body`, extended
to arms and fingers) and can be reduced or turned off in the BODY section; without the tracking engine the
section is disabled.

## Not in MVP-C

The expression mapping editor UI, vowel classification from audio (A/I/U/E/O), finger spread, full body,
external providers (VMC/OSC/ARKit).
