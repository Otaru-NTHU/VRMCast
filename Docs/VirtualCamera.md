# Virtual Camera (MVP-E)

The virtual camera is a **Core Media I/O Camera Extension** (PRD 20.1) published as the device
`VRM Live Camera`. Work is split as PRD 43 demands: an isolated spike proves the extension with generated
test frames and no Unity; the Unity frame bridge is coupled only after the spike passes its definition of
done on real hardware.

## Status

| Step | State |
| --- | --- |
| Camera Extension spike (`Native/macOS/CameraExtensionSpike`) | written, awaiting on-device validation |
| Unity frame bridge (`MacVirtualCameraOutput` + Objective-C++ plugin) | not started, blocked on the spike |
| Activation UI inside VRMCast (PRD 20.3) | not started |

## Pipeline

```
RenderService.FrameRendered
   -> OutputService
   -> MacVirtualCameraOutput : IFrameOutput          (Assets/VRMCast/Runtime/Output, C#)      [later]
   -> native frame bridge                            (Native/macOS/FrameBridge, Objective-C++)  [later]
   -> CVPixelBuffer (IOSurface-backed) in a CMSampleBuffer
   -> CMSimpleQueue of the extension's SINK stream   (CMIOStreamCopyBufferQueue)
   -> Camera Extension process                       (Native/macOS/CameraExtensionSpike/Extension, Swift)
   -> SOURCE stream at a fixed 30 fps
   -> OBS / FaceTime / any AVFoundation client
```

The device has two streams. The **source** stream is what capture apps read. The **sink** stream is the
Apple-sanctioned way for a host app to feed a camera extension; the host enqueues sample buffers into it
and the extension receives them through `consumeSampleBuffer`. This is the same mechanism OBS's own
macOS virtual camera uses, so its behaviour with OBS is well trodden.

## Spike layout

```
Native/macOS/CameraExtensionSpike/
  VRMCastCameraSpike.xcodeproj/   hand-maintained project, 2 targets, shared scheme
  Config/Signing.xcconfig         DEVELOPMENT_TEAM and bundle prefix (edit before building)
  Host/                           SwiftUI app: install/uninstall/status + sends host frames to the sink
  Extension/                      the system extension: provider, device, source + sink streams
  Shared/                         constants and the test-pattern painter compiled into both targets
Scripts/build-camera-spike.sh     xcodebuild wrapper (+ --install to copy into /Applications)
Scripts/check-xcodeproj.py        structural check of the project file, runs without Xcode
```

## Design decisions (PRD 43 items 1–12)

1. **Target structure.** One macOS app target (`VRMCastCameraSpike`, SwiftUI) and one
   `com.apple.product-type.system-extension` target embedded through a Copy Files phase into
   `Contents/Library/SystemExtensions/`. The extension's bundle ID is the host's plus `.Camera`; macOS
   requires that prefix relationship. The same two-target shape is reused later with the Unity-built
   `VRMCast.app` as the host (post-build step adds the embed).
2. **Entitlements.** Host: `com.apple.security.app-sandbox`,
   `com.apple.developer.system-extension.install`, `com.apple.security.device.camera` (a sandboxed process
   needs it to enumerate CMIO devices and start the sink stream), and the App Group. Extension:
   `com.apple.security.app-sandbox` and the same App Group. Hardened runtime on both.
3. **App Group / IPC.** App Group `<TeamID>.<prefix>`; the extension's `CMIOExtensionMachServiceName` is
   `<TeamID>.<prefix>.camera`, which must start with an App Group the extension holds. No custom XPC: the
   sink stream is the only host→extension channel and CMIO owns the transport.
4. **Activation flow.** `OSSystemExtensionRequest.activationRequest` from the host; the delegate answers
   `.replace` when an older version is installed; `requestNeedsUserApproval` turns into a status line
   telling the user where to approve; `didFinishWithResult` distinguishes `.completed` from
   `.willCompleteAfterReboot`; every failure is shown with the error code and a hint (PRD 20.3).
   `propertiesRequest` gives the status line on launch (installed / enabled / awaiting approval).
5. **Approval behaviour.** On first activation macOS shows a system dialog and the user must allow the
   extension in System Settings (macOS 15+: General › Login Items & Extensions › Camera Extensions;
   macOS 13–14: Privacy & Security). No admin password is needed on a standard-user account for camera
   extensions; MDM-managed Macs may block it (`forbiddenBySystemPolicy`). Reinstalling a newer build
   does not re-prompt.
6. **Pixel format.** `kCVPixelFormatType_32BGRA`, 1920×1080, buffers created from a pool with
   `kCVPixelBufferIOSurfacePropertiesKey` so they are IOSurface-backed. BGRA matches the Unity
   `RenderTexture` readback (ARGB32 on Metal is BGRA in memory), so the bridge needs no swizzle.
7. **Frame timing.** The extension owns the clock: a `DispatchSourceTimer` (`.strict`, 1 ms leeway) on a
   user-interactive queue sends exactly 30 frames per second on the source stream with host-time
   presentation stamps. Host frames do not drive the client cadence; they only replace the pixel buffer
   that the next tick sends. Worst-case added latency is one frame interval (33 ms).
