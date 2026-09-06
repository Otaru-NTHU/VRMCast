# VRM Live Camera — PRD + Technical Specification + Claude Code Implementation Guide

**Document version:** 0.1  
**Target platform:** macOS on Apple Silicon  
**Primary machines:** M3–M5 class Macs, 64 GB RAM, current macOS  
**Primary use case:** Webcam / iPhone / external camera driven VRM avatar for OBS live streaming  
**Default output:** **1920 × 1080, 30 fps**  
**Product direction:** Personal long-term tool first; architecture suitable for later classroom/public distribution

---

## 1. Product summary

VRM Live Camera is a macOS application that:

1. Loads VRM 0.x and VRM 1.0 avatar files.
2. Detects the VRM version automatically whenever possible.
3. Captures a selectable camera source:
   - built-in Mac camera
   - external USB webcam
   - iPhone Continuity Camera
4. Tracks the user's:
   - face
   - head pose
   - eye motion / blinking
   - mouth
   - upper body
   - optionally pose / hands in later modes
5. Drives the VRM model in real time.
6. Supports camera-driven, microphone-driven, and hybrid lip sync.
7. Composites the avatar over:
   - solid color
   - image
   - chroma-key green
   - transparent output when the output path supports alpha
   - later: video / webcam / blur background
8. Provides camera framing controls:
   - Face
   - Bust
   - Half Body
   - Full Body
   - zoom
   - pan
   - orbit / rotate
   - FOV
9. Saves per-avatar profiles.
10. Supports calibration and per-expression tuning.
11. Supports expression hotkeys.
12. Can hide all controls during streaming.
13. Outputs video for OBS.
14. Eventually installs a real macOS virtual camera using Core Media I/O Camera Extension.
15. Keeps future interfaces open for OSC, VMC, MIDI, WebSocket, external mocap, ARKit/iPhone, NDI and Syphon.

The first objective is not to build a full VTube Studio replacement. The first objective is a stable, low-friction macOS VRM live camera that can be used reliably in real broadcasts.

---

# 2. Architecture decision

## 2.1 Recommended implementation stack

### Main application

**Unity, current supported LTS release**

Use Unity as the real-time runtime because the project requires:

- reliable 3D avatar rendering
- runtime VRM loading
- morph targets
- skeletal animation
- spring bones
- real-time camera input
- low-latency audio
- real-time tracking
- background compositing
- a render texture that can later be passed to native macOS output plugins

### VRM runtime

**UniVRM**

Requirements:

- runtime load VRM 0.x
- runtime load VRM 1.0
- automatic version detection
- humanoid bones
- expressions
- LookAt
- SpringBone
- runtime import
- Mac standalone

### Tracking

**MediaPipeUnityPlugin by Homuler**, using MediaPipe Tasks APIs.

Required tasks:

- Face Landmarker
- Pose Landmarker
- later: Hand Landmarker

Important implementation constraint:

- macOS CPU inference must be treated as the supported baseline.
- Do not assume MediaPipe GPU inference on macOS.
- Tracking resolution is independent of streaming output resolution.
- Feed MediaPipe a downscaled camera image; do not run tracking at 1080p unless profiling proves it is needed.

Suggested initial tracking input:

- 640 × 480 or equivalent aspect-aware crop
- 30 fps face tracking
- pose may run at a lower cadence such as 15–30 fps if required

### macOS native functions

Write native Swift / Objective-C++ plugins only where Unity should not own the implementation.

Native components:

1. macOS Camera Extension installer/activation helper
2. Core Media I/O virtual camera extension
3. shared frame transport between Unity and Camera Extension
4. optional future Syphon output
5. macOS-specific permissions / diagnostics if Unity APIs are insufficient

### UI

Start with Unity UI Toolkit.

Reason:

- avoids a split UI/application architecture during MVP
- can produce a clean desktop interface
- supports collapsible controls and live preview
- can later be replaced or supplemented by a native shell only if there is a concrete need

Do **not** create a SwiftUI shell in MVP.

---

## 2.2 Why not Swift + WKWebView for v1

Do not use the previously considered SwiftUI + WKWebView + three-vrm architecture as the primary v1 implementation.

Reasons:

- frame transfer from WebGL/WebGPU to a native virtual-camera pipeline adds unnecessary complexity
- native macOS MediaPipe distribution is not as turnkey as its Web and Unity routes
- a mixed native/Web renderer increases the number of synchronization and color-space failure modes
- Unity already provides a render texture suitable for later native frame transport

This architecture may be revisited if the project eventually needs a very small native App Store binary and the rendering stack becomes the main limiting factor.

---

## 2.3 Native Swift / Metal route

Native Swift remains a possible future route.

Interesting libraries currently include:

- VRMKit
- VRMMetalKit

However, they are not the baseline for v1.

Reason:

The project needs the combination of VRM compatibility, live tracking, expression mapping, physics, camera control, OBS output and public distribution. Unity + UniVRM currently reduces integration risk.

---

# 3. Product principles

The application must follow these principles.

## 3.1 Simple by default

A new user should be able to:

1. launch the app
2. load a `.vrm`
3. select a webcam
4. press Calibrate
5. select a background
6. start output

without configuring a tracking graph.

## 3.2 Advanced controls are optional

The main view should expose only frequently used controls.

