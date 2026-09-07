using System;
using VRMCast.Core.Camera;

namespace VRMCast.Core.Tracking
{
    public enum Finger
    {
        Thumb = 0,
        Index = 1,
        Middle = 2,
        Ring = 3,
        Little = 4,
    }

    /// <summary>Tuning for finger tracking (PRD 15). Curl is per finger, 0 = straight, 1 = fully bent.</summary>
    public sealed class HandTrackingSettings
    {
        public float Smoothing { get; set; } = 0.35f;
        public float MaxSmoothingSeconds { get; set; } = 0.3f;
        /// <summary>Handedness score below which a detected hand is ignored.</summary>
        public float MinConfidence { get; set; } = 0.5f;
        /// <summary>Seconds without a hand before its fingers ease back to <see cref="RestCurl"/>.</summary>
        public float LostTimeoutSeconds { get; set; } = 0.4f;
        public float CurlGain { get; set; } = 1f;
        /// <summary>Relaxed hand: slightly curled fingers look more natural than a flat hand.</summary>
        public float RestCurl { get; set; } = 0.1f;
        /// <summary>
        /// Swaps left and right for arms and fingers together. Needed when the camera delivers a mirrored picture:
        /// every landmark label is then flipped the same way, which the solvers cannot tell from geometry.
        /// </summary>
        public bool SwapHands { get; set; }

        public void Clamp()
        {
            Smoothing = SmoothingMath.Clamp01(Smoothing);
            MinConfidence = SmoothingMath.Clamp01(MinConfidence);
            CurlGain = SmoothingMath.Clamp(CurlGain, 0f, 3f);
            RestCurl = SmoothingMath.Clamp01(RestCurl);
        }
    }

    /// <summary>Finger curl of one of the avatar's hands, 0..1 each.</summary>
    public struct HandPose
    {
        public bool Tracked;
        public float Thumb, Index, Middle, Ring, Little;

        public float Curl(Finger finger)
        {
            switch (finger)
            {
                case Finger.Thumb: return Thumb;
                case Finger.Index: return Index;
                case Finger.Middle: return Middle;
                case Finger.Ring: return Ring;
                default: return Little;
            }
        }

        public static HandPose Uniform(float curl, bool tracked = false) =>
            new HandPose { Tracked = tracked, Thumb = curl, Index = curl, Middle = curl, Ring = curl, Little = curl };
    }

    public struct HandsPose
    {
        public HandPose Left;
        public HandPose Right;
    }

    /// <summary>
    /// Builds the hand blocks of a <see cref="TrackingFrame"/> from MediaPipe Hand Landmarker output. Landmark order:
    /// 0 wrist; 1–4 thumb (CMC, MCP, IP, tip); 5–8 index (MCP, PIP, DIP, tip); 9–12 middle; 13–16 ring; 17–20 little.
    /// </summary>
    public static class HandFrameBuilder
    {
        public const int LandmarkCount = 21;
        public const int Wrist = 0;
        public const int ThumbCmc = 1, ThumbMcp = 2, ThumbIp = 3, ThumbTip = 4;
        public const int IndexMcp = 5, IndexPip = 6, IndexDip = 7, IndexTip = 8;
        public const int MiddleMcp = 9, MiddlePip = 10, MiddleDip = 11, MiddleTip = 12;
        public const int RingMcp = 13, RingPip = 14, RingDip = 15, RingTip = 16;
        public const int LittleMcp = 17, LittlePip = 18, LittleDip = 19, LittleTip = 20;

        /// <summary>
        /// Resolves a MediaPipe handedness label ("Left"/"Right") to the user's own side. The model assumes a mirrored
        /// selfie image; the app feeds the camera picture unmirrored, so the labels are swapped unless the picture is
        /// mirrored at the source. <paramref name="swap"/> flips the result once more (user setting).
        /// </summary>
        public static bool IsUserLeft(string mediaPipeLabel, bool imageIsMirrored, bool swap = false)
        {
            var labelLeft = string.Equals(mediaPipeLabel, "Left", StringComparison.OrdinalIgnoreCase);
            return IsUserLeft(labelLeft, imageIsMirrored, swap);
        }

        public static bool IsUserLeft(bool labelLeft, bool imageIsMirrored, bool swap = false)
        {
            var userLeft = imageIsMirrored ? labelLeft : !labelLeft;
            return swap ? !userLeft : userLeft;
        }

        /// <summary>
        /// Packs up to two raw detections (labels unresolved) into a frame. Call <see cref="ResolveSides"/> before the
        /// frame reaches a solver; until then <c>LeftHand</c>/<c>RightHand</c> only reflect the raw labels.
        /// </summary>
        public static TrackingFrame Build(double timestamp, HandTracking? first, HandTracking? second)
        {
            var frame = TrackingFrame.Empty(timestamp);
            Place(ref frame, first);
            Place(ref frame, second);
            return frame;
        }

