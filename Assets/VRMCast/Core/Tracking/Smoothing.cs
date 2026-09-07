using System;

namespace VRMCast.Core.Tracking
{
    /// <summary>Frame-rate independent exponential smoother: value moves toward the target with time constant tau.</summary>
    public struct ExponentialSmoother
    {
        public float Value;
        private bool _initialized;

        public void Reset(float value = 0f)
        {
            Value = value;
            _initialized = true;
        }

        /// <param name="tauSeconds">Time constant; 0 snaps immediately.</param>
        public float Update(float target, float dt, float tauSeconds)
        {
            if (!_initialized || tauSeconds <= 0f || dt <= 0f)
            {
                Value = target;
                _initialized = true;
                return Value;
            }
            var alpha = 1f - (float)Math.Exp(-dt / tauSeconds);
            Value += (target - Value) * alpha;
            return Value;
        }
    }

    public static class SmoothingMath
    {
        /// <summary>Maps a 0..1 smoothing amount to a time constant in seconds (0 = raw).</summary>
        public static float Tau(float amount, float maxSeconds) => Math.Max(0f, Math.Min(1f, amount)) * maxSeconds;

        /// <summary>Removes a dead zone around zero while keeping the response continuous.</summary>
        public static float DeadZone(float value, float zone)
        {
            if (zone <= 0f) return value;
            var magnitude = Math.Abs(value);
            if (magnitude <= zone) return 0f;
            return Math.Sign(value) * (magnitude - zone);
        }

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
    }
}
