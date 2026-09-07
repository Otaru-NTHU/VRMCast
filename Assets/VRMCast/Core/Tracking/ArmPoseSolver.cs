using System;
using VRMCast.Core.Camera;

namespace VRMCast.Core.Tracking
{
    /// <summary>Direction of one arm's segments in the avatar's normalized space (unit vectors), plus the hand's orientation.</summary>
    public struct ArmPose
    {
        public bool Tracked;
        /// <summary>True when the wrist target came from the hand landmarker (arm solved by IK).</summary>
        public bool FromHand;
        public Float3 UpperArm;
        public Float3 Forearm;
        /// <summary>Palm orientation from the hand landmarks: fingers direction and the direction the palm faces.</summary>
        public bool HasHandOrientation;
        public Float3 HandForward;
        public Float3 HandNormal;
    }

    public struct ArmsPose
    {
        public ArmPose Left;
        public ArmPose Right;
    }

    /// <summary>
    /// Solves both arms in the avatar's frame.
    ///
    /// Sides: the pose landmarks label the user's left and right for an unmirrored camera picture, and the hands
    /// are matched to those wrists by <see cref="HandFrameBuilder.ResolveSides"/>, so arms and fingers always agree.
    /// Mirror mode makes the avatar the user's reflection (real left arm → avatar's right arm, which is on the same
    /// side of the screen); copy mode drives left with left. A camera that delivers a mirrored picture swaps every
    /// label the same way, which the swap-sides setting undoes.
    ///
    /// When the hand landmarker sees a hand, its wrist is the target: the wrist's image position and the palm's
    /// apparent size (against the metric hand landmarks) give a 3D point relative to the shoulder under a pinhole
    /// camera with an assumed 60° horizontal field of view; the point is scaled from the user's reach to the avatar's
    /// and a two-bone IK places the elbow, using the pose elbow as the bend hint when visible. Without a hand the
    /// pose landmarks drive the segment directions directly; without those the arm eases to the rest pose. The palm
    /// orientation (fingers direction and palm normal) is passed on for the wrist.
    /// </summary>
    public sealed class ArmPoseSolver
    {
        /// <summary>Focal length in image-width units for a 60° horizontal field of view (typical laptop webcam).</summary>
        public const float AssumedFocal = 0.87f;
        /// <summary>How long a hand detection keeps steering the arm after the last hand frame.</summary>
        public const float HandHoldSeconds = 0.3f;
        public const float HandMinConfidence = 0.5f;
        private const float DefaultUserReach = 0.55f;

        private readonly BodyTrackingSettings _settings;
        private Float3 _leftUpper, _leftFore, _rightUpper, _rightFore;
        private Float3 _leftHandF, _leftHandN, _rightHandF, _rightHandN;
        private bool _initialized;
        private float _userReach = DefaultUserReach;

        private HandTracking? _userLeftHand, _userRightHand;
        private double _userLeftHandAt = double.NegativeInfinity, _userRightHandAt = double.NegativeInfinity;

        public ArmsPose Pose;

        /// <summary>Avatar bone lengths in meters (set from the loaded model; defaults suit a 1.6 m avatar).</summary>
        public float UpperArmLength { get; private set; } = 0.26f;
        public float ForearmLength { get; private set; } = 0.24f;

        /// <summary>Estimated user arm reach (shoulder to wrist, meters), learned from the pose landmarks.</summary>
        public float UserReach => _userReach;

        public ArmPoseSolver(BodyTrackingSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Reset();
        }