        private static void Place(ref TrackingFrame frame, HandTracking? hand)
        {
            if (!hand.HasValue || hand.Value.LandmarksXyz == null || hand.Value.LandmarksXyz.Length < LandmarkCount * 3) return;
            var h = hand.Value;
            if (h.LabelLeft)
            {
                if (!frame.LeftHand.HasValue || frame.LeftHand.Value.Confidence < h.Confidence) frame.LeftHand = h;
                else if (!frame.RightHand.HasValue) frame.RightHand = h;
            }
            else
            {
                if (!frame.RightHand.HasValue || frame.RightHand.Value.Confidence < h.Confidence) frame.RightHand = h;
                else if (!frame.LeftHand.HasValue) frame.LeftHand = h;
            }
        }

        /// <summary>Convenience for tests and simple providers: builds a frame whose sides are already the user's.</summary>
        public static TrackingFrame Build(double timestamp, float[] leftWorld, float leftScore, float[] rightWorld, float rightScore)
        {
            var frame = TrackingFrame.Empty(timestamp);
            if (leftWorld != null && leftWorld.Length >= LandmarkCount * 3)
            {
                frame.LeftHand = new HandTracking { Confidence = leftScore, LandmarksXyz = leftWorld, LabelLeft = true };
            }
            if (rightWorld != null && rightWorld.Length >= LandmarkCount * 3)
            {
                frame.RightHand = new HandTracking { Confidence = rightScore, LandmarksXyz = rightWorld, LabelLeft = false };
            }
            return frame;
        }

        /// <summary>Normalized distance at which a hand is matched to a pose wrist (width-normalized image units).</summary>
        public const float WristMatchDistance = 0.12f;

        /// <summary>
        /// Assigns the detected hands to the user's left/right so that fingers always belong to the arm the pose drives.
        /// When the pose gives wrist image positions, each hand goes to the nearest visible pose wrist; otherwise the
        /// handedness label is used (<paramref name="imageMirrored"/> says which way MediaPipe's label convention
        /// applies). <paramref name="swap"/> flips the final result.
        /// </summary>
        public static void ResolveSides(ref TrackingFrame hands, PoseTracking? pose, bool imageMirrored, bool swap)
        {
            var a = hands.LeftHand;
            var b = hands.RightHand;
            hands.LeftHand = null;
            hands.RightHand = null;

            var havePose = pose.HasValue && pose.Value.HasImageCoords;
            float realLeftU = 0, realLeftV = 0, realRightU = 0, realRightV = 0, realLeftVis = 0, realRightVis = 0, aspect = 1f;
            if (havePose)
            {
                var p = pose.Value;
                aspect = p.ImageAspect > 0f ? p.ImageAspect : 1f;
                realLeftU = p.LeftWristU; realLeftV = p.LeftWristV; realLeftVis = p.LeftWristVisibility;
                realRightU = p.RightWristU; realRightV = p.RightWristV; realRightVis = p.RightWristVisibility;
            }

            bool? Side(HandTracking? hand)
            {
                if (!hand.HasValue) return null;
                var h = hand.Value;
                if (havePose)
                {
                    var dl = realLeftVis >= 0.3f ? Dist(h.WristU, h.WristV, realLeftU, realLeftV, aspect) : float.MaxValue;
                    var dr = realRightVis >= 0.3f ? Dist(h.WristU, h.WristV, realRightU, realRightV, aspect) : float.MaxValue;
                    if (Math.Min(dl, dr) <= WristMatchDistance) return dl <= dr;
                }
                return IsUserLeft(h.LabelLeft, imageMirrored, false);
            }

            var sideA = Side(a);
            var sideB = Side(b);
            if (sideA.HasValue && sideB.HasValue && sideA.Value == sideB.Value)
            {
                // Both claim the same side: the one nearer that pose wrist keeps it, the other takes the free side.
                var ha = a.Value; var hb = b.Value;
                var target = sideA.Value;
                var tu = target ? realLeftU : realRightU;
                var tv = target ? realLeftV : realRightV;
                var keepA = !havePose ? ha.Confidence >= hb.Confidence : Dist(ha.WristU, ha.WristV, tu, tv, aspect) <= Dist(hb.WristU, hb.WristV, tu, tv, aspect);
                if (keepA) sideB = !target; else sideA = !target;
            }

            HandTracking? left = null, right = null;
            void Put(HandTracking? hand, bool? side)
            {
                if (!hand.HasValue || !side.HasValue) return;
                var isLeft = swap ? !side.Value : side.Value;
                if (isLeft) left = hand; else right = hand;
            }
            Put(a, sideA);
            Put(b, sideB);
            hands.LeftHand = left;
            hands.RightHand = right;
        }

