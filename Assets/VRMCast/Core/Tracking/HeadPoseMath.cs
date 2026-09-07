using System;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// Extracts head angles from the face's basis vectors in camera space (Unity-style left-handed: x right in the
    /// image, y up, z away from the camera toward the user). Conventions match <see cref="HeadTracking"/>:
    /// pitch positive looks down, yaw positive turns toward the user's own left, roll positive tilts the top of the
    /// head toward the image's right (clockwise as seen by the camera).
    /// </summary>
    public static class HeadPoseMath
    {
        /// <param name="fx">Face forward axis (out of the face, toward the camera) in camera space.</param>
        /// <param name="ux">Face up axis in camera space.</param>
        public static void ExtractAngles(float fx, float fy, float fz, float ux, float uy, float uz,
            out float pitchRad, out float yawRad, out float rollRad)
        {
            // Facing the camera, forward is (0, 0, -1). Turning toward the user's left moves the nose toward image +X
            // (a non-mirrored image shows the user's left on its right).
            yawRad = (float)Math.Atan2(fx, -fz);
            var horizontal = (float)Math.Sqrt(fx * fx + fz * fz);
            pitchRad = (float)Math.Atan2(-fy, horizontal);

            // Roll from the up axis projected onto the image plane.
            rollRad = (float)Math.Atan2(ux, uy);

            if (float.IsNaN(yawRad)) yawRad = 0f;
            if (float.IsNaN(pitchRad)) pitchRad = 0f;
            if (float.IsNaN(rollRad)) rollRad = 0f;
        }

        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);
    }
}
