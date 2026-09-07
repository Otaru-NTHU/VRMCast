using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// What the avatar should do this frame, in avatar terms (mirroring already applied). Consumed by the runtime
    /// driver, which knows about bones and UniVRM; this type does not.
    /// </summary>
    public sealed class AvatarPose
    {
        /// <summary>Total head rotation, radians, in the avatar's own frame (pitch down, yaw toward avatar's left, roll top toward avatar's left).</summary>
        public float HeadPitchRad;
        public float HeadYawRad;
        public float HeadRollRad;

        /// <summary>Share of the rotation each bone receives; the driver multiplies the angles by these.</summary>
        public float HeadRatio = 1f;
        public float NeckRatio;
        public float ChestRatio;
        public float SpineRatio;

        /// <summary>Eye direction in degrees for the avatar's LookAt (yaw positive toward the avatar's right, pitch positive up).</summary>
        public float LookYawDeg;
        public float LookPitchDeg;

        public readonly Dictionary<string, float> Expressions = new Dictionary<string, float>();

        public bool HasFace;
        public float Confidence;

        public void Clear()
        {
            HeadPitchRad = HeadYawRad = HeadRollRad = 0f;
            LookYawDeg = LookPitchDeg = 0f;
            Expressions.Clear();
            HasFace = false;
            Confidence = 0f;
        }
    }
}
