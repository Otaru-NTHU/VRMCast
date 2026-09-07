# VRMCast

Open-source real-time VRM avatar tracking, motion capture, and virtual camera for macOS.

VRMCast loads VRM 0.x and VRM 1.0 avatars, will drive them from a webcam / iPhone / microphone, and
composites the result for OBS at 1920×1080 / 30 fps. Apple Silicon only.

**Current milestone: MVP-D.** The app loads avatars at runtime, frames them with camera presets, composites
solid / image / chroma / transparent backgrounds into a dedicated 1920×1080 render texture, drives head, eyes,
blinks and mouth from a webcam through MediaPipe Face Landmarker with calibration and smoothing, adds
microphone / hybrid lip sync and upper-body motion from Pose Landmarker, and keeps everything in per-avatar
profiles with expression hotkeys and a performance mode. The macOS virtual camera (MVP-E) follows the sequence
in [Docs/PRD.md](Docs/PRD.md).

## Stack

- Unity 6.3 LTS (`6000.3.20f1`), Built-in Render Pipeline, C#
- [UniVRM](https://github.com/vrm-c/UniVRM) v0.131.2 for VRM 0.x and 1.0 runtime loading
- Unity UI Toolkit for the desktop UI, in 繁體中文 (default) and English
- [MediaPipeUnityPlugin](https://github.com/homuler/MediaPipeUnityPlugin) v0.16.3 for face tracking (CPU)
- Later: Core Media I/O Camera Extension (virtual camera)

See [Docs/Architecture.md](Docs/Architecture.md) for the service layout and decisions.

## Getting started

1. Install Unity `6000.3.20f1` with the **Mac Build Support** module through Unity Hub.
2. Run `Scripts/setup-mediapipe.sh` once; it downloads the 290 MB MediaPipe plugin tarball into `Packages/`.
3. Open this folder as a project. Unity Package Manager fetches UniVRM from GitHub on first open
   (`Packages/manifest.json`).
4. Open `Assets/VRMCast/Scenes/Main.unity` and press Play, or build with
   `Scripts/build-macos.sh` (produces `Builds/macOS/VRMCast.app`).
5. Load VRM → pick a `.vrm` file. You can also pass a path: `open -a VRMCast.app --args /path/model.vrm`.
6. Start Tracking, allow camera and microphone access, then Calibrate while facing the camera.

If the scene opens with missing references (for example after a Unity upgrade), run
**VRMCast > Setup > Rebuild Main Scene**; it regenerates the scene and UI assets.

## Tests

```
Scripts/run-core-tests.sh        # engine-free tests with the .NET 8 SDK, no Unity needed
Scripts/run-unity-tests.sh       # same tests inside the Unity Test Runner (EditMode)
Scripts/verify-dependencies.sh   # checks pinned dependency versions
Scripts/check-xcodeproj.py Native/macOS/CameraExtensionSpike/VRMCastCameraSpike.xcodeproj  # project file sanity
Scripts/build-camera-spike.sh --install DEVELOPMENT_TEAM=<TeamID>   # builds the camera extension spike (Xcode)
Scripts/build-frame-bridge.sh                                        # native plugin for the virtual camera (clang)
Scripts/package-macos.sh --install DEVELOPMENT_TEAM=<TeamID>         # embeds + signs the camera extension in VRMCast.app
```

Manual QA steps for each milestone are in [Docs/QA.md](Docs/QA.md).

## Repository layout

```
Assets/VRMCast/
  Core/       pure C# (VRM inspection, framing math, backgrounds, diagnostics, tracking contracts)
  Runtime/    Unity services, tracking pipeline, UI Toolkit controller, bootstrap
  Tracking.MediaPipe/ MediaPipe face provider (optional assembly)
  Editor/     scene builder, build script
  Tests/      NUnit EditMode tests
  UI/         Main.uxml, Main.uss, PanelSettings, theme, background material
  Scenes/     Main.unity
Docs/         PRD, Architecture, QA, Tracking, VirtualCamera
Native/macOS/ Camera Extension (Xcode project) and the Unity frame bridge plugin source (MVP-E)
Scripts/      build, test and dependency scripts
```

## Privacy

Everything runs on device. No camera frames, audio or profiles leave the machine, and diagnostics
reports contain no file contents (PRD 32).

## License

MIT, see [LICENSE](LICENSE). Third-party components are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
