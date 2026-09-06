# Native macOS components (reserved)

Nothing here is built yet. The PRD schedules native code only after the render and tracking
foundations are stable (PRD 20.4, 43):

- `FrameBridge/` Objective-C++ Unity plugin that hands the OutputRenderTexture to the extension
  through an IOSurface or shared pixel buffer.
- `CameraExtension/` Swift Core Media I/O Camera Extension publishing the "VRM Live Camera" device.
- `InstallerHelper/` activation / approval flow for the extension.
- Later, optionally, a small NSOpenPanel bridge so the app can use the native file dialog.

The isolated Camera Extension spike (test pattern, no Unity) comes first; see Docs/VirtualCamera.md.