8. **IOSurface.** Yes, indirectly: sample buffers enqueued into a sink stream carry IOSurface-backed
   pixel buffers across the process boundary without copying pixels. There is no supported API for a
   host to hand the extension a raw IOSurface ID, so the sink stream *is* the IOSurface strategy.
9. **Fallback strategy.** If a Mac ever returns non-IOSurface buffers, the same code path still works
   with a memcpy inside CMIO. No shared-memory or file-based transport is needed.
10. **Host stops sending.** The extension keeps the last host frame for 500 ms
    (`hostFrameTimeout`), then switches to its own generated pattern (red lower band). Clients never see
    a frozen or corrupt frame, and the source stream never stops (PRD 37.6).
11. **OBS opens/closes the camera.** `startStream`/`stopStream` on the source stream count clients; the
    timer runs while at least one client is attached and is cancelled at zero. The extension process
    itself stays resident while enabled, so re-opening the device in OBS is instant and the host's sink
    connection survives OBS restarts.
12. **Packaging/signing.** A real Developer team is mandatory (`DEVELOPMENT_TEAM` in
    `Config/Signing.xcconfig`); "Sign to Run Locally" cannot carry the system-extension entitlement.
    The app must be launched from `/Applications` unless `systemextensionsctl developer on` is active.
    Distribution needs Developer ID + notarization; the extension is notarized as part of the app.

## Building and testing the spike

Requirements: Xcode 15 or newer, macOS 13 or newer, an Apple Developer team, OBS 28 or newer.

1. Put your Team ID in `Native/macOS/CameraExtensionSpike/Config/Signing.xcconfig`
   (`DEVELOPMENT_TEAM = ABCDE12345`) or pass it on the command line as shown below.
2. Build and install:

   ```
   Scripts/build-camera-spike.sh --install DEVELOPMENT_TEAM=ABCDE12345
   open /Applications/VRMCastCameraSpike.app
   ```

   Or open the `.xcodeproj` in Xcode, select the `VRMCastCameraSpike` scheme, Product › Archive is not
   needed; Product › Build, then copy the app from the Products folder into `/Applications`. When
   iterating from Xcode directly, run `systemextensionsctl developer on` once (SIP stays on) so the
   extension loads from DerivedData.
3. In the app press **Install / Enable**, approve the extension in System Settings when asked, then
   **Check Status** until it says enabled.
4. Open OBS → Sources → Video Capture Device → device `VRM Live Camera`. Expected: colour bars, a
   **red** lower band with a moving white square and a growing black progress line. OBS shows
   1920×1080 in the source properties.
5. Press **Start Sending 1080p30**. Expected within one second: the lower band turns **blue**; the
   counter in the app climbs by ~30 per second with zero enqueue failures.
6. Press **Stop Sending**. Expected: the band returns to red within about half a second; OBS never
   shows a frozen or black frame.
7. Restart OBS while sending. Expected: the device is still listed, picking it shows blue frames, no
   reinstall needed.
8. Leave it sending for 10 minutes with OBS open. Expected: OBS Stats shows a steady ~30 fps for the
   source, the app reports no enqueue failures, Activity Monitor shows flat memory for both the app and
   the `VRMCastCameraExtension` process.
9. Quit the app while OBS is open. Expected: red pattern within half a second, OBS keeps running.
10. Press **Uninstall**, approve if asked. Expected: the device disappears from OBS's list.

Record the results in `Docs/QA.md` (MVP-E section). Only when steps 4–9 pass does the Unity bridge start.

## Troubleshooting

- `extensionNotFound`: the `.systemextension` is not inside the app bundle. Check the Embed System
  Extensions phase and that both bundle IDs share the prefix.
- `unsupportedParentBundleLocation`: launch from `/Applications` or enable developer mode.
- `validationFailed` / `codeSignatureInvalid`: signing. Both targets must be signed by the same team and the
  Mach service name must start with an App Group listed in the extension's entitlements.
- Device missing from OBS after approval: `systemextensionsctl list` should show the extension as
  `[activated enabled]`. If it says `waiting for user`, approval is still pending. Restart OBS after
  first activation.
- Old versions stuck: `systemextensionsctl reset` requires SIP disabled; instead bump
  `CURRENT_PROJECT_VERSION` and reinstall, the host answers `.replace`.
- Logs: `log stream --predicate 'subsystem == "VRMCast.CameraExtension"' --level info`.

## Contract with the Unity side (unchanged from MVP-A)

- Frames are ARGB32 `RenderTexture`s; size and fps come from `OutputConfiguration.Settings`.
- `OutputConfiguration.WantsAlpha` says whether the Transparent background is active; the virtual camera
  reports `SupportsAlpha = false` because no capture app consumes alpha from a camera (D-010).
- `OutputService` restarts outputs when settings change; the bridge renegotiates the sink format then.
- Stopping output leaves the extension's fallback frame in the device, not the last avatar frame.
