using System;
using UnityEngine;
using VRMCast.Avatar;
using VRMCast.Core.Camera;
using VRMCast.Rendering;

namespace VRMCast.CameraControl
{
    /// <summary>
    /// Applies <see cref="AvatarCameraState"/> to the avatar camera using the pure <see cref="AvatarCameraSolver"/>.
    /// Input gestures are interpreted by the UI layer (so they never fight UI dragging, PRD 16.2) and arrive here as
    /// zoom / pan / orbit deltas.
    /// </summary>
    public sealed class AvatarCameraController : IDisposable
    {
        public const float ZoomStepPerScrollUnit = 0.05f;
        public const float OrbitDegreesPerPixel = 0.4f;
        public const float PanHeightsPerPixel = 0.0015f;

        private readonly IRenderService _render;
        private readonly IAvatarService _avatars;
        private AvatarMetrics _metrics = AvatarMetrics.Placeholder;
        private bool _dirty = true;

        public AvatarCameraState State { get; }

        public event Action StateChanged;

        public AvatarCameraController(IRenderService render, IAvatarService avatars, AvatarCameraState state = null)
        {
            _render = render ?? throw new ArgumentNullException(nameof(render));
            _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            State = state ?? new AvatarCameraState();

            _avatars.AvatarLoaded += OnAvatarLoaded;
            _avatars.AvatarUnloaded += OnAvatarUnloaded;
            _render.SettingsChanged += _ => _dirty = true;
        }

        public void SetPreset(FramingPreset preset)
        {
            State.ApplyPreset(preset);
            MarkChanged();
        }

        /// <summary>Scroll zoom. Positive delta zooms in.</summary>
        public void ZoomBy(float scrollDelta)
        {
            State.Zoom *= Mathf.Pow(1f + ZoomStepPerScrollUnit, scrollDelta);
            MarkChanged();
        }

        public void SetZoom(float zoom)
        {
            State.Zoom = zoom;
            MarkChanged();
        }

        /// <summary>Pan by a pointer delta in pixels (screen +x right, +y down).</summary>
        public void PanByPixels(float dx, float dy)
        {
            State.PanX += dx * PanHeightsPerPixel;
            State.PanY -= dy * PanHeightsPerPixel;
            MarkChanged();
        }

        /// <summary>Orbit by a pointer delta in pixels (screen +x right, +y down).</summary>
        public void OrbitByPixels(float dx, float dy)
        {
            State.OrbitYawDeg += dx * OrbitDegreesPerPixel;
            State.OrbitPitchDeg += dy * OrbitDegreesPerPixel;
            MarkChanged();
        }

        public void SetFov(float fovDeg)
        {
            State.FovDeg = fovDeg;
            MarkChanged();
        }

        public void ResetCamera()
        {
            State.ResetView();
            MarkChanged();
        }

        public void ResetOrientation()
        {
            State.ResetOrientation();
            MarkChanged();
        }

        /// <summary>Recomputes avatar landmarks (after a pose or scale change) and reapplies.</summary>
        public void Reframe()
        {
            _metrics = _avatars.HasAvatar ? _avatars.Current.ComputeMetrics() : AvatarMetrics.Placeholder;
            _dirty = true;
        }

        /// <summary>Applies the pose when something changed. Cheap enough to call every LateUpdate.</summary>
        public void Apply(bool force = false)
        {
            if (!_dirty && !force) return;
            _dirty = false;

            State.Clamp();
            var pose = AvatarCameraSolver.Solve(State, _metrics, _render.Settings.Aspect);
            var cam = _render.AvatarCamera;
            var t = cam.transform;
            t.position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            t.LookAt(new Vector3(pose.LookAt.X, pose.LookAt.Y, pose.LookAt.Z), Vector3.up);
            cam.fieldOfView = pose.FovDeg;
        }

        private void MarkChanged()
        {
            State.Clamp();
            _dirty = true;
            StateChanged?.Invoke();
        }

        private void OnAvatarLoaded(LoadedAvatar avatar) => Reframe();

        private void OnAvatarUnloaded() => Reframe();

        public void Dispose()
        {
            _avatars.AvatarLoaded -= OnAvatarLoaded;
            _avatars.AvatarUnloaded -= OnAvatarUnloaded;
        }
    }
}
