# Native macOS components

| Directory | Purpose | State |
| --- | --- | --- |
| `CameraExtensionSpike/` | Xcode project: host app + Core Media I/O Camera Extension publishing `VRM Live Camera` with a generated 1080p30 test pattern and a sink stream for host frames (PRD 43). | written, needs on-device validation |
| `FrameBridge/` | Objective-C++ Unity plugin: pushes BGRA frames into the extension's sink stream and drives install / status through `OSSystemExtensionRequest`. Built by `Scripts/build-frame-bridge.sh`. | written, needs on-device validation |

Build the spike with `Scripts/build-camera-spike.sh --install DEVELOPMENT_TEAM=<TeamID>`; package the real app
with `Scripts/package-macos.sh --install DEVELOPMENT_TEAM=<TeamID>`. The test procedure and the design notes are
in `Docs/VirtualCamera.md`. The project file is maintained by hand
and checked by `Scripts/check-xcodeproj.py` (no Xcode required), so it can be reviewed like any other
source file.

No third-party code: only Apple frameworks (SwiftUI, SystemExtensions, CoreMediaIO, CoreVideo).
