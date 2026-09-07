using System;
using VRMCast.Core.Camera;

namespace VRMCast.Core.Tracking
{
    /// <summary>Direction of one arm's segments in the avatar's normalized space (unit vectors).</summary>
    public struct ArmPose
    {
        public bool Tracked;
        public Float3 UpperArm;
        public Float3 Forearm;
    }

    public struct ArmsPose
    {
        public ArmPose Left;
        public ArmPose Right;
    }

    /// <summary>
    /// Turns shoulder / elbow / wrist world landmarks into upper-arm and forearm directions in the avatar's frame.
    /// Mirror mode (default) drives the avatar's left arm from the user's left arm as a mirror would; non-mirror
    /// swaps arms so the avatar copies the user. Low-visibility joints ease the arm back to the rest pose
    /// (hanging <see cref="BodyTrackingSettings.ArmRestAngleDeg"/> below the T-pose). Hands stay neutral: finger
    /// tracking is a later milestone (PRD 15).
    /// </summary>
    public sealed class ArmPoseSolver
    {
        private readonly BodyTrackingSettings _settings;
        private Float3 _leftUpper, _leftFore, _rightUpper, _rightFore;
        private bool _initialized;

        public ArmsPose Pose;

        public ArmPoseSolver(BodyTrackingSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Reset();
        }

        /// <summary>Rest direction of an arm: the T-pose axis (±X) rotated down by the rest angle.</summary>
        public static Float3 RestDirection(bool leftArm, float restAngleDeg)
        {
            var rad = restAngleDeg * HeadPoseMath.Deg2Rad;
            var x = (float)Math.Cos(rad) * (leftArm ? -1f : 1f);   // the avatar's left is -X
            var y = -(float)Math.Sin(rad);
            return new Float3(x, y, 0f).Normalized;
        }

        /// <summary>
        /// Maps a user-space direction (x right in the image, y down, z away from the camera) into the avatar's frame
        /// (x = avatar's right = viewer's left, y up, z toward the viewer).
        /// </summary>
        public static Float3 ToAvatarSpace(Float3 user, bool mirror)
        {
            // Image-right is the viewer's right, i.e. the avatar's -X; without mirroring the arms are swapped so the
            // sign flips back.
            return new Float3(mirror ? -user.X : user.X, -user.Y, -user.Z).Normalized;
        }

        public ArmsPose Update(in TrackingFrame frame, bool bodyRecent, float dt, bool mirrorUser)
        {
            var s = _settings;
            s.Clamp();
            var restLeft = RestDirection(true, s.ArmRestAngleDeg);
            var restRight = RestDirection(false, s.ArmRestAngleDeg);

            var enabled = s.ArmsEnabled && bodyRecent && frame.Pose != null;
            var p = frame.Pose ?? default;

            // User's left arm feeds the avatar's left arm when mirroring, the avatar's right arm otherwise.
            ComputeArm(p, true, enabled, mirrorUser, out var userLeftUpper, out var userLeftFore, out var userLeftOk);
            ComputeArm(p, false, enabled, mirrorUser, out var userRightUpper, out var userRightFore, out var userRightOk);

            Float3 targetLeftUpper, targetLeftFore, targetRightUpper, targetRightFore;
            bool leftOk, rightOk;
            if (mirrorUser)
            {
                leftOk = userLeftOk; targetLeftUpper = userLeftUpper; targetLeftFore = userLeftFore;
                rightOk = userRightOk; targetRightUpper = userRightUpper; targetRightFore = userRightFore;
            }
            else
            {
                leftOk = userRightOk; targetLeftUpper = userRightUpper; targetLeftFore = userRightFore;
                rightOk = userLeftOk; targetRightUpper = userLeftUpper; targetRightFore = userLeftFore;
            }
            if (!leftOk) { targetLeftUpper = restLeft; targetLeftFore = restLeft; }
            if (!rightOk) { targetRightUpper = restRight; targetRightFore = restRight; }

            var tau = SmoothingMath.Tau(leftOk || rightOk ? s.ArmSmoothing : 0.8f, s.MaxSmoothingSeconds);
            var alpha = tau <= 0f || dt <= 0f ? 1f : 1f - (float)Math.Exp(-dt / tau);
            if (!_initialized) { alpha = 1f; _initialized = true; }

            _leftUpper = Blend(_leftUpper, targetLeftUpper, alpha);
            _leftFore = Blend(_leftFore, targetLeftFore, alpha);
            _rightUpper = Blend(_rightUpper, targetRightUpper, alpha);
            _rightFore = Blend(_rightFore, targetRightFore, alpha);

            Pose.Left = new ArmPose { Tracked = leftOk, UpperArm = _leftUpper, Forearm = _leftFore };
            Pose.Right = new ArmPose { Tracked = rightOk, UpperArm = _rightUpper, Forearm = _rightFore };
            return Pose;
        }

        private void ComputeArm(in PoseTracking p, bool userLeft, bool enabled, bool mirror, out Float3 upper, out Float3 fore, out bool ok)
        {
            upper = default;
            fore = default;
            ok = false;
            if (!enabled) return;

            float sx, sy, sz, ex, ey, ez, wx, wy, wz, ev, wv;
            if (userLeft)
            {
                sx = p.LeftShoulderX; sy = p.LeftShoulderY; sz = p.LeftShoulderZ;
                ex = p.LeftElbowX; ey = p.LeftElbowY; ez = p.LeftElbowZ; ev = p.LeftElbowVisibility;
                wx = p.LeftWristX; wy = p.LeftWristY; wz = p.LeftWristZ; wv = p.LeftWristVisibility;
            }
            else
            {
                sx = p.RightShoulderX; sy = p.RightShoulderY; sz = p.RightShoulderZ;
                ex = p.RightElbowX; ey = p.RightElbowY; ez = p.RightElbowZ; ev = p.RightElbowVisibility;
                wx = p.RightWristX; wy = p.RightWristY; wz = p.RightWristZ; wv = p.RightWristVisibility;
            }
            if (ev < _settings.ArmMinVisibility) return;

            var upperUser = new Float3(ex - sx, ey - sy, ez - sz);
            if (upperUser.Length < 0.02f) return;
            upper = ToAvatarSpace(upperUser, mirror);

            if (wv >= _settings.ArmMinVisibility)
            {
                var foreUser = new Float3(wx - ex, wy - ey, wz - ez);
                fore = foreUser.Length < 0.02f ? upper : ToAvatarSpace(foreUser, mirror);
            }
            else
            {
                fore = upper; // wrist hidden: keep the arm straight
            }
            ok = true;
        }

        private static Float3 Blend(Float3 current, Float3 target, float alpha)
        {
            var v = current + (target - current) * alpha;
            return v.Length < 1e-4f ? target : v.Normalized;
        }

        public void Reset()
        {
            _initialized = false;
            _leftUpper = _leftFore = RestDirection(true, _settings.ArmRestAngleDeg);
            _rightUpper = _rightFore = RestDirection(false, _settings.ArmRestAngleDeg);
            Pose = new ArmsPose
            {
                Left = new ArmPose { UpperArm = _leftUpper, Forearm = _leftFore },
                Right = new ArmPose { UpperArm = _rightUpper, Forearm = _rightFore },
            };
        }
    }
}