Detailed mapping, smoothing, OSC, debug landmarks and technical output settings belong under Advanced.

## 3.3 Profiles are first-class

The application should remember:

- avatar
- camera
- tracking mode
- lip-sync mode
- calibration
- expression mapping
- camera framing
- background
- output settings
- hotkeys

## 3.4 Render and tracking must be separated

Tracking frequency must not control render frequency.

Example:

- render: 60 fps
- camera capture: 30 fps
- face tracking: 30 fps
- pose tracking: 20 fps
- audio: continuous callback

Tracking data is interpolated/smoothed by the avatar solver.

## 3.5 Outputs are consumers

The renderer must not know about OBS.

Use:

```text
Avatar + Background
        |
        v
Final RenderTexture
        |
        v
Frame Output Bus
   |       |        |
Preview  Virtual   Future
         Camera    Syphon/NDI
```

This is a critical architectural constraint.

---

# 4. User modes

## 4.1 Standard mode

Default interface.

Shows:

- avatar preview
- Load VRM
- Camera selector
- Tracking mode
- Lip Sync mode
- Background
- Calibrate
- framing preset
- output status
- Start Output

## 4.2 Advanced mode

Adds:

- face mapping editor
- smoothing
- gain
- thresholds
- tracking debug
- pose settings
- microphone sensitivity
- hybrid lip weights
- FPS/resolution
- renderer quality
- physics settings
- OSC/WebSocket later

## 4.3 Performance mode

For live broadcast.

Behavior:

- hide control panels
- disable tracking landmark visualization
- disable source-camera preview if not required
- reduce nonessential UI update frequency
- keep avatar rendering and output active
- display only optional status overlay:
  - FPS
  - tracking state
  - audio state
  - output state

## 4.4 Presentation / operator mode

Optional later feature.

Allows one machine/operator to manage:

- avatar
- expressions
- profiles
- output

while a separate clean window shows only the live scene.

Not required for MVP.

---

# 5. Supported VRM behavior

## 5.1 Versions

Required:

- VRM 0.x
- VRM 1.0

Automatic detection is the default.

If automatic detection fails:

Advanced import dialog:

```text
VRM Version
[ Auto Detect ▼ ]

Options:
Auto Detect
Force VRM 0.x
Force VRM 1.0
```

Do not show this selector during normal successful loading.

## 5.2 Import flow

Accept:

- File > Open
- Load VRM button
- drag and drop `.vrm`

On load:

1. validate file
2. detect VRM version
3. read metadata
4. load avatar
5. initialize humanoid
6. initialize expressions
7. initialize spring bones
8. inspect available expressions
9. show model
10. load matching profile if one exists

## 5.3 Import errors

User-facing errors must be actionable.

Examples:

- "This file is not a valid VRM file."
- "VRM metadata could not be read."
- "The avatar has no compatible humanoid definition."
- "The model loaded, but SpringBone data is invalid. Physics has been disabled."

Never show only a stack trace to a normal user.

---

# 6. Camera capture

## 6.1 Required devices

Device list should support all macOS cameras visible to the application, including:

- Mac built-in camera
- external USB webcams
- iPhone Continuity Camera

Do not hard-code device names.

## 6.2 Camera settings

UI:

```text
Camera
[ FaceTime HD Camera ▼ ]

Tracking Camera Resolution
[ Auto ▼ ]

Mirror Preview
[✓]

Tracking FPS
[ 30 ▼ ]
```

The displayed avatar output should not mirror automatically just because the camera preview is mirrored.

## 6.3 Camera loss

If camera disconnects:

- freeze or smoothly return avatar to neutral
- show non-blocking status
- try device recovery
- do not crash
- do not automatically switch to a different camera without notifying the user

---

# 7. Tracking pipeline

Core pipeline:

```text
Camera
  |
  +--> Face Landmarker
  |
  +--> Pose Landmarker
  |
  +--> optional Hand Landmarker
             |
             v
       TrackingFrame
             |
             v
        MotionSolver
             |
             v
          Avatar
```

---

# 8. TrackingFrame data contract

All tracking engines must output a common structure.

Conceptual C# contract:

```csharp
public struct TrackingFrame
{
    public double Timestamp;

    public HeadTracking Head;
    public EyeTracking Eyes;
    public MouthTracking Mouth;

    public Dictionary<string, float> Blendshapes;

    public PoseTracking? Pose;
    public HandTracking? LeftHand;
    public HandTracking? RightHand;

    public float FaceConfidence;
    public float PoseConfidence;
}
```

Suggested normalized ranges:

- expression values: 0...1
- blink: 0...1
- mouth open: 0...1
- look X/Y: -1...1
- head rotations: radians internally
- positions: normalized or meters, documented per field

No provider-specific MediaPipe object may be passed directly into the VRM runtime.

---

# 9. Face tracking modes

## 9.1 Basic

Optimized for stability.

Drive:

- head pitch
- head yaw
- head roll
- blink left
- blink right
- eye look X
- eye look Y
- mouth open
- smile

Optional:

- brow
- cheek

## 9.2 Advanced

Uses all useful Face Landmarker blendshape coefficients.

Do not assume every avatar supports every expression.

Pipeline:

```text
MediaPipe Face Blendshapes
          |
          v
Expression Mapping
          |
          v
VRM Expressions
```

