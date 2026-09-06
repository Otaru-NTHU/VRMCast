# Virtual Camera

Not implemented in MVP-A (PRD 20.4). The foundation only guarantees the input the virtual camera will
consume: a fixed-size `OutputRenderTexture` delivered through `IFrameOutput.SubmitFrame` on every render.

## Planned shape

```
RenderService.FrameRendered
   -> OutputService
   -> MacVirtualCameraOutput : IFrameOutput   (Assets/VRMCast/Runtime/Output, C#)
   -> native frame bridge                     (Native/macOS/FrameBridge, Objective-C++ plugin)
   -> IOSurface / shared pixel buffer
   -> Camera Extension                        (Native/macOS/CameraExtension, Swift, CMIOExtension)
   -> OBS / FaceTime / any AVFoundation client
```

## Spike first (PRD 43)

The Camera Extension is proven in isolation with a generated 1920×1080 30 fps test pattern before any
Unity coupling. The spike must answer: Xcode target layout, entitlements, App Group / IPC design,
activation and approval flow, pixel format, frame timing, IOSurface viability, fallback strategy,
behaviour when the host stops sending frames, behaviour when OBS opens/closes the device, and
signing/packaging constraints. Findings go in this file.

## Constraints fixed by MVP-A

- Frames are ARGB32, output size and fps come from `OutputConfiguration.Settings`.
- `OutputConfiguration.WantsAlpha` tells the output whether the Transparent background is active;
  the output must report `SupportsAlpha` honestly (D-010) and the UI will only offer transparent
  output when it does.
- Outputs are restarted by `OutputService` when the output settings change.
- Stopping output must leave a defined fallback frame in the device (PRD 37.6), which is the
  extension's responsibility, not the renderer's.