        public void SetAvatarArm(float upperArmLength, float forearmLength)
        {
            if (upperArmLength > 0.02f) UpperArmLength = upperArmLength;
            if (forearmLength > 0.02f) ForearmLength = forearmLength;
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
        /// Maps a camera-space vector (x right in the image, y down, z away from the camera) into the avatar's frame
        /// (x = avatar's right = viewer's left, y up, z toward the viewer), without normalizing. An unmirrored picture
        /// shows the user's left on image-right, so image-x is "user's left positive"; mirror keeps that as +X (the
        /// reflection shows the user's left on screen-left), copy flips it (the avatar's own left is -X).
        /// </summary>
        public static Float3 MapAxes(Float3 camera, bool mirror)
        {
            return new Float3(mirror ? camera.X : -camera.X, -camera.Y, -camera.Z);
        }

        public static Float3 ToAvatarSpace(Float3 camera, bool mirror) => MapAxes(camera, mirror).Normalized;

        /// <summary>Feeds a hand frame whose sides are the user's real sides (after <see cref="HandFrameBuilder.ResolveSides"/>).</summary>
        public void SubmitHands(in TrackingFrame hands, double now)
        {
            if (hands.LeftHand.HasValue) { _userLeftHand = hands.LeftHand; _userLeftHandAt = now; }
            if (hands.RightHand.HasValue) { _userRightHand = hands.RightHand; _userRightHandAt = now; }
        }

        public ArmsPose Update(in TrackingFrame frame, bool bodyRecent, float dt, bool mirrorUser)
            => Update(frame, bodyRecent, double.NegativeInfinity, dt, mirrorUser, false);

        public ArmsPose Update(in TrackingFrame frame, bool bodyRecent, double now, float dt, bool mirrorUser, bool swapSides)
        {
            var s = _settings;
            s.Clamp();
            var restLeft = RestDirection(true, s.ArmRestAngleDeg);
            var restRight = RestDirection(false, s.ArmRestAngleDeg);

            var enabled = s.ArmsEnabled && bodyRecent && frame.Pose != null;
            var p = frame.Pose ?? default;
            if (frame.Pose != null) LearnReach(p);

            // Real sides of the user, then the mirror rule decides which avatar arm each one drives.
            var realLeft = ComputeArm(p, true, enabled, mirrorUser, now);
            var realRight = ComputeArm(p, false, enabled, mirrorUser, now);
            var toAvatarLeft = mirrorUser ? realRight : realLeft;
            var toAvatarRight = mirrorUser ? realLeft : realRight;
            if (swapSides) { var tmp = toAvatarLeft; toAvatarLeft = toAvatarRight; toAvatarRight = tmp; }

            if (!toAvatarLeft.Tracked) { toAvatarLeft.UpperArm = restLeft; toAvatarLeft.Forearm = restLeft; }
            if (!toAvatarRight.Tracked) { toAvatarRight.UpperArm = restRight; toAvatarRight.Forearm = restRight; }

            var tau = SmoothingMath.Tau(toAvatarLeft.Tracked || toAvatarRight.Tracked ? s.ArmSmoothing : 0.8f, s.MaxSmoothingSeconds);
            var alpha = tau <= 0f || dt <= 0f ? 1f : 1f - (float)Math.Exp(-dt / tau);
            if (!_initialized) { alpha = 1f; _initialized = true; }

            _leftUpper = Blend(_leftUpper, toAvatarLeft.UpperArm, alpha);
            _leftFore = Blend(_leftFore, toAvatarLeft.Forearm, alpha);
            _rightUpper = Blend(_rightUpper, toAvatarRight.UpperArm, alpha);
            _rightFore = Blend(_rightFore, toAvatarRight.Forearm, alpha);
            if (toAvatarLeft.HasHandOrientation)
            {
                _leftHandF = Blend(_leftHandF, toAvatarLeft.HandForward, alpha);
                _leftHandN = Blend(_leftHandN, toAvatarLeft.HandNormal, alpha);
            }
            if (toAvatarRight.HasHandOrientation)
            {
                _rightHandF = Blend(_rightHandF, toAvatarRight.HandForward, alpha);
                _rightHandN = Blend(_rightHandN, toAvatarRight.HandNormal, alpha);
            }

            Pose.Left = new ArmPose
            {
                Tracked = toAvatarLeft.Tracked, FromHand = toAvatarLeft.FromHand, UpperArm = _leftUpper, Forearm = _leftFore,
                HasHandOrientation = toAvatarLeft.HasHandOrientation, HandForward = _leftHandF, HandNormal = _leftHandN,
            };
            Pose.Right = new ArmPose
            {
                Tracked = toAvatarRight.Tracked, FromHand = toAvatarRight.FromHand, UpperArm = _rightUpper, Forearm = _rightFore,
                HasHandOrientation = toAvatarRight.HasHandOrientation, HandForward = _rightHandF, HandNormal = _rightHandN,
            };
            return Pose;
        }

        private void LearnReach(in PoseTracking p)
        {
            for (var side = 0; side < 2; side++)
            {
                GetJoints(p, side == 0, out var sh, out var el, out var wr, out var ev, out var wv);
                if (ev < _settings.ArmMinVisibility || wv < _settings.ArmMinVisibility) continue;
                var reach = (el - sh).Length + (wr - el).Length;
                if (reach < 0.3f || reach > 0.9f) continue;
                _userReach = Math.Max(_userReach, reach);
            }
            // Drift slowly back toward the default so one noisy frame cannot shrink the avatar's arms forever.
            _userReach += (DefaultUserReach - _userReach) * 0.0015f;
        }

        /// <summary>Joints by pose label (the user's own sides for an unmirrored picture).</summary>
        private static void GetJoints(in PoseTracking p, bool labelLeft, out Float3 shoulder, out Float3 elbow, out Float3 wrist, out float elbowVis, out float wristVis)
        {
            if (labelLeft)
            {
                shoulder = new Float3(p.LeftShoulderX, p.LeftShoulderY, p.LeftShoulderZ);
                elbow = new Float3(p.LeftElbowX, p.LeftElbowY, p.LeftElbowZ);
                wrist = new Float3(p.LeftWristX, p.LeftWristY, p.LeftWristZ);
                elbowVis = p.LeftElbowVisibility; wristVis = p.LeftWristVisibility;
            }
            else
            {
                shoulder = new Float3(p.RightShoulderX, p.RightShoulderY, p.RightShoulderZ);
                elbow = new Float3(p.RightElbowX, p.RightElbowY, p.RightElbowZ);
                wrist = new Float3(p.RightWristX, p.RightWristY, p.RightWristZ);
                elbowVis = p.RightElbowVisibility; wristVis = p.RightWristVisibility;
            }
        }

        private static void GetShoulderImage(in PoseTracking p, bool labelLeft, out float u, out float v)
        {
            u = labelLeft ? p.LeftShoulderU : p.RightShoulderU;
            v = labelLeft ? p.LeftShoulderV : p.RightShoulderV;
        }

        private ArmPose ComputeArm(in PoseTracking p, bool realLeft, bool enabled, bool mirror, double now)
        {
            var result = new ArmPose();
            if (!enabled) return result;

            var labelLeft = realLeft;
            GetJoints(p, labelLeft, out var shoulder, out var elbow, out var wrist, out var elbowVis, out var wristVis);
            var elbowOk = elbowVis >= _settings.ArmMinVisibility;

            // Avatar side that this real arm ends up on decides the outward direction for the default elbow hint.
            var avatarLeft = mirror ? !realLeft : realLeft;
            var outward = new Float3(avatarLeft ? -1f : 1f, 0f, 0f);

            var hand = realLeft ? _userLeftHand : _userRightHand;
            var handAt = realLeft ? _userLeftHandAt : _userRightHandAt;
            var handFresh = hand.HasValue && now - handAt <= HandHoldSeconds && hand.Value.Confidence >= HandMinConfidence
                            && hand.Value.LandmarksXyz != null;

            if (handFresh && TryHandTarget(p, labelLeft, hand.Value, out var deltaCamera))
            {
                var scale = (UpperArmLength + ForearmLength) / Math.Max(0.3f, _userReach);
                var target = MapAxes(deltaCamera, mirror) * scale;
                Float3? pole = null;
                if (elbowOk)
                {
                    var e = MapAxes(elbow - shoulder, mirror) * scale;
                    if (e.Length > 0.03f) pole = e;
                }
                SolveTwoBone(target, pole, outward, out result.UpperArm, out result.Forearm);
                result.Tracked = true;
                result.FromHand = true;
            }
            else if (elbowOk)
            {
                var upperUser = elbow - shoulder;
                if (upperUser.Length < 0.02f) return result;
                result.UpperArm = ToAvatarSpace(upperUser, mirror);
                if (wristVis >= _settings.ArmMinVisibility)
                {
                    var foreUser = wrist - elbow;
                    result.Forearm = foreUser.Length < 0.02f ? result.UpperArm : ToAvatarSpace(foreUser, mirror);
                }
                else
                {
                    result.Forearm = result.UpperArm; // wrist hidden: keep the arm straight
                }
                result.Tracked = true;
            }
            else
            {
                return result;
            }

            if (handFresh && TryHandOrientation(hand.Value, realLeft, mirror, out var f, out var n))
            {
                result.HasHandOrientation = true;
                result.HandForward = f;
                result.HandNormal = n;
            }
            return result;
        }

        /// <summary>
        /// Wrist position relative to the shoulder, camera frame, meters. Depth comes from apparent sizes under a pinhole
        /// model: the shoulders (metric width from the pose world landmarks) fix the shoulder depth, the palm (metric
        /// size from the hand world landmarks) fixes the hand depth.
        /// </summary>
        private static bool TryHandTarget(in PoseTracking p, bool labelLeft, in HandTracking hand, out Float3 delta)
        {
            delta = default;
            if (!p.HasImageCoords) return false;
            var aspect = p.ImageAspect > 0f ? p.ImageAspect : 1f;

            var swWorld = (float)Math.Sqrt(Sq(p.LeftShoulderX - p.RightShoulderX) + Sq(p.LeftShoulderY - p.RightShoulderY) + Sq(p.LeftShoulderZ - p.RightShoulderZ));
            var sw2D = Dist2D(p.LeftShoulderU, p.LeftShoulderV, p.RightShoulderU, p.RightShoulderV, aspect);
            if (swWorld < 0.15f || sw2D < 0.02f) return false;
            var shoulderDepth = AssumedFocal * swWorld / sw2D;

            var w = hand.LandmarksXyz;
            var palm1World = Dist3(w, HandFrameBuilder.Wrist, HandFrameBuilder.MiddleMcp);
            var palm2World = Dist3(w, HandFrameBuilder.IndexMcp, HandFrameBuilder.LittleMcp);
            var palm1Image = Dist2D(hand.WristU, hand.WristV, hand.MiddleMcpU, hand.MiddleMcpV, aspect);
            var palm2Image = Dist2D(hand.IndexMcpU, hand.IndexMcpV, hand.LittleMcpU, hand.LittleMcpV, aspect);
            // The least foreshortened palm axis gives the truest scale (meters per image unit).
            var scale = float.MaxValue;
            if (palm1World > 0.03f && palm1Image > 0.005f) scale = Math.Min(scale, palm1World / palm1Image);
            if (palm2World > 0.03f && palm2Image > 0.005f) scale = Math.Min(scale, palm2World / palm2Image);
            if (scale == float.MaxValue) return false;
            var handDepth = AssumedFocal * scale;
            handDepth = SmoothingMath.Clamp(handDepth, shoulderDepth * 0.5f, shoulderDepth * 1.5f);

            GetShoulderImage(p, labelLeft, out var su, out var sv);
            var shoulderPos = Unproject(su, sv, aspect, shoulderDepth);
            var handPos = Unproject(hand.WristU, hand.WristV, aspect, handDepth);
            delta = handPos - shoulderPos;
            return delta.Length > 0.03f;
        }

        private static Float3 Unproject(float u, float v, float aspect, float depth)
        {
            return new Float3((u - 0.5f) * depth / AssumedFocal, (v - 0.5f) / aspect * depth / AssumedFocal, depth);
        }

        /// <summary>Palm axes in the avatar frame from the metric hand landmarks.</summary>
        private static bool TryHandOrientation(in HandTracking hand, bool realLeft, bool mirror, out Float3 forward, out Float3 normal)
        {
            forward = default; normal = default;
            var w = hand.LandmarksXyz;
            var wrist = At(w, HandFrameBuilder.Wrist);
            var f = At(w, HandFrameBuilder.MiddleMcp) - wrist;
            var lateral = At(w, HandFrameBuilder.IndexMcp) - At(w, HandFrameBuilder.LittleMcp);
            if (f.Length < 0.02f || lateral.Length < 0.02f) return false;
            // Camera frame is right-handed-as-seen (x right, y down, z away): cross(forward, index-little) points toward
            // the camera for a left palm facing it and away for a right palm, hence the sign.
            var n = Float3.Cross(f, lateral) * (realLeft ? 1f : -1f);
            if (n.Length < 1e-5f) return false;
            forward = ToAvatarSpace(f, mirror);
            normal = ToAvatarSpace(n, mirror);
            return true;
        }

        /// <summary>
        /// Two-bone IK from the shoulder (origin) to <paramref name="target"/> with the avatar's bone lengths. The elbow
        /// lies in the plane of the target and the pole; without a pole it hangs down and slightly outward/back.
        /// </summary>
        public void SolveTwoBone(Float3 target, Float3? pole, Float3 outward, out Float3 upper, out Float3 forearm)
        {
            var a = UpperArmLength;
            var b = ForearmLength;
            var len = target.Length;
            if (len < 1e-4f)
            {
                upper = forearm = new Float3(0f, -1f, 0f);
                return;
            }
            var n = target / len;
            var d = SmoothingMath.Clamp(len, Math.Abs(a - b) + 0.01f, (a + b) * 0.995f);

            var poleDir = pole ?? (new Float3(0f, -1f, 0f) + outward * 0.5f + new Float3(0f, 0f, -0.3f));
            var perp = poleDir - n * Float3.Dot(poleDir, n);
            if (perp.Length < 1e-3f)
            {
                perp = new Float3(0f, -1f, -0.3f) + outward * 0.5f;
                perp = perp - n * Float3.Dot(perp, n);
                if (perp.Length < 1e-3f) perp = new Float3(0f, 0f, -1f) - n * Float3.Dot(new Float3(0f, 0f, -1f), n);
            }
            perp = perp.Normalized;

            var cos = SmoothingMath.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            var sin = (float)Math.Sqrt(Math.Max(0f, 1f - cos * cos));
            var elbow = n * (a * cos) + perp * (a * sin);
            var toTarget = n * d - elbow;
            upper = elbow.Normalized;
            forearm = toTarget.Length < 1e-5f ? upper : toTarget.Normalized;
        }

        private static float Sq(float v) => v * v;
        private static Float3 At(float[] w, int i) => new Float3(w[i * 3], w[i * 3 + 1], w[i * 3 + 2]);
        private static float Dist3(float[] w, int i, int j) => (At(w, i) - At(w, j)).Length;
        private static float Dist2D(float u0, float v0, float u1, float v1, float aspect)
        {
            var du = u0 - u1;
            var dv = (v0 - v1) / aspect;
            return (float)Math.Sqrt(du * du + dv * dv);
        }

        private static Float3 Blend(Float3 current, Float3 target, float alpha)
        {
            var v = current + (target - current) * alpha;
            return v.Length < 1e-4f ? target : v.Normalized;
        }

        public void Reset()
        {
            _initialized = false;
            _userLeftHand = _userRightHand = null;
            _userLeftHandAt = _userRightHandAt = double.NegativeInfinity;
            _leftUpper = _leftFore = RestDirection(true, _settings.ArmRestAngleDeg);
            _rightUpper = _rightFore = RestDirection(false, _settings.ArmRestAngleDeg);
            _leftHandF = new Float3(-1f, 0f, 0f); _leftHandN = new Float3(0f, -1f, 0f);
            _rightHandF = new Float3(1f, 0f, 0f); _rightHandN = new Float3(0f, -1f, 0f);
            Pose = new ArmsPose
            {
                Left = new ArmPose { UpperArm = _leftUpper, Forearm = _leftFore, HandForward = _leftHandF, HandNormal = _leftHandN },
                Right = new ArmPose { UpperArm = _rightUpper, Forearm = _rightFore, HandForward = _rightHandF, HandNormal = _rightHandN },
            };
        }
    }
}