The expression mapper is responsible for adapting tracking outputs to each avatar.

---

# 10. Expression Mapping Editor

Required before public release; a minimal version should exist in MVP.

Each mapping supports:

- source
- destination
- gain
- threshold
- minimum
- maximum
- smoothing
- invert
- enabled

Example:

```text
jawOpen
 -> VRM aa
Gain: 0.70
Threshold: 0.08
Min: 0
Max: 1
Smoothing: 0.35
```

## 10.1 Mapping behavior

Multiple sources may drive one destination.

Example:

```text
happy =
max(
  mouthSmileLeft,
  mouthSmileRight
)
```

Multiple destinations may be driven from one source.

Mapping calculations must be data-driven.

Do not bury expression coefficients in MonoBehaviour code.

---

# 11. Motion solver

MotionSolver is separate from tracking.

Responsibilities:

- neutral calibration offsets
- smoothing
- dead zones
- gain
- interpolation
- pose confidence handling
- natural propagation through head / neck / chest / spine

Suggested default head propagation:

```text
Head   100%
Neck    55–70%
Chest   15–30%
Spine    5–15%
```

Exact defaults should be tuned empirically.

---

# 12. Calibration

Primary button:

**Calibrate**

Instruction:

```text
Look naturally toward the camera.
Relax your face and shoulders.
```

Capture a short neutral window rather than a single frame.

Suggested:

- 0.5–1.0 second sampling
- reject low-confidence frames
- calculate robust mean / median neutral values

Store:

- head neutral rotation
- eye neutral
- mouth neutral
- shoulder pose
- face scale if needed

Calibration belongs to Profile.

---

# 13. Lip sync

UI:

```text
Lip Sync
[ Hybrid ▼ ]

Camera
Microphone
Hybrid
```

## 13.1 Camera mode

Uses tracked mouth geometry / blendshapes.

Primary values:

- jawOpen
- mouthPucker
- mouthFunnel
- smile
- optional vowel-related inference

## 13.2 Microphone mode

MVP:

- microphone RMS / envelope drives mouth-open
- attack/release smoothing
- noise gate
- sensitivity

Later:

- A/I/U/E/O or viseme classification

Do not make speech recognition a requirement for MVP.

## 13.3 Hybrid mode

Default lip-sync mode after calibration.

Concept:

```text
shape = camera mouth shape
energy = microphone speaking energy

final mouth = combine(shape, energy)
```

Initial weighting:

```text
camera weight: 0.45
audio weight:  0.55
```

Expose advanced sliders.

Hybrid algorithm must suppress false mouth opening when:

- user is silent
- camera detects poor landmarks
- microphone detects only low background noise

---

# 14. Upper-body tracking

MVP goal:

- head
- neck
- shoulders
- chest
- upper spine

Do not require full-body tracking for first live-ready release.

Pose Landmarker can be enabled independently from face tracking.

UI:

```text
Body Tracking
[ Upper Body ▼ ]

Off
Upper Body
Full Body (Experimental)
```

---

# 15. Full-body / hands

Architecture must reserve these providers:

```csharp
ITrackingProvider
IFaceTrackingProvider
IPoseTrackingProvider
IHandTrackingProvider
IExternalTrackingProvider
```

Potential later inputs:

- MediaPipe Pose
- MediaPipe Hands
- iPhone / ARKit
- VMC protocol
- OSC
- Rokoko
- Xsens
- OptiTrack
- Vive trackers
- AI mocap systems

Do not implement all of these in MVP.

---

# 16. Avatar camera

## 16.1 Presets

Required:

- Face
- Bust — **default**
- Half Body
- Full Body

## 16.2 Controls

- scroll: zoom
- middle/right drag or explicit UI gesture: pan
- orbit/rotation mode
- FOV
- Reset Camera
- Reset Avatar Orientation

Input behavior must be documented and must not conflict with UI dragging.

## 16.3 Output framing

Camera settings are stored per profile.

Changing output resolution must preserve framing based on aspect ratio.

---

# 17. Background compositor

Required modes:

```text
Solid Color
Image
Chroma Key
Transparent
```

## 17.1 Chroma key

Default key color:

```text
#00FF00
```

User can choose a different color.

Chroma mode is the safest compatibility baseline for OBS.

## 17.2 Transparent

Renderer must support alpha internally.

Transparent output is enabled only for output transports that actually preserve alpha.

Do not promise alpha through standard virtual-camera formats unless the chosen pixel format/client path is verified to support it.

## 17.3 Image

Support:

- PNG
- JPG/JPEG
- optionally HEIC if Unity/macOS import path is reliable

Fit modes:

- Fill
- Fit
- Stretch

Default: Fill.

## 17.4 Later

Phase 2/3:

- video
- webcam
- webcam blur
- screen capture
- web page
- NDI input

---

# 18. Renderer

## 18.1 Default output

**1920 × 1080 / 30 fps**

This is the product default.

## 18.2 Optional output presets

```text
1280 × 720
1920 × 1080
2560 × 1440
3840 × 2160
```

FPS options:

```text
30
60
```

Not every resolution/FPS pair must be enabled by default.

Suggested UI:

```text
Output Quality
[ 1080p 30 — Recommended ▼ ]

720p 30
720p 60
1080p 30
1080p 60
1440p 30
1440p 60
4K 30
Custom...
```

