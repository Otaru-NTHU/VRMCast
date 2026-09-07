using System;

namespace VRMCast.Core.Tracking
{
    /// <summary>Face tracking modes (PRD 9). Basic drives the stable subset; Advanced uses every useful blendshape.</summary>
    public enum FaceTrackingMode
    {
        Basic = 0,
        Advanced = 1,
    }

    /// <summary>
    /// Tunables of the motion solver (PRD 11). Stored per profile later; defaults follow PRD 34 ("smoothing: medium").
    /// </summary>
    public sealed class FaceTrackingSettings
    {
        public const float MaxHeadAngleDeg = 80f;

        public FaceTrackingMode Mode { get; set; } = FaceTrackingMode.Basic;

        /// <summary>Mirror the user like a webcam preview: the user's left becomes the avatar's right.</summary>
        public bool MirrorUser { get; set; } = true;

        /// <summary>0 = raw, 1 = heavy. Maps to a time constant, so it is frame-rate independent.</summary>
        public float HeadSmoothing { get; set; } = 0.5f;
        public float ExpressionSmoothing { get; set; } = 0.35f;
        public float LookSmoothing { get; set; } = 0.4f;

        public float HeadGain { get; set; } = 1.0f;
        public float HeadDeadZoneDeg { get; set; } = 0.8f;

        /// <summary>Share of the tracked head rotation applied to each bone (PRD 11, tuned so the chain sums to 1).</summary>
        public float HeadRatio { get; set; } = 0.55f;
        public float NeckRatio { get; set; } = 0.25f;
        public float ChestRatio { get; set; } = 0.12f;
        public float SpineRatio { get; set; } = 0.08f;

        public bool InvertPitch { get; set; }
        public bool InvertYaw { get; set; }
        public bool InvertRoll { get; set; }

        /// <summary>Maximum eye yaw / pitch in degrees sent to the avatar's LookAt for LookX/LookY = ±1.</summary>
        public float LookYawRangeDeg { get; set; } = 25f;
        public float LookPitchRangeDeg { get; set; } = 15f;
        public float LookGain { get; set; } = 1.2f;

        /// <summary>Seconds without a face before the solver starts returning to neutral (PRD 6.3, 37.3).</summary>
        public float LostTimeoutSeconds { get; set; } = 0.35f;
        public float ReturnToNeutralSeconds { get; set; } = 0.8f;

        /// <summary>Longest smoothing time constant, seconds, reached when a smoothing value is 1.</summary>
        public float MaxSmoothingSeconds { get; set; } = 0.25f;

        public CalibrationData Calibration { get; set; } = CalibrationData.Identity;

        public FaceTrackingSettings Clone() => (FaceTrackingSettings)MemberwiseClone();

        public void Clamp()
        {
            HeadSmoothing = Clamp01(HeadSmoothing);
            ExpressionSmoothing = Clamp01(ExpressionSmoothing);
            LookSmoothing = Clamp01(LookSmoothing);
            HeadGain = Math.Min(Math.Max(HeadGain, 0f), 3f);
            HeadDeadZoneDeg = Math.Min(Math.Max(HeadDeadZoneDeg, 0f), 10f);
            LookGain = Math.Min(Math.Max(LookGain, 0f), 3f);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
