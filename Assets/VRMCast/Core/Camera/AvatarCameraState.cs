using System;

namespace VRMCast.Core.Camera
{
    /// <summary>
    /// Serializable-friendly avatar camera state (stored per profile, PRD 16.3). Zoom, pan and orbit are
    /// relative to the preset so switching preset keeps the user's adjustments meaningful.
    /// </summary>
    public sealed class AvatarCameraState
    {
        public const float DefaultFovDeg = 30f;
        public const float MinFovDeg = 10f;
        public const float MaxFovDeg = 90f;
        public const float MinZoom = 0.25f;
        public const float MaxZoom = 4f;
        public const float MaxPan = 2f;
        public const float MaxPitchDeg = 80f;

        public FramingPreset Preset { get; set; } = FramingPresetExtensions.Default;

        /// <summary>Magnification relative to the preset distance. 1 = preset, 2 = twice as close.</summary>
        public float Zoom { get; set; } = 1f;

        /// <summary>Horizontal view-plane offset in units of avatar height. Positive moves the avatar right.</summary>
        public float PanX { get; set; }

        /// <summary>Vertical view-plane offset in units of avatar height. Positive moves the avatar up.</summary>
        public float PanY { get; set; }

        /// <summary>Orbit around the avatar's vertical axis, degrees. 0 = camera in front of the avatar.</summary>
        public float OrbitYawDeg { get; set; }

        /// <summary>Orbit elevation, degrees. Positive looks down from above.</summary>
        public float OrbitPitchDeg { get; set; }

        public float FovDeg { get; set; } = DefaultFovDeg;

        public AvatarCameraState Clone() => new AvatarCameraState
        {
            Preset = Preset,
            Zoom = Zoom,
            PanX = PanX,
            PanY = PanY,
            OrbitYawDeg = OrbitYawDeg,
            OrbitPitchDeg = OrbitPitchDeg,
            FovDeg = FovDeg,
        };

        /// <summary>Reset Camera (PRD 16.2): back to the preset's default view, keeping the preset.</summary>
        public void ResetView()
        {
            Zoom = 1f;
            PanX = 0f;
            PanY = 0f;
            OrbitYawDeg = 0f;
            OrbitPitchDeg = 0f;
            FovDeg = DefaultFovDeg;
        }

        /// <summary>Reset Avatar Orientation (PRD 16.2): clears orbit only.</summary>
        public void ResetOrientation()
        {
            OrbitYawDeg = 0f;
            OrbitPitchDeg = 0f;
        }

        public void ApplyPreset(FramingPreset preset)
        {
            Preset = preset;
            ResetView();
        }

        public void Clamp()
        {
            Zoom = Math.Min(Math.Max(Zoom, MinZoom), MaxZoom);
            PanX = Math.Min(Math.Max(PanX, -MaxPan), MaxPan);
            PanY = Math.Min(Math.Max(PanY, -MaxPan), MaxPan);
            OrbitPitchDeg = Math.Min(Math.Max(OrbitPitchDeg, -MaxPitchDeg), MaxPitchDeg);
            OrbitYawDeg = WrapDegrees(OrbitYawDeg);
            FovDeg = Math.Min(Math.Max(FovDeg, MinFovDeg), MaxFovDeg);
        }

        public static float WrapDegrees(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            if (deg <= -180f) deg += 360f;
            return deg;
        }
    }
}