## 18.3 Internal render target

Use a dedicated final RenderTexture.

Do not use the Game window or UI framebuffer as the broadcast frame source.

Suggested:

```text
Avatar Camera
       +
Background
       |
       v
FinalCompositor
       |
       v
OutputRenderTexture
```

---

# 19. Output subsystem

Define:

```csharp
public interface IFrameOutput
{
    string Name { get; }
    bool IsAvailable { get; }

    void Start(OutputConfiguration config);
    void SubmitFrame(RenderTexture texture, double timestamp);
    void Stop();
}
```

Initial implementations:

```text
PreviewOutput
NullOutput / DebugOutput
```

Next implementation:

```text
MacVirtualCameraOutput
```

Future:

```text
SyphonOutput
NDIOutput
RecorderOutput
```

---

# 20. macOS virtual camera

## 20.1 Technology

Use Apple's **Core Media I/O Camera Extension** architecture.

The extension should publish a software camera device such as:

**VRM Live Camera**

The camera extension is installed/activated with the app.

## 20.2 Native architecture

```text
Unity Application
      |
      v
Final RenderTexture
      |
      v
Native Frame Bridge
      |
      v
IOSurface / shared pixel-buffer strategy
      |
      v
Camera Extension
      |
      v
CMIOExtensionStream
      |
      v
OBS / FaceTime / supported apps
```

The exact frame-sharing mechanism must be validated in a dedicated technical spike before implementation.

Preferred properties:

- zero-copy or minimal-copy
- bounded latency
- no PNG/JPEG encoding
- no network loopback
- deterministic frame pacing

## 20.3 Activation UX

App needs a page:

```text
Virtual Camera

Status: Not Installed

[ Install / Enable Virtual Camera ]

Instructions / status
```

Handle:

- extension activation request
- permission/approval requirement
- missing admin approval
- activation failure
- restart if required by OS behavior

Do not silently fail.

## 20.4 Virtual camera phase

Virtual Camera is **not** the first feature to implement.

It starts only after:

- VRM render stable
- tracking stable
- compositor stable
- fixed RenderTexture output stable

---

# 21. OBS integration

Primary target:

OBS reads the output.

Two supported workflows over project life:

### A. Virtual camera device

Ideal public-user workflow.

```text
OBS
 -> Video Capture Device
 -> VRM Live Camera
```

### B. Chroma workflow

Output contains user-selected green background.

OBS applies Chroma Key.

This remains the compatibility fallback.

A future Syphon output may offer better local compositor performance and alpha behavior, but is not required for the first live-ready milestone.

---

# 22. Audio

Required:

- microphone selector
- permission handling
- input level meter
- sensitivity
- noise gate
- lip-sync attack
- lip-sync release

The microphone used for avatar mouth motion is independent from whatever microphone OBS uses for broadcast audio.

Do not route or monitor broadcast audio in MVP.

---

# 23. Profiles

Profile object stores:

```text
Profile
├── profile metadata
├── VRM path / reference
├── VRM detected version
├── camera device preference
├── microphone device preference
├── face tracking mode
├── body tracking mode
├── calibration
├── expression mappings
├── lip sync
├── avatar camera
├── background
├── renderer
├── output
└── hotkeys
```

## 23.1 Profile behavior

Support:

- New
- Save
- Save As
- Duplicate
- Rename
- Delete
- Auto-save optional

## 23.2 Missing files

If model/background path is missing:

- show profile with missing-resource badge
- allow relink
- do not delete profile

## 23.3 Pack Profile

Later feature.

Exports:

```text
.profile package
├── profile JSON
├── VRM if redistribution/license permits
└── background assets
```

The app must not assume every VRM model license allows redistribution.

---

# 24. Expression hotkeys

Required basic system.

Each action:

- expression
- intensity
- trigger mode

Trigger modes:

- Hold
- Toggle
- One Shot

Example:

```text
1 -> Happy
2 -> Angry
3 -> Sad
4 -> Surprised
5 -> Neutral
```

Hotkeys must work when the preview window is not focused if macOS permissions allow the selected implementation.

Global hotkeys may be deferred if system permission complexity threatens MVP.

---

# 25. External control interfaces

Do not implement all in v1.

Design service boundary:

```csharp
public interface IExternalControlProvider
{
    void Start();
    void Stop();
    event Action<ExternalControlMessage> MessageReceived;
}
```

Priority order after MVP:

1. OSC
2. WebSocket
3. VMC
4. MIDI
5. NDI / Syphon-related controls if needed

---

# 26. Suggested repository structure

```text
VRMLiveCamera/
│
├── README.md
├── LICENSE
├── THIRD_PARTY_NOTICES.md
├── Docs/
│   ├── PRD.md
│   ├── Architecture.md
│   ├── Tracking.md
│   ├── VirtualCamera.md
│   └── QA.md
│
├── UnityProject/
│   ├── Assets/
│   │   └── VRMLiveCamera/
│   │       ├── App/
│   │       ├── UI/
│   │       ├── Core/
│   │       ├── Avatar/
│   │       ├── Tracking/
│   │       │   ├── Contracts/
│   │       │   ├── MediaPipe/
│   │       │   ├── Solvers/
│   │       │   └── Calibration/
│   │       ├── Audio/
│   │       ├── Rendering/
│   │       ├── Backgrounds/
│   │       ├── Profiles/
│   │       ├── Hotkeys/
│   │       ├── Output/
│   │       ├── ExternalControl/
│   │       ├── Diagnostics/
│   │       └── Tests/
│   │
│   └── Packages/
│
├── Native/
│   └── macOS/
│       ├── FrameBridge/
│       ├── CameraExtension/
│       └── InstallerHelper/
│
└── Scripts/
    ├── build-macos.sh
    ├── package.sh
    └── verify-dependencies.sh
```

