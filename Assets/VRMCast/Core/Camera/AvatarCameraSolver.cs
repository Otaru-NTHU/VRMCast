using System;

namespace VRMCast.Core.Camera
{
    /// <summary>Resolved camera placement produced by <see cref="AvatarCameraSolver"/>.</summary>
    public readonly struct CameraPose
    {
        public Float3 Position { get; }
        public Float3 LookAt { get; }
        public float FovDeg { get; }
        public float Distance => Float3.Distance(Position, LookAt);

        public CameraPose(Float3 position, Float3 lookAt, float fovDeg)
        {
            Position = position;
            LookAt = lookAt;
            FovDeg = fovDeg;
        }
    }

    /// <summary>Vertical span a preset must keep in frame, plus the margin added around it.</summary>
    public readonly struct FramingSpec
    {
        public float LowY { get; }
        public float HighY { get; }
        public float Margin { get; }

        /// <summary>Fraction of the avatar's full width that must stay in frame (a face shot does not need the shoulders).</summary>
        public float WidthFraction { get; }

        public FramingSpec(float lowY, float highY, float margin, float widthFraction = 1f)
        {
            LowY = lowY;
            HighY = highY;
            Margin = margin;
            WidthFraction = widthFraction;
        }
    }

    /// <summary>
    /// Pure framing math: preset + metrics + user adjustments → camera pose. The output aspect ratio is an
    /// input so that changing resolution preserves framing (PRD 16.3). Independent of UnityEngine so it can be
    /// tested directly.
    /// </summary>
    public static class AvatarCameraSolver
    {
        public const float MinDistance = 0.05f;

        /// <summary>
        /// Direction sign along Z on which the camera sits relative to the avatar when orbit is zero.
        /// VRM avatars face +Z in Unity, so the camera sits on +Z and looks toward -Z.
        /// </summary>
        public const float FrontSign = 1f;

        public static FramingSpec GetSpec(FramingPreset preset, AvatarMetrics m)
        {
            switch (preset)
            {
                case FramingPreset.Face:
                    return new FramingSpec(m.NeckY, m.TopY, 0.35f, widthFraction: 0.4f);
                case FramingPreset.Bust:
                    return new FramingSpec(m.ChestY - (m.ChestY - m.HipsY) * 0.25f, m.TopY, 0.18f);
                case FramingPreset.HalfBody:
                    return new FramingSpec(m.HipsY - (m.HipsY - m.FootY) * 0.1f, m.TopY, 0.12f);
                case FramingPreset.FullBody:
                    return new FramingSpec(m.FootY, m.TopY, 0.08f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }

        public static CameraPose Solve(AvatarCameraState state, AvatarMetrics metrics, float aspect)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (metrics == null) throw new ArgumentNullException(nameof(metrics));
            if (aspect <= 0f || float.IsNaN(aspect) || float.IsInfinity(aspect)) aspect = 16f / 9f;

            var s = state.Clone();
            s.Clamp();

            var spec = GetSpec(s.Preset, metrics);
            var span = Math.Max(spec.HighY - spec.LowY, 0.01f);
            var halfSpan = span * 0.5f * (1f + spec.Margin);
            var tanHalfFov = (float)Math.Tan(s.FovDeg * 0.5 * Math.PI / 180.0);

            // Fit vertically, then make sure the avatar's width also fits for narrow aspect ratios.
            var distance = halfSpan / tanHalfFov;
            var halfWidth = metrics.Width * spec.WidthFraction * 0.5f * (1f + spec.Margin);
            var widthDistance = halfWidth / (tanHalfFov * aspect);
            distance = Math.Max(distance, widthDistance);
            distance = Math.Max(distance / s.Zoom, MinDistance);

            var target = new Float3(metrics.CenterX, (spec.LowY + spec.HighY) * 0.5f, metrics.CenterZ);

            var yaw = s.OrbitYawDeg * Math.PI / 180.0;
            var pitch = s.OrbitPitchDeg * Math.PI / 180.0;
            var offsetDir = new Float3(
                (float)(Math.Sin(yaw) * Math.Cos(pitch)) * FrontSign,
                (float)Math.Sin(pitch),
                (float)(Math.Cos(yaw) * Math.Cos(pitch)) * FrontSign);
            var position = target + offsetDir * distance;

            // Pan in the camera's view plane, scaled by avatar height so it feels the same on any model.
            var forward = (target - position).Normalized;
            var right = Float3.Cross(Float3.Up, forward).Normalized;
            var up = Float3.Cross(forward, right).Normalized;
            // Positive PanX should move the avatar to the right on screen, so the camera moves left.
            var pan = right * (-s.PanX * metrics.Height) + up * (-s.PanY * metrics.Height);

            return new CameraPose(position + pan, target + pan, s.FovDeg);
        }
    }
}
