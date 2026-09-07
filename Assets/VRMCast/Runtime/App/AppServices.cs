using System;
using VRMCast.Avatar;
using VRMCast.Backgrounds;
using VRMCast.CameraControl;
using VRMCast.Core.Localization;
using VRMCast.Diagnostics;
using VRMCast.Output;
using VRMCast.Rendering;
using VRMCast.Tracking;

namespace VRMCast.App
{
    /// <summary>
    /// Explicit service graph for the application (PRD 27). Built once by <see cref="AppBootstrap"/> and handed to
    /// the UI; there are no global singletons and no scene lookups.
    /// </summary>
    public sealed class AppServices : IDisposable
    {
        public Localizer Localizer { get; }
        public IRenderService Render { get; }
        public IAvatarService Avatars { get; }
        public BackgroundService Background { get; }
        public AvatarCameraController Camera { get; }
        public OutputService Outputs { get; }
        public PreviewOutput Preview { get; }
        public NullOutput DebugOutput { get; }
        public DiagnosticsService Diagnostics { get; }
        public CameraCaptureService Camera2D { get; }
        public TrackingCoordinator Tracking { get; }

        public AppServices(Localizer localizer, IRenderService render, IAvatarService avatars, BackgroundService background,
            AvatarCameraController camera, OutputService outputs, PreviewOutput preview, NullOutput debugOutput,
            DiagnosticsService diagnostics, CameraCaptureService camera2D, TrackingCoordinator tracking)
        {
            Camera2D = camera2D;
            Tracking = tracking;
            Localizer = localizer;
            Render = render;
            Avatars = avatars;
            Background = background;
            Camera = camera;
            Outputs = outputs;
            Preview = preview;
            DebugOutput = debugOutput;
            Diagnostics = diagnostics;
        }

        public void Dispose()
        {
            // Reverse construction order.
            Tracking.Dispose();
            Camera2D.Dispose();
            Outputs.Dispose();
            Camera.Dispose();
            Background.Dispose();
            Avatars.Dispose();
            Render.Dispose();
        }
    }
}