---

# 27. Core C# service architecture

Do not build the application as many MonoBehaviours that find each other globally.

Use services/interfaces.

Suggested:

```text
AppController
│
├── AvatarService
├── CameraCaptureService
├── FaceTrackingService
├── PoseTrackingService
├── AudioTrackingService
├── TrackingCoordinator
├── MotionSolver
├── LipSyncSolver
├── RenderService
├── BackgroundService
├── ProfileService
├── HotkeyService
├── OutputService
└── DiagnosticsService
```

Avoid:

```csharp
FindObjectOfType<T>()
GameObject.Find()
Resources.Load()
```

in core runtime logic.

Dependency injection can be lightweight and manual. A DI framework is not required for MVP.

---

# 28. Threading

Expected responsibilities:

### Main thread

- Unity object mutation
- avatar bones
- expressions
- UI
- render orchestration

### Tracking worker / MediaPipe callbacks

- face inference
- pose inference
- build immutable tracking result

### Audio thread/callback

- capture amplitude/spectrum
- publish compact audio metrics

Never mutate Unity GameObjects from MediaPipe/audio worker callbacks.

Use a thread-safe latest-frame buffer.

---

# 29. Data flow

```text
Webcam
  |
  +---------------------+
  |                     |
Face Task            Pose Task
  |                     |
  +----------+----------+
             |
             v
       TrackingCoordinator
             |
             v
         Calibration
             |
             v
         MotionSolver
             |
        +----+-----+
        |          |
      Face       Body
        |          |
        +----+-----+
             |
             v
            VRM
             |
          Renderer
             |
      Background Composite
             |
             v
      OutputRenderTexture
             |
       OutputService
```

Audio:

```text
Microphone -> AudioLipProvider
                      |
Camera Mouth ----------+
                      |
                      v
                HybridLipSolver
                      |
                      v
                 VRM Mouth
```

---

# 30. Performance strategy

The app targets Apple Silicon.

Default:

- output: 1920×1080 @ 30
- camera capture: preferred 720p or camera-native mode
- face inference input: approx. 640×480
- face inference: up to 30 fps
- pose inference: 15–30 fps
- render: 30 or 60 depending selected output

Do not run AI inference at output resolution.

## 30.1 Adaptive quality

Later:

If frame time exceeds threshold:

1. reduce tracking input resolution
2. reduce pose frequency
3. preserve face tracking
4. preserve renderer output if possible
5. warn user before changing output resolution

Do not silently reduce broadcast resolution.

---

# 31. Diagnostics

A Diagnostics panel is required before public beta.

Display:

- render FPS
- tracking FPS
- face inference ms
- pose inference ms
- camera resolution
- render resolution
- dropped tracking frames
- dropped output frames
- face confidence
- pose confidence
- microphone level
- avatar model stats if available
- output status

Provide:

**Copy Diagnostics**

Never include camera frames, audio, or personal file contents in diagnostics.

---

# 32. Privacy

Expected design:

- tracking processing stays on device
- webcam images are not uploaded
- microphone audio is not uploaded
- profiles stay local
- no account required for MVP

If telemetry is ever added:

- opt-in
- document exactly what is collected
- never transmit image/audio frames

---

# 33. Main UI specification

Approximate desktop layout:

```text
┌──────────────────────────────────────────────────────────────────┐
│ VRM Live Camera                              Profile: Default ▼  │
├────────────────┬─────────────────────────────────────────────────┤
│ MODEL          │                                                 │
│ avatar.vrm     │                                                 │
│ [Load VRM]     │                                                 │
│                │                                                 │
│ CAMERA         │                                                 │
│ FaceTime ... ▼ │                                                 │
│                │                  AVATAR                         │
│ TRACKING       │                  PREVIEW                        │
│ Basic       ▼  │                                                 │
│ Body: Upper ▼  │                                                 │
│                │                                                 │
│ LIP SYNC       │                                                 │
│ Hybrid      ▼  │                                                 │
│                │                                                 │
│ BACKGROUND     │                                                 │
│ Chroma      ▼  │                                                 │
│                │                                                 │
│ [ Calibrate ]  │                                                 │
├────────────────┴─────────────────────────────────────────────────┤
│ 1920×1080 | 30 FPS | Face ● | Audio ● | Output ○  [Start Output]│
└──────────────────────────────────────────────────────────────────┘
```

Top-level actions:

- File
- Profile
- View
- Output
- Help

View:

- Standard
- Advanced
- Performance Mode
- Fullscreen Preview

---

# 34. Default settings

