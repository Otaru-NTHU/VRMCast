# Native macOS components

| Directory | Purpose | State |
| --- | --- | --- |
| `CameraExtensionSpike/` | Xcode project: host app + Core Media I/O Camera Extension publishing `VRM Live Camera` with a generated 1080p30 test pattern and a sink stream for host frames (PRD 43). | written, needs on-device validation |
| `FrameBridge/` | Objective-C++ Unity plugin that hands the OutputRenderTexture to the extension through the sink stream. | not started, blocked on the spike |
| `InstallerHelper/` | activation / approval flow for the extension inside VRMCast.app. | not started |

Build the spike with `Scripts/build-camera-spike.sh --install DEVELOPMENT_TEAM=<TeamID>`; the test
procedure and the design notes are in `Docs/VirtualCamera.md`. The project file is maintained by hand
and checked by `Scripts/check-xcodeproj.py` (no Xcode required), so it can be reviewed like any other
source file.

No third-party code: only Apple frameworks (SwiftUI, SystemExtensions, CoreMediaIO, CoreVideo).
