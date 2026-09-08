using System;
using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>Body tracking modes (PRD 14). Full body is reserved for a later milestone.</summary>
    public enum BodyTrackingMode
    {
        Off = 0,
        UpperBody = 1,
        /// <summary>Torso plus upper and lower arms from the pose landmarks (hands stay neutral).</summary>
        UpperBodyArms = 2,
        /// <summary>Arms plus finger curl from the hand landmarks.</summary>
        UpperBodyArmsFingers = 3,
    }

    public sealed class BodyTrackingSettings
    {
        public BodyTrackingMode Mode { get; set; } = BodyTrackingMode.UpperBodyArms;
        public float Smoothing { get; set; } = 0.6f;

        /// <summary>Arm smoothing (0..1) and the landmark visibility below which an arm returns to rest.</summary>
        public float ArmSmoothing { get; set; } = 0.45f;
        public float ArmMinVisibility { get; set; } = 0.55f;

        /// <summary>Degrees the upper arms hang below the T-pose when not tracked.</summary>
        public float ArmRestAngleDeg { get; set; } = 70f;

        public bool ArmsEnabled => Mode == BodyTrackingMode.UpperBodyArms || Mode == BodyTrackingMode.UpperBodyArmsFingers;

        /// <summary>Use the hand landmarker's wrist as the arm target (two-bone IK) when a hand is seen; off = pose landmarks only.</summary>
        public bool ArmsFromHands { get; set; } = true;
        /// <summary>Rotate the hand bones from the palm orientation. Off keeps the wrists at rest.</summary>
        public bool WristFromPalm { get; set; }
        public bool HandsEnabled => Mode == BodyTrackingMode.UpperBodyArmsFingers;
        public float Gain { get; set; } = 1f;
        public float DeadZoneDeg { get; set; } = 1f;
        public float MaxRollDeg { get; set; } = 25f;
        public float MaxYawDeg { get; set; } = 35f;
        public float MaxLeanDeg { get; set; } = 20f;
        public float LostTimeoutSeconds { get; set; } = 0.5f;
        public float ReturnToNeutralSeconds { get; set; } = 1.0f;

        /// <summary>Per-axis sign switches for cameras or models whose torso conventions disagree with the defaults.</summary>
        public bool InvertRoll { get; set; }
        public bool InvertYaw { get; set; }
        public bool InvertPitch { get; set; }
        public float MaxSmoothingSeconds { get; set; } = 0.35f;

        /// <summary>Neutral torso angles captured by Calibrate (radians, user frame).</summary>
        public float NeutralRollRad { get; set; }
        public float NeutralYawRad { get; set; }
        public float NeutralPitchRad { get; set; }

        public void Clamp()
        {
            Smoothing = Math.Min(Math.Max(Smoothing, 0f), 1f);
            Gain = Math.Min(Math.Max(Gain, 0f), 3f);
            ArmSmoothing = Math.Min(Math.Max(ArmSmoothing, 0f), 1f);
            ArmMinVisibility = Math.Min(Math.Max(ArmMinVisibility, 0f), 1f);
        }
    }

    /// <summary>Torso rotation in the avatar's frame (mirroring applied), radians.</summary>
    public struct BodyPose
    {
        public float RollRad;
        public float YawRad;
        public float PitchRad;
        public bool HasBody;
    }

    /// <summary>
    /// Builds the pose block of a <see cref="TrackingFrame"/> from MediaPipe Pose Landmarker output and derives torso
    /// angles. Landmark indices follow MediaPipe: 0 nose, 11/12 left/right shoulder, 23/24 left/right hip.
    /// World landmarks are meters with the origin between the hips, x right in the image, y down, z away from the camera.
    /// </summary>
    public static class PoseFrameBuilder
    {
        /// <summary>
        /// Default for <see cref="Build"/>'s swap flag. The stand-alone Pose Landmarker labels image sides on the
        /// unmirrored feed (validated: its "left" is the user's real right), so they are swapped into real sides;
        /// the Holistic Landmarker's labels are already real sides and its provider passes false explicitly.
        /// </summary>
        public static volatile bool SwapLeftRight = true;

        public const int Nose = 0;
        public const int LeftShoulder = 11;
        public const int RightShoulder = 12;
        public const int LeftElbow = 13;
        public const int RightElbow = 14;
        public const int LeftWrist = 15;
        public const int RightWrist = 16;
        public const int LeftHip = 23;
        public const int RightHip = 24;
        public const int LandmarkCount = 33;

        /// <param name="world">33 × (x, y, z) world landmarks, meters.</param>
        /// <param name="visibility">33 visibilities (0..1) or null.</param>
        /// <param name="normalized">33 × (u, v, z) image-normalized landmarks or null.</param>
        /// <param name="imageAspect">Image width / height (used with <paramref name="normalized"/>).</param>
        public static TrackingFrame Build(double timestamp, float[] world, float[] visibility, float[] normalized = null, float imageAspect = 1f)
            => Build(timestamp, world, visibility, normalized, imageAspect, SwapLeftRight);

        /// <param name="swapSides">True when the model's Left/Right labels are image sides and must become the user's real sides.</param>
        public static TrackingFrame Build(double timestamp, float[] world, float[] visibility, float[] normalized, float imageAspect, bool swapSides)
        {
            var frame = TrackingFrame.Empty(timestamp);
            if (world == null || world.Length < LandmarkCount * 3)
            {
                frame.PoseConfidence = 0f;
                return frame;
            }

            // Landmark indices for the user's REAL left and right: swapped when the model labels image sides.
            var swap = swapSides;
            int lSh = swap ? RightShoulder : LeftShoulder, rSh = swap ? LeftShoulder : RightShoulder;
            int lHip = swap ? RightHip : LeftHip, rHip = swap ? LeftHip : RightHip;
            int lEl = swap ? RightElbow : LeftElbow, rEl = swap ? LeftElbow : RightElbow;
            int lWr = swap ? RightWrist : LeftWrist, rWr = swap ? LeftWrist : RightWrist;

            var pose = new PoseTracking
            {
                LeftShoulderX = world[lSh * 3], LeftShoulderY = world[lSh * 3 + 1], LeftShoulderZ = world[lSh * 3 + 2],
                RightShoulderX = world[rSh * 3], RightShoulderY = world[rSh * 3 + 1], RightShoulderZ = world[rSh * 3 + 2],
                LeftHipX = world[lHip * 3], LeftHipY = world[lHip * 3 + 1], LeftHipZ = world[lHip * 3 + 2],
                RightHipX = world[rHip * 3], RightHipY = world[rHip * 3 + 1], RightHipZ = world[rHip * 3 + 2],
                NoseX = world[Nose * 3], NoseY = world[Nose * 3 + 1], NoseZ = world[Nose * 3 + 2],
                LeftElbowX = world[lEl * 3], LeftElbowY = world[lEl * 3 + 1], LeftElbowZ = world[lEl * 3 + 2],
                RightElbowX = world[rEl * 3], RightElbowY = world[rEl * 3 + 1], RightElbowZ = world[rEl * 3 + 2],
                LeftWristX = world[lWr * 3], LeftWristY = world[lWr * 3 + 1], LeftWristZ = world[lWr * 3 + 2],
                RightWristX = world[rWr * 3], RightWristY = world[rWr * 3 + 1], RightWristZ = world[rWr * 3 + 2],
                LeftElbowVisibility = 1f, RightElbowVisibility = 1f, LeftWristVisibility = 1f, RightWristVisibility = 1f,
            };
            var confidence = 1f;
            if (visibility != null && visibility.Length >= LandmarkCount)
            {
                confidence = Math.Min(visibility[LeftShoulder], visibility[RightShoulder]);
                pose.LeftElbowVisibility = visibility[lEl];
                pose.RightElbowVisibility = visibility[rEl];
                pose.LeftWristVisibility = visibility[lWr];
                pose.RightWristVisibility = visibility[rWr];
            }
            pose.Confidence = confidence;
            if (normalized != null && normalized.Length >= LandmarkCount * 3)
            {
                pose.HasImageCoords = true;
                pose.ImageAspect = imageAspect > 0f ? imageAspect : 1f;
                pose.LeftShoulderU = normalized[lSh * 3]; pose.LeftShoulderV = normalized[lSh * 3 + 1];
                pose.RightShoulderU = normalized[rSh * 3]; pose.RightShoulderV = normalized[rSh * 3 + 1];
                pose.LeftElbowU = normalized[lEl * 3]; pose.LeftElbowV = normalized[lEl * 3 + 1];
                pose.RightElbowU = normalized[rEl * 3]; pose.RightElbowV = normalized[rEl * 3 + 1];
                pose.LeftWristU = normalized[lWr * 3]; pose.LeftWristV = normalized[lWr * 3 + 1];
                pose.RightWristU = normalized[rWr * 3]; pose.RightWristV = normalized[rWr * 3 + 1];
            }
            frame.Pose = pose;
            frame.PoseConfidence = confidence;
            return frame;
        }

        public static TrackingFrame NoBody(double timestamp)
        {
            var frame = TrackingFrame.Empty(timestamp);
            frame.PoseConfidence = 0f;
            return frame;
        }

        /// <summary>
        /// Torso angles in the user's frame: roll positive leans toward the user's left, yaw positive turns toward the
        /// user's left, pitch positive leans forward (toward the camera).
        /// </summary>
        public static void ComputeAngles(in PoseTracking p, out float rollRad, out float yawRad, out float pitchRad)
        {
            var dx = p.LeftShoulderX - p.RightShoulderX;   // > 0: the user's left shoulder is on the image's right
            var dy = p.LeftShoulderY - p.RightShoulderY;   // y down: > 0 when the left shoulder is lower
            var dz = p.LeftShoulderZ - p.RightShoulderZ;   // > 0 when the left shoulder is farther from the camera
            var width = Math.Max(Math.Abs(dx), 0.05f);

            rollRad = (float)Math.Atan2(dy, width);
            yawRad = (float)Math.Atan2(dz, width);

            var shoulderCx = (p.LeftShoulderX + p.RightShoulderX) * 0.5f;
            var shoulderCy = (p.LeftShoulderY + p.RightShoulderY) * 0.5f;
            var shoulderCz = (p.LeftShoulderZ + p.RightShoulderZ) * 0.5f;
            var hipCx = (p.LeftHipX + p.RightHipX) * 0.5f;
            var hipCy = (p.LeftHipY + p.RightHipY) * 0.5f;
            var hipCz = (p.LeftHipZ + p.RightHipZ) * 0.5f;
            var upY = hipCy - shoulderCy;                  // torso length (positive: shoulders above hips)
            var forwardZ = hipCz - shoulderCz;             // positive when shoulders are closer to the camera
            pitchRad = (float)Math.Atan2(forwardZ, Math.Max(upY, 0.05f));
            _ = shoulderCx; _ = hipCx;

            if (float.IsNaN(rollRad)) rollRad = 0f;
            if (float.IsNaN(yawRad)) yawRad = 0f;
            if (float.IsNaN(pitchRad)) pitchRad = 0f;
        }
    }

    /// <summary>
    /// Smooths and clamps torso angles (PRD 14) with the same conventions as the head solver: mirror flips yaw and
    /// roll, tracking loss glides back to neutral. Calibration is a median over a short window.
    /// </summary>
    public sealed class BodyPoseSolver
    {
        private ExponentialSmoother _roll, _yaw, _pitch;
        private TrackingFrame _latest;
        private bool _hasLatest;
        private double _lastBodyTime = double.NegativeInfinity;
        private readonly List<float> _calRoll = new List<float>(), _calYaw = new List<float>(), _calPitch = new List<float>();
        private double _calStart = -1;
        private float _calWindow;

        public BodyTrackingSettings Settings { get; }
        public BodyPose Pose;
        public bool IsTracking { get; private set; }
        public bool IsCalibrating => _calStart >= 0;

        public BodyPoseSolver(BodyTrackingSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Reset();
        }

        public void Submit(in TrackingFrame frame)
        {
            _latest = frame;
            _hasLatest = true;
            if (frame.Pose != null && frame.PoseConfidence > 0.5f)
            {
                _lastBodyTime = frame.Timestamp;
                if (_calStart >= 0)
                {
                    PoseFrameBuilder.ComputeAngles(frame.Pose.Value, out var r, out var y, out var p);
                    _calRoll.Add(r); _calYaw.Add(y); _calPitch.Add(p);
                }
            }
        }

        public void StartCalibration(double now, float windowSeconds = 0.75f)
        {
            _calStart = now;
            _calWindow = windowSeconds;
            _calRoll.Clear(); _calYaw.Clear(); _calPitch.Clear();
        }

        /// <summary>Returns true when the calibration window ended on this update (neutral stored if enough samples).</summary>
        public bool CalibrationSucceeded { get; private set; }

        public BodyPose Update(float dt, double now, bool mirrorUser)
        {
            var s = Settings;
            s.Clamp();

            if (_calStart >= 0 && now - _calStart >= _calWindow)
            {
                _calStart = -1;
                CalibrationSucceeded = _calRoll.Count >= 6;
                if (CalibrationSucceeded)
                {
                    s.NeutralRollRad = Median(_calRoll);
                    s.NeutralYawRad = Median(_calYaw);
                    s.NeutralPitchRad = Median(_calPitch);
                }
                CalibrationEnded?.Invoke(CalibrationSucceeded);
            }

            var bodyRecent = s.Mode != BodyTrackingMode.Off && _hasLatest && _latest.Pose != null
                             && _latest.PoseConfidence > 0.5f && now - _lastBodyTime <= s.LostTimeoutSeconds;
            IsTracking = bodyRecent;

            float tr, ty, tp, tau;
            if (bodyRecent)
            {
                PoseFrameBuilder.ComputeAngles(_latest.Pose.Value, out var r, out var y, out var p);
                tr = Shape(r - s.NeutralRollRad, s.MaxRollDeg);
                ty = Shape(y - s.NeutralYawRad, s.MaxYawDeg);
                tp = Shape(p - s.NeutralPitchRad, s.MaxLeanDeg);
                tau = SmoothingMath.Tau(s.Smoothing, s.MaxSmoothingSeconds);
            }
            else
            {
                tr = ty = tp = 0f;
                tau = Math.Max(0.05f, s.ReturnToNeutralSeconds / 3f);
            }

            var roll = _roll.Update(tr, dt, tau);
            var yaw = _yaw.Update(ty, dt, tau);
            var pitch = _pitch.Update(tp, dt, tau);

            // Signs validated on device with the real-side labels: mirror mode leans and turns the avatar toward the
            // same side of the screen as the user; copy mode (mirror off) flips both.
            var mirrorSign = mirrorUser ? 1f : -1f;
            Pose.RollRad = roll * mirrorSign * (s.InvertRoll ? -1f : 1f);
            Pose.YawRad = -yaw * mirrorSign * (s.InvertYaw ? -1f : 1f);
            Pose.PitchRad = pitch * (s.InvertPitch ? -1f : 1f);
            Pose.HasBody = bodyRecent;
            return Pose;
        }

        public event Action<bool> CalibrationEnded;

        private float Shape(float rad, float maxDeg)
        {
            var s = Settings;
            var deg = SmoothingMath.DeadZone(rad * HeadPoseMath.Rad2Deg, s.DeadZoneDeg) * s.Gain;
            return SmoothingMath.Clamp(deg, -maxDeg, maxDeg) * HeadPoseMath.Deg2Rad;
        }

        private static float Median(List<float> values)
        {
            if (values.Count == 0) return 0f;
            var copy = new List<float>(values);
            copy.Sort();
            var n = copy.Count;
            return n % 2 == 1 ? copy[n / 2] : (copy[n / 2 - 1] + copy[n / 2]) * 0.5f;
        }

        public void Reset()
        {
            _roll.Reset(0f); _yaw.Reset(0f); _pitch.Reset(0f);
            _hasLatest = false;
            _lastBodyTime = double.NegativeInfinity;
            _calStart = -1;
            Pose = default;
            IsTracking = false;
        }
    }
}
