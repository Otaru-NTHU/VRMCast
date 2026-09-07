using System;
using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>Neutral offsets captured by Calibrate (PRD 12). Subtracted from every tracked frame.</summary>
    public sealed class CalibrationData
    {
        public bool IsCalibrated { get; }
        public float PitchRad { get; }
        public float YawRad { get; }
        public float RollRad { get; }
        public float LookX { get; }
        public float LookY { get; }
        public float MouthOpen { get; }
        public float Smile { get; }

        public CalibrationData(bool isCalibrated, float pitchRad, float yawRad, float rollRad, float lookX, float lookY, float mouthOpen, float smile)
        {
            IsCalibrated = isCalibrated;
            PitchRad = pitchRad;
            YawRad = yawRad;
            RollRad = rollRad;
            LookX = lookX;
            LookY = lookY;
            MouthOpen = mouthOpen;
            Smile = smile;
        }

        public static CalibrationData Identity => new CalibrationData(false, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// Collects a short window of frames and produces robust (median) neutral values (PRD 12). Frames without a
    /// confident face are rejected; the window must contain at least <see cref="MinFrames"/> accepted frames.
    /// </summary>
    public sealed class CalibrationSampler
    {
        public const float DefaultWindowSeconds = 0.75f;
        public const int MinFrames = 8;
        public const float MinConfidence = 0.5f;

        private readonly List<TrackingFrame> _accepted = new List<TrackingFrame>();
        private double _startTimestamp = -1;
        private double _lastTimestamp;

        public float WindowSeconds { get; }
        public bool IsRunning { get; private set; }
        public int AcceptedFrames => _accepted.Count;
        public int RejectedFrames { get; private set; }
        public CalibrationData Result { get; private set; }

        /// <summary>0..1 progress through the sampling window.</summary>
        public float Progress
        {
            get
            {
                if (!IsRunning || _startTimestamp < 0) return Result != null ? 1f : 0f;
                var p = (float)((_lastTimestamp - _startTimestamp) / WindowSeconds);
                return p < 0f ? 0f : (p > 1f ? 1f : p);
            }
        }

        public CalibrationSampler(float windowSeconds = DefaultWindowSeconds)
        {
            WindowSeconds = windowSeconds > 0 ? windowSeconds : DefaultWindowSeconds;
        }

        public void Start()
        {
            _accepted.Clear();
            RejectedFrames = 0;
            _startTimestamp = -1;
            Result = null;
            IsRunning = true;
        }

        public void Cancel()
        {
            IsRunning = false;
            _accepted.Clear();
        }

        /// <summary>Feeds a frame. Returns true when the window completed on this call (Result is then set, or null if too few frames).</summary>
        public bool Add(in TrackingFrame frame)
        {
            if (!IsRunning) return false;
            if (_startTimestamp < 0) _startTimestamp = frame.Timestamp;
            _lastTimestamp = frame.Timestamp;

            if (frame.FaceConfidence >= MinConfidence) _accepted.Add(frame);
            else RejectedFrames++;

            if (frame.Timestamp - _startTimestamp < WindowSeconds) return false;

            IsRunning = false;
            Result = _accepted.Count >= MinFrames ? Compute() : null;
            return true;
        }

        private CalibrationData Compute()
        {
            return new CalibrationData(
                true,
                Median(f => f.Head.PitchRad),
                Median(f => f.Head.YawRad),
                Median(f => f.Head.RollRad),
                Median(f => f.Eyes.LookX),
                Median(f => f.Eyes.LookY),
                Median(f => f.Mouth.Open),
                Median(f => f.Mouth.Smile));
        }

        private float Median(Func<TrackingFrame, float> select)
        {
            var values = new List<float>(_accepted.Count);
            foreach (var f in _accepted) values.Add(select(f));
            values.Sort();
            var n = values.Count;
            if (n == 0) return 0f;
            return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) * 0.5f;
        }
    }
}