        private static float Dist(float u0, float v0, float u1, float v1, float aspect)
        {
            var du = u0 - u1;
            var dv = (v0 - v1) / aspect;
            return (float)Math.Sqrt(du * du + dv * dv);
        }

        public static TrackingFrame NoHands(double timestamp) => TrackingFrame.Empty(timestamp);
    }

    /// <summary>Joint-angle based curl estimate that does not depend on the hand's orientation or on which hand it is.</summary>
    public static class FingerCurl
    {
        /// <summary>Summed bend below this many degrees counts as straight (landmark noise on an open hand).</summary>
        public const float FingerOffsetDeg = 25f;
        /// <summary>Summed bend (MCP + PIP + DIP) that counts as a full curl.</summary>
        public const float FingerRangeDeg = 170f;
        public const float ThumbOffsetDeg = 15f;
        public const float ThumbRangeDeg = 85f;

        private static Float3 At(float[] w, int i) => new Float3(w[i * 3], w[i * 3 + 1], w[i * 3 + 2]);

        /// <summary>Angle in degrees between two segment vectors (0 = collinear, straight).</summary>
        public static float BendDeg(Float3 a, Float3 b)
        {
            var la = a.Length;
            var lb = b.Length;
            if (la < 1e-6f || lb < 1e-6f) return 0f;
            var c = SmoothingMath.Clamp(Float3.Dot(a, b) / (la * lb), -1f, 1f);
            return (float)(Math.Acos(c) * HeadPoseMath.Rad2Deg);
        }

        /// <summary>Curl 0..1 for one finger from 21 world landmarks.</summary>
        public static float Compute(float[] w, Finger finger)
        {
            if (w == null || w.Length < HandFrameBuilder.LandmarkCount * 3) return 0f;
            int a, b, c, d;
            float offset, range;
            switch (finger)
            {
                case Finger.Thumb:
                    a = HandFrameBuilder.ThumbCmc; b = HandFrameBuilder.ThumbMcp; c = HandFrameBuilder.ThumbIp; d = HandFrameBuilder.ThumbTip;
                    offset = ThumbOffsetDeg; range = ThumbRangeDeg;
                    break;
                case Finger.Index:
                    a = HandFrameBuilder.IndexMcp; b = HandFrameBuilder.IndexPip; c = HandFrameBuilder.IndexDip; d = HandFrameBuilder.IndexTip;
                    offset = FingerOffsetDeg; range = FingerRangeDeg;
                    break;
                case Finger.Middle:
                    a = HandFrameBuilder.MiddleMcp; b = HandFrameBuilder.MiddlePip; c = HandFrameBuilder.MiddleDip; d = HandFrameBuilder.MiddleTip;
                    offset = FingerOffsetDeg; range = FingerRangeDeg;
                    break;
                case Finger.Ring:
                    a = HandFrameBuilder.RingMcp; b = HandFrameBuilder.RingPip; c = HandFrameBuilder.RingDip; d = HandFrameBuilder.RingTip;
                    offset = FingerOffsetDeg; range = FingerRangeDeg;
                    break;
                default:
                    a = HandFrameBuilder.LittleMcp; b = HandFrameBuilder.LittlePip; c = HandFrameBuilder.LittleDip; d = HandFrameBuilder.LittleTip;
                    offset = FingerOffsetDeg; range = FingerRangeDeg;
                    break;
            }
            var wrist = At(w, HandFrameBuilder.Wrist);
            var pa = At(w, a);
            var pb = At(w, b);
            var pc = At(w, c);
            var pd = At(w, d);
            var s0 = pa - wrist;   // palm segment (or thumb metacarpal base)
            var s1 = pb - pa;
            var s2 = pc - pb;
            var s3 = pd - pc;
            var total = finger == Finger.Thumb
                ? BendDeg(s1, s2) + BendDeg(s2, s3)               // the thumb's base joint moves sideways, not a curl
                : BendDeg(s0, s1) + BendDeg(s1, s2) + BendDeg(s2, s3);
            var byAngle = SmoothingMath.Clamp01((total - offset) / range);

            // Depth from a single camera is the weakest landmark axis, so bends toward the camera read shallow. The
            // tip-to-knuckle distance against the finger's own length shrinks whatever the direction of the bend;
            // take whichever estimate is larger.
            var length = s1.Length + s2.Length + s3.Length;
            var byDistance = 0f;
            if (length > 1e-4f)
            {
                var ratio = (pd - pa).Length / length;                 // 1 straight, about 0.35 for a fist
                var straight = finger == Finger.Thumb ? 0.97f : 0.95f;
                var closed = finger == Finger.Thumb ? 0.6f : 0.45f;
                byDistance = SmoothingMath.Clamp01((straight - ratio) / (straight - closed));
            }
            return Math.Max(byAngle, byDistance);
        }