```yaml
output:
  width: 1920
  height: 1080
  fps: 30

tracking:
  face_mode: basic
  face_fps: 30
  body_mode: upper_body
  pose_fps: 20
  smoothing: medium

lip_sync:
  mode: hybrid
  camera_weight: 0.45
  microphone_weight: 0.55

avatar_camera:
  preset: bust

background:
  mode: chroma
  chroma_color: "#00FF00"

ui:
  mode: standard
```

These are initial defaults, not immutable constants.

---

# 35. MVP scope

## MVP-A — Render foundation

Required:

- Unity project boots
- UniVRM installed
- drag/drop or Load VRM
- VRM 0.x load
- VRM 1.0 load
- automatic version detection
- SpringBone update
- avatar preview
- camera framing
- solid background
- image background
- chroma background
- 1920×1080 render target
- 30 fps stable target

No tracking yet.

## MVP-B — Face tracking

Required:

- selectable camera
- MediaPipe Face Landmarker
- head tracking
- blinking
- eye look
- mouth open
- smile
- calibration
- smoothing
- basic / advanced switch

## MVP-C — Lip + upper body

Required:

- microphone selector
- camera lip sync
- microphone lip sync
- hybrid lip sync
- noise gate
- Pose Landmarker
- shoulder / upper torso motion

## MVP-D — Profiles / usability

Required:

- profile save/load
- mapping settings
- expression hotkeys
- hide UI
- performance mode
- diagnostics
- settings persistence
- graceful camera/microphone recovery

At this point the app should be usable as a live avatar tool.

## MVP-E — OBS output

Required:

- fixed OutputRenderTexture
- native frame bridge spike
- Camera Extension spike
- virtual camera published in macOS
- OBS sees VRM Live Camera
- 1080p30 stable stream
- activation/permission UI
- failure recovery

---

# 36. Explicit non-goals for first milestone

Do **not** implement before MVP-A/B work:

- NDI
- full-body IK perfection
- hand tracking
- MIDI
- VMC
- OSC
- WebSocket
- cloud AI
- speech recognition
- automatic emotion classification
- video background
- screen capture
- built-in recording suite
- scene editor
- multiple avatars
- multi-camera director
- mobile companion app
- automatic updater
- plugin marketplace

These features are allowed only after the foundation is stable.

---

# 37. Acceptance criteria

## 37.1 VRM

Pass if:

- at least two known VRM 0.x models load
- at least two known VRM 1.0 models load
- version is detected automatically
- model orientation is correct
- expressions can be driven
- SpringBone works where present
- unloading/reloading does not leak or duplicate avatars

## 37.2 Camera

Pass if:

- built-in camera works
- external camera works when attached
- Continuity Camera works when exposed by macOS
- switching camera does not require app restart
- unplugging camera does not crash

## 37.3 Tracking

Pass if:

- head motion visually follows user
- blinking is independent left/right if source data supports it
- mouth closes correctly at rest
- calibration fixes a tilted neutral pose
- loss of tracking returns smoothly rather than snapping violently

## 37.4 Lip sync

Pass if:

- camera mode responds without microphone
- mic mode responds without face visibility
- hybrid mode responds to both
- background room noise does not constantly open mouth after calibration/gate tuning

## 37.5 Renderer

Pass if:

- default is exactly 1920×1080 / 30 fps
- output aspect ratio is 16:9
- UI is not included in OutputRenderTexture
- chroma background is clean
- image background scales correctly
- transparent internal render can be produced

## 37.6 OBS / virtual camera

Pass if:

- macOS publishes a camera device named VRM Live Camera
- OBS can select it
- OBS receives 1920×1080 frames
- observed frame cadence is ~30 fps
- app UI can remain hidden while output continues
- stopping output produces a defined fallback frame rather than corrupted memory
- restarting OBS does not require reinstalling the extension

---

# 38. Testing hardware matrix

Minimum development test matrix:

```text
Apple Silicon:
- one M3-class Mac
- one M4/M5-class Mac when available to tester

Camera:
- built-in Mac camera
- common USB webcam
- iPhone Continuity Camera

VRM:
- VRM 0.x simple
- VRM 0.x complex
- VRM 1.0 simple
- VRM 1.0 complex

OBS:
- current stable OBS on macOS
```

Do not optimize only against one avatar.

---

# 39. Third-party dependency policy

All dependencies must be:

- documented
- version pinned after validation
- license reviewed
- listed in THIRD_PARTY_NOTICES.md

Core expected dependencies:

- Unity
- UniVRM
- MediaPipeUnityPlugin / MediaPipe
- Apple Core Media I/O APIs

Do not automatically upgrade packages during development without a dedicated dependency-update commit.

---

# 40. Git policy for coding agents

Claude Code must:

- make small commits
- not rewrite unrelated systems
- not upgrade Unity automatically
- not upgrade UniVRM automatically
- not replace MediaPipe implementation without explicit reason
- not introduce paid assets/plugins
- not add network dependencies for runtime tracking
- write tests around pure C# solver/mapping logic
- update Docs/Architecture.md when architecture changes

Suggested branches:

```text
main
develop
feature/render-foundation
feature/vrm-loader
feature/face-tracking
feature/lip-sync
feature/profiles
feature/mac-camera-extension
```

---

# 41. First Claude Code implementation assignment

Copy the following prompt to Claude Code after creating an empty project/repository.

---

## CLAUDE CODE PROMPT — PHASE 1

