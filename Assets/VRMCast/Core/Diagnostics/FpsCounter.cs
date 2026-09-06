using System;

namespace VRMCast.Core.Diagnostics
{
    /// <summary>
    /// Windowed frame-rate meter. Feed it every frame's delta time; it publishes a new average once per
    /// window so the UI reads a stable number instead of per-frame noise.
    /// </summary>
    public sealed class FpsCounter
    {
        private readonly double _windowSeconds;
        private double _accumulated;
        private int _frames;

        public FpsCounter(double windowSeconds = 0.5)
        {
            if (windowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            _windowSeconds = windowSeconds;
        }

        /// <summary>Average frames per second over the last completed window. 0 until the first window completes.</summary>
        public double Fps { get; private set; }

        /// <summary>Average frame time in milliseconds over the last completed window.</summary>
        public double FrameTimeMs { get; private set; }

        /// <summary>Total frames counted since construction or <see cref="Reset"/>.</summary>
        public long TotalFrames { get; private set; }

        /// <summary>Returns true when a new window average was published on this call.</summary>
        public bool AddFrame(double deltaSeconds)
        {
            if (deltaSeconds < 0 || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds)) return false;
            _accumulated += deltaSeconds;
            _frames++;
            TotalFrames++;
            if (_accumulated < _windowSeconds) return false;

            Fps = _frames / _accumulated;
            FrameTimeMs = _accumulated / _frames * 1000.0;
            _accumulated = 0;
            _frames = 0;
            return true;
        }

        public void Reset()
        {
            _accumulated = 0;
            _frames = 0;
            Fps = 0;
            FrameTimeMs = 0;
            TotalFrames = 0;
        }
    }
}