        public static HandPose ComputeAll(float[] w, float gain)
        {
            return new HandPose
            {
                Tracked = true,
                Thumb = SmoothingMath.Clamp01(Compute(w, Finger.Thumb) * gain),
                Index = SmoothingMath.Clamp01(Compute(w, Finger.Index) * gain),
                Middle = SmoothingMath.Clamp01(Compute(w, Finger.Middle) * gain),
                Ring = SmoothingMath.Clamp01(Compute(w, Finger.Ring) * gain),
                Little = SmoothingMath.Clamp01(Compute(w, Finger.Little) * gain),
            };
        }
    }

    /// <summary>
    /// Smooths finger curl per hand and eases fingers to the rest curl when a hand is lost. Mirror mode assigns the
    /// user's left hand to the avatar's right hand (same side of the screen, like the arms); copy mode keeps sides.
    /// </summary>
    public sealed class FingerCurlSolver
    {
        private readonly HandTrackingSettings _settings;
        private HandPose _left, _right;
        private double _leftSeen = double.NegativeInfinity, _rightSeen = double.NegativeInfinity;
        private HandPose _leftTarget, _rightTarget;
        private bool _initialized;

        public HandsPose Pose;

        public FingerCurlSolver(HandTrackingSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Reset();
        }

        /// <summary>Feeds the latest hand frame (user sides). Call whenever a new frame arrives.</summary>
        public void Submit(in TrackingFrame frame, double now, bool mirrorUser)
        {
            var s = _settings;
            s.Clamp();
            var userLeft = Accept(frame.LeftHand, s);
            var userRight = Accept(frame.RightHand, s);
            // Mirror: the user's left hand sits on screen-left, which is the avatar's right hand.
            var avatarLeft = mirrorUser ? userRight : userLeft;
            var avatarRight = mirrorUser ? userLeft : userRight;
            if (avatarLeft.HasValue) { _leftTarget = avatarLeft.Value; _leftSeen = now; }
            if (avatarRight.HasValue) { _rightTarget = avatarRight.Value; _rightSeen = now; }
        }

        private static HandPose? Accept(HandTracking? hand, HandTrackingSettings s)
        {
            if (!hand.HasValue || hand.Value.LandmarksXyz == null || hand.Value.Confidence < s.MinConfidence) return null;
            return FingerCurl.ComputeAll(hand.Value.LandmarksXyz, s.CurlGain);
        }

        /// <summary>Steps the smoothing; <paramref name="enabled"/> false eases both hands to rest.</summary>
        public HandsPose Update(float dt, double now, bool enabled)
        {
            var s = _settings;
            var rest = HandPose.Uniform(s.RestCurl);
            var leftTracked = enabled && now - _leftSeen <= s.LostTimeoutSeconds;
            var rightTracked = enabled && now - _rightSeen <= s.LostTimeoutSeconds;
            var leftTarget = leftTracked ? _leftTarget : rest;
            var rightTarget = rightTracked ? _rightTarget : rest;

            var tau = SmoothingMath.Tau(leftTracked || rightTracked ? s.Smoothing : 0.8f, s.MaxSmoothingSeconds);
            var alpha = tau <= 0f || dt <= 0f ? 1f : 1f - (float)Math.Exp(-dt / tau);
            if (!_initialized) { alpha = 1f; _initialized = true; }

            _left = Blend(_left, leftTarget, alpha, leftTracked);
            _right = Blend(_right, rightTarget, alpha, rightTracked);
            Pose.Left = _left;
            Pose.Right = _right;
            return Pose;
        }

        private static HandPose Blend(HandPose cur, HandPose target, float a, bool tracked)
        {
            return new HandPose
            {
                Tracked = tracked,
                Thumb = cur.Thumb + (target.Thumb - cur.Thumb) * a,
                Index = cur.Index + (target.Index - cur.Index) * a,
                Middle = cur.Middle + (target.Middle - cur.Middle) * a,
                Ring = cur.Ring + (target.Ring - cur.Ring) * a,
                Little = cur.Little + (target.Little - cur.Little) * a,
            };
        }

        public void Reset()
        {
            _initialized = false;
            _leftSeen = _rightSeen = double.NegativeInfinity;
            _left = _right = _leftTarget = _rightTarget = HandPose.Uniform(_settings.RestCurl);
            Pose = new HandsPose { Left = _left, Right = _right };
        }
    }
}
