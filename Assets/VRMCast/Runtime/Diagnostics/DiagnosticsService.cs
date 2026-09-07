using UnityEngine;
using VRMCast.Avatar;
using VRMCast.Backgrounds;
using VRMCast.CameraControl;
using VRMCast.Core.Diagnostics;
using VRMCast.Output;
using VRMCast.Rendering;
using VRMCast.Tracking;

namespace VRMCast.Diagnostics
{
    /// <summary>Collects render FPS and current settings into a <see cref="DiagnosticsSnapshot"/> (PRD 31, MVP-A subset).</summary>
    public sealed class DiagnosticsService
    {
        private readonly FpsCounter _fps = new FpsCounter(0.5);
        private readonly IRenderService _render;
        private readonly IAvatarService _avatars;
        private readonly BackgroundService _background;
        private readonly AvatarCameraController _camera;
        private readonly OutputService _outputs;
        private TrackingCoordinator _tracking;

        public DiagnosticsService(IRenderService render, IAvatarService avatars, BackgroundService background, AvatarCameraController camera, OutputService outputs)
        {
            _render = render;
            _avatars = avatars;
            _background = background;
            _camera = camera;
            _outputs = outputs;
        }

        /// <summary>Tracking is constructed after diagnostics; attach it once it exists.</summary>
        public void AttachTracking(TrackingCoordinator tracking) => _tracking = tracking;

        public double RenderFps => _fps.Fps;

        /// <summary>Call once per frame from Update with the unscaled delta time. Returns true when a new average is ready.</summary>
        public bool Tick(float unscaledDeltaTime) => _fps.AddFrame(unscaledDeltaTime);

        public DiagnosticsSnapshot Snapshot()
        {
            var avatar = _avatars.HasAvatar ? _avatars.Current.Info : null;
            return new DiagnosticsSnapshot
            {
                RenderFps = _fps.Fps,
                FrameTimeMs = _fps.FrameTimeMs,
                Output = _render.Settings,
                TargetFrameRate = Application.targetFrameRate,
                AvatarName = avatar != null ? avatar.FileName : "(none)",
                AvatarVersion = avatar != null ? avatar.VersionLabel : "-",
                ExpressionCount = avatar != null ? avatar.ExpressionCount : 0,
                SpringBones = avatar != null && avatar.HasSpringBones,
                Background = _background.Settings.Mode,
                Framing = _camera.State.Preset,
                OutputStatus = OutputStatusLabel(),
                AppVersion = Application.version,
                Platform = $"{Application.platform} / {SystemInfo.graphicsDeviceType} / {SystemInfo.processorType}",
                TrackingStatus = _tracking != null ? _tracking.CurrentStatus.ToString() : "off",
                TrackingEngine = FaceTrackingProviderRegistry.HasProviders ? FaceTrackingProviderRegistry.Names[0] : "(none)",
                TrackingFps = _tracking != null ? _tracking.Stats.ResultFps : 0,
                InferenceMs = _tracking != null ? _tracking.Stats.InferenceMs : 0,
                TrackingDropped = _tracking != null ? _tracking.Stats.Dropped : 0,
                FaceConfidence = _tracking != null ? _tracking.Solver.Pose.Confidence : 0f,
                CameraDevice = _tracking != null && !string.IsNullOrEmpty(_tracking.Camera.SelectedDevice) ? _tracking.Camera.SelectedDevice : "(none)",
                CameraResolution = _tracking != null && _tracking.Camera.Texture != null ? $"{_tracking.Camera.Texture.width}x{_tracking.Camera.Texture.height}" : "-",
            };
        }

        public string OutputStatusLabel()
        {
            foreach (var o in _outputs.Outputs)
            {
                if (o.IsRunning && !(o is PreviewOutput)) return $"{o.Name} running";
            }
            return "Preview only";
        }

        public void CopyReportToClipboard()
        {
            GUIUtility.systemCopyBuffer = Snapshot().ToReport();
        }
    }
}