You are implementing **VRM Live Camera**, a macOS Apple-Silicon desktop application for driving VRM avatars from a camera and eventually exposing the composited result as a virtual camera for OBS.

Read all project documentation before modifying files.

### Non-negotiable architecture

The initial implementation uses:

- Unity, current supported LTS
- C#
- UniVRM for VRM runtime loading/rendering
- MediaPipeUnityPlugin later for tracking
- Unity UI Toolkit for the application UI
- a dedicated `OutputRenderTexture`
- macOS native Camera Extension only in a later milestone

Do not introduce Electron, Tauri, a browser renderer, Python runtime, Node server, cloud inference, or paid Unity packages.

### Current assignment

Implement only **MVP-A: Render Foundation**.

Required outcomes:

1. Establish clean project directory structure under `Assets/VRMLiveCamera`.
2. Implement `AvatarService`:
   - runtime load `.vrm`
   - VRM 0.x
   - VRM 1.0
   - automatic detection through UniVRM
   - unload previous avatar cleanly
   - report friendly loading errors
3. Implement the render scene:
   - dedicated avatar camera
   - dedicated compositor/output RenderTexture
   - default 1920×1080
   - default 30 fps
   - UI must never appear in output RenderTexture
4. Implement avatar-camera presets:
   - Face
   - Bust (default)
   - Half Body
   - Full Body
   - Reset
5. Implement adjustable:
   - zoom
   - pan
   - FOV
   - avatar rotation/orbit where safe
6. Implement backgrounds:
   - solid color
   - image
   - chroma key
   - transparent internal rendering
7. Implement a minimal UI Toolkit desktop layout with:
   - Load VRM
   - model name/status
   - preview
   - framing preset
   - background mode
   - output resolution/FPS display
8. Create interfaces now for:
   - `ITrackingProvider`
   - `IFrameOutput`
   but do not implement MediaPipe or virtual camera yet.
9. Implement basic diagnostics:
   - render FPS
   - selected output width/height/fps
10. Add pure C# tests where appropriate.
11. Create/update:
   - `Docs/Architecture.md`
   - `Docs/QA.md`
   - `THIRD_PARTY_NOTICES.md`

### Defaults

Output:
- 1920×1080
- 30 fps

Avatar camera:
- Bust

Background:
- Chroma
- #00FF00

### Engineering constraints

- Do not use `GameObject.Find` or `FindObjectOfType` in core runtime logic.
- Do not make unrelated global singletons.
- Do not let UI scripts directly manipulate UniVRM objects.
- UI calls services.
- Keep Unity-object mutation on the main thread.
- Do not start tracking work in this phase.
- Do not start virtual-camera work in this phase.
- Do not add speculative features.
- Use serialized references or explicit composition/bootstrap.
- Fail gracefully when no VRM is loaded.

### Definition of done

The milestone is complete when a macOS standalone build can:

1. launch,
2. load either a VRM 0.x or VRM 1.0 file at runtime,
3. display the avatar,
4. switch between the required background modes,
5. change avatar framing,
6. maintain a 1920×1080 OutputRenderTexture,
7. run with a 30 fps target,
8. hide/omit application UI from the output texture,
9. unload and load another VRM without restarting.

Before declaring completion:

- run tests,
- build for macOS Apple Silicon,
- list any known failures,
- do not claim VRM 0.x/1.0 support unless both were actually tested.

At the end, provide:
- files changed
- architecture decisions made
- tests run
- build result
- known issues
- exact manual QA steps

---

# 42. Second Claude Code assignment

Only use after MVP-A passes.

## CLAUDE CODE PROMPT — PHASE 2 FACE TRACKING

Implement MVP-B Face Tracking without changing the established rendering/output architecture.

Requirements:

- integrate MediaPipeUnityPlugin
- Face Landmarker
- selectable camera source
- Apple Silicon macOS standalone build
- Basic and Advanced modes
- TrackingFrame contract
- thread-safe latest tracking frame
- MotionSolver
- calibration
- smoothing
- head pose
- eye blink
- eye look
- mouth open
- smile
- expression mapping
- diagnostics

Tracking inference resolution must be independent of the 1920×1080 output resolution.

Default face tracking target: 30 fps.

Do not implement Pose Landmarker, microphone lip sync or virtual camera in this milestone.

Before declaring success, test with at least:
- one VRM 0.x
- one VRM 1.0
- built-in Mac camera
- macOS standalone build

---

# 43. Virtual camera technical spike prompt

Do not use until rendering and tracking milestones are stable.

## CLAUDE CODE PROMPT — CAMERA EXTENSION SPIKE

Create an isolated macOS proof-of-concept for a Core Media I/O Camera Extension.

Goal:

Prove that a host macOS application can publish a software camera named `VRM Live Camera` and stream generated 1920×1080 30fps test frames to OBS.

Do not connect Unity yet.

The spike must determine:

1. Camera Extension Xcode target structure.
2. entitlements required.
3. App Group/shared communication design.
4. activation flow.
5. admin/user approval behavior.
6. supported pixel format.
7. frame timing.
8. whether IOSurface can be used for efficient host-to-extension frame sharing.
9. fallback shared-memory/CVPixelBuffer strategy if required.
10. behavior when host stops sending frames.
11. behavior when OBS opens/closes the camera.
12. packaging/signing constraints.

Definition of done:

