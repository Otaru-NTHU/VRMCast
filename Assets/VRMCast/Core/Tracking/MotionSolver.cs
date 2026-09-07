using System;
using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// Turns tracking frames into an <see cref="AvatarPose"/> (PRD 11): calibration offsets, dead zones, gain,
    /// smoothing, mirroring, confidence gating and a soft return to neutral when tracking is lost. It runs on the
    /// main thread at render rate and interpolates whatever the tracker last produced, so tracking cadence never
    /// dictates render cadence (PRD 3.4).
    /// </summary>
    public sealed class MotionSolver
    {
        private readonly ExpressionMapper _mapper;
        private readonly AvatarPose _pose = new AvatarPose();
        private ExponentialSmoother _pitch, _yaw, _roll, _lookX, _lookY;
        private TrackingFrame _latest;
        private bool _hasLatest;
        private double _lastFaceTime = double.NegativeInfinity;
        private double _clock;

        public FaceTrackingSettings Settings { get; }
        public AvatarPose Pose => _pose;

        /// <summary>True while the last frame within the timeout contained a face.</summary>
        public bool IsTracking { get; private set; }

        public MotionSolver(FaceTrackingSettings settings, ExpressionMapper mapper = null)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _mapper = mapper ?? new ExpressionMapper(ExpressionMappingDefaults.For(settings.Mode));
            ResetSmoothers();
        }

        /// <summary>Head and eyes ease in from neutral instead of snapping to the first tracked value.</summary>
        private void ResetSmoothers()
        {
            _pitch.Reset(0f); _yaw.Reset(0f); _roll.Reset(0f); _lookX.Reset(0f); _lookY.Reset(0f);
        }

        public ExpressionMapper Mapper => _mapper;

        /// <summary>Replaces the mapping table (for example when switching Basic/Advanced).</summary>
        public void SetMappings(IEnumerable<ExpressionMapping> mappings) => _mapper.SetMappings(mappings);

        /// <summary>Feeds the newest tracking frame (any thread may produce it; call this from the consumer thread).</summary>
        public void Submit(in TrackingFrame frame)
        {
            _latest = frame;
            _hasLatest = true;
            if (frame.FaceConfidence > 0f) _lastFaceTime = frame.Timestamp;
        }

        /// <summary>Advances the solver by one render frame and returns the pose to apply.</summary>
        public AvatarPose Update(float dt, double now)
        {
            _clock = now;
            var s = Settings;
            s.Clamp();

            var faceRecent = _hasLatest && _latest.FaceConfidence > 0f && now - _lastFaceTime <= s.LostTimeoutSeconds;
            IsTracking = faceRecent;

            float targetPitch, targetYaw, targetRoll, targetLookX, targetLookY;
            float headTau, lookTau, expressionScale;
            IReadOnlyDictionary<string, float> shapes;

            if (faceRecent)
            {
                var c = s.Calibration ?? CalibrationData.Identity;
                targetPitch = ShapeAngle(_latest.Head.PitchRad - c.PitchRad, s.InvertPitch);
                targetYaw = ShapeAngle(_latest.Head.YawRad - c.YawRad, s.InvertYaw);
                targetRoll = ShapeAngle(_latest.Head.RollRad - c.RollRad, s.InvertRoll);
                targetLookX = SmoothingMath.Clamp((_latest.Eyes.LookX - c.LookX) * s.LookGain, -1f, 1f);
                targetLookY = SmoothingMath.Clamp((_latest.Eyes.LookY - c.LookY) * s.LookGain, -1f, 1f);
                headTau = SmoothingMath.Tau(s.HeadSmoothing, s.MaxSmoothingSeconds);
                lookTau = SmoothingMath.Tau(s.LookSmoothing, s.MaxSmoothingSeconds);
                expressionScale = 1f;
                shapes = CalibratedShapes(_latest.Blendshapes, c);
            }
            else
            {
                // Lost: glide back to neutral instead of snapping (PRD 37.3).
                targetPitch = targetYaw = targetRoll = 0f;
                targetLookX = targetLookY = 0f;
                headTau = lookTau = Math.Max(0.05f, s.ReturnToNeutralSeconds / 3f);
                expressionScale = Math.Max(1f, s.ReturnToNeutralSeconds / s.MaxSmoothingSeconds);
                shapes = null;
            }

            var pitch = _pitch.Update(targetPitch, dt, headTau);
            var yaw = _yaw.Update(targetYaw, dt, headTau);
            var roll = _roll.Update(targetRoll, dt, headTau);
            var lookX = _lookX.Update(targetLookX, dt, lookTau);
            var lookY = _lookY.Update(targetLookY, dt, lookTau);

            // User frame -> avatar frame. Yaw/roll/lookX flip when not mirroring; pitch and lookY never do.
            var mirrorSign = s.MirrorUser ? 1f : -1f;
            _pose.HeadPitchRad = pitch;
            _pose.HeadYawRad = yaw * mirrorSign;
            _pose.HeadRollRad = -roll * mirrorSign;
            _pose.LookYawDeg = -lookX * mirrorSign * s.LookYawRangeDeg;
            _pose.LookPitchDeg = lookY * s.LookPitchRangeDeg;
            _pose.HeadRatio = s.HeadRatio;
            _pose.NeckRatio = s.NeckRatio;
            _pose.ChestRatio = s.ChestRatio;
            _pose.SpineRatio = s.SpineRatio;
            _pose.HasFace = faceRecent;
            _pose.Confidence = faceRecent ? _latest.FaceConfidence : 0f;

            var weights = _mapper.Evaluate(shapes, dt, expressionScale);
            _pose.Expressions.Clear();
            foreach (var kv in weights)
            {
                var name = s.MirrorUser ? VrmExpressions.Mirror(kv.Key) : kv.Key;
                _pose.Expressions[name] = kv.Value;
            }
            return _pose;
        }

        private float ShapeAngle(float rad, bool invert)
        {
            var s = Settings;
            var deg = rad * HeadPoseMath.Rad2Deg;
            deg = SmoothingMath.DeadZone(deg, s.HeadDeadZoneDeg) * s.HeadGain;
            deg = SmoothingMath.Clamp(deg, -FaceTrackingSettings.MaxHeadAngleDeg, FaceTrackingSettings.MaxHeadAngleDeg);
            if (invert) deg = -deg;
            return deg * HeadPoseMath.Deg2Rad;
        }

        private static IReadOnlyDictionary<string, float> CalibratedShapes(Dictionary<string, float> raw, CalibrationData c)
        {
            if (raw == null || !c.IsCalibrated || (c.MouthOpen <= 0f && c.Smile <= 0f)) return raw;
            // Only the mouth baseline is subtracted: blinks and brows already rest at ~0.
            var copy = new Dictionary<string, float>(raw, StringComparer.Ordinal);
            Subtract(copy, MediaPipeBlendshapes.JawOpen, c.MouthOpen);
            Subtract(copy, MediaPipeBlendshapes.MouthSmileLeft, c.Smile);
            Subtract(copy, MediaPipeBlendshapes.MouthSmileRight, c.Smile);
            return copy;
        }

        private static void Subtract(Dictionary<string, float> shapes, string key, float baseline)
        {
            if (baseline <= 0f || !shapes.TryGetValue(key, out var v)) return;
            // Re-normalize so a fully open mouth still reaches 1 after removing the resting value.
            shapes[key] = SmoothingMath.Clamp01((v - baseline) / Math.Max(1f - baseline, 0.05f));
        }

        public void Reset()
        {
            ResetSmoothers();
            _mapper.Reset();
            _pose.Clear();
            _hasLatest = false;
            _lastFaceTime = double.NegativeInfinity;
            IsTracking = false;
        }
    }
}