- camera appears in OBS
- OBS displays generated test pattern
- 1920×1080
- 30 fps
- at least 10 minutes without stream failure
- clean start/stop
- implementation notes documented in `Docs/VirtualCamera.md`

Do not couple the spike to Unity until this proof-of-concept passes.

---

# 44. Implementation sequence

Recommended order:

```text
0. Repository + dependency pinning
1. VRM runtime load
2. RenderTexture compositor
3. Backgrounds
4. Avatar camera
5. UI shell
6. Face tracking
7. Calibration / smoothing
8. Expression mapper
9. Microphone lip sync
10. Hybrid lip solver
11. Upper-body pose
12. Profiles
13. Hotkeys
14. Performance mode
15. Diagnostics
16. Camera Extension isolated spike
17. Unity/native frame bridge
18. OBS virtual camera validation
19. packaging / signing / public beta
20. optional OSC / Syphon / VMC
```

Do not change this sequence casually.

---

# 45. Main technical risks

## Risk 1 — MediaPipe macOS packaging

MediaPipe on Apple Silicon macOS is supported by the selected Unity plugin route, but desktop GPU mode should not be assumed.

Mitigation:

- CPU baseline
- low tracking input resolution
- decouple tracking/render FPS
- benchmark early
- pin a known plugin build

## Risk 2 — Virtual camera frame bridge

This is the highest-risk native integration.

Mitigation:

- isolated Camera Extension spike first
- generated test pattern before Unity
- evaluate IOSurface/shared buffers
- no encoded image transfer
- integrate only after 1080p30 extension is proven

## Risk 3 — VRM model variance

Different creators use different expressions and tuning.

Mitigation:

- auto mapping defaults
- per-avatar expression mapping editor
- calibration
- profiles

## Risk 4 — tracking jitter

Mitigation:

- calibration
- smoothing
- dead zones
- confidence gating
- decouple tracking/render cadence

## Risk 5 — public distribution / signing

Camera Extension packaging requires proper Apple signing and permissions.

Mitigation:

- test Developer ID signing before public beta
- document first-launch approval flow
- do not postpone signing validation until the final week

---

# 46. Public beta target

A reasonable first public beta should provide:

- drag/drop VRM
- VRM 0.x + 1.0
- built-in/USB/Continuity cameras
- face/head/eyes/mouth
- upper body
- hybrid lip sync
- background color/image/chroma
- 1080p30 default
- optional 1080p60
- profiles
- calibration
- hotkeys
- clean streaming UI
- virtual camera
- OBS-tested onboarding
- diagnostics
- no required cloud account

Features such as full-body, hands, OSC and Syphon can follow.

---

# 47. Source / technology validation notes

The architecture was selected after checking current project/platform documentation in September 2026.

Key findings used for this decision:

- UniVRM supports runtime import of both VRM 0.x and VRM 1.0 and targets Mac standalone.
- Homuler MediaPipeUnityPlugin lists Apple Silicon macOS support and MediaPipe Tasks including Face Landmarker, Pose Landmarker and Hand Landmarker; it notes that GPU mode is not supported on macOS/Windows and the desktop baseline is CPU inference.
- Google's current Face Landmarker task produces 3D landmarks, facial transformation matrices and 52 blendshape coefficients.
- Apple's Core Media I/O Camera Extension is the modern supported macOS mechanism for publishing a software/virtual camera and is designed to be packaged with a host application.
- A native Swift/Metal implementation remains possible, but current macOS MediaPipe distribution and overall integration risk make Unity the preferred v1 route.

Primary references to keep bookmarked during implementation:

- UniVRM repository / VRM Consortium documentation
- Homuler MediaPipeUnityPlugin repository
- Google AI Edge MediaPipe Face Landmarker documentation
- Apple Developer Core Media I/O documentation
- Apple "Creating a camera extension with Core Media I/O"

---

# 48. Decision log

### D-001
Use Unity for v1 runtime.

### D-002
Use UniVRM for avatar loading/runtime.

### D-003
Support VRM 0.x and 1.0 with automatic detection.

### D-004
Use MediaPipeUnityPlugin for the first advanced camera-tracking implementation.

### D-005
Default render/output is **1920×1080 @ 30fps**.

### D-006
Keep tracking resolution independent from output resolution.

### D-007
Default avatar framing is Bust.

### D-008
Default lip mode is Hybrid after microphone/camera features exist.

### D-009
Default background is chroma green for compatibility.

### D-010
Maintain internal alpha rendering but do not depend on alpha for initial OBS workflow.

### D-011
Use Apple's Core Media I/O Camera Extension for the eventual real virtual camera.

### D-012
Prove the Camera Extension independently before bridging Unity frames into it.

### D-013
Do not implement optional protocols/features before the live-ready foundation passes QA.

---

# 49. Immediate next action

Create the Git repository and Unity project, place this specification under:

```text
Docs/PRD.md
```

Then give Claude Code the **PHASE 1** prompt in section 41.

The first review checkpoint is not "the whole VTuber app works."

The first checkpoint is:

> A macOS Apple-Silicon standalone application reliably loads VRM 0.x and VRM 1.0 at runtime and produces a clean 1920×1080/30 composited RenderTexture with framing/background controls and no UI contamination.

Only after that checkpoint passes should face tracking be added.
