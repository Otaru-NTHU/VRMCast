using System.Threading;
using VRMCast.Core.Diagnostics;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// Thread-safe tracking counters for diagnostics (PRD 31): frames submitted, results received, inference time,
    /// dropped (submitted but never answered) frames. Producers call from any thread; the UI reads on the main thread.
    /// </summary>
    public sealed class TrackingStats
    {
        private readonly FpsCounter _resultFps = new FpsCounter(1.0);
        private long _submitted;
        private long _results;
        private long _facesSeen;
        private long _inferenceMicrosTotal;
        private long _inferenceSamples;
        private double _lastResultTime;
        private readonly object _gate = new object();

        public long Submitted => Interlocked.Read(ref _submitted);
        public long Results => Interlocked.Read(ref _results);
        public long FacesSeen => Interlocked.Read(ref _facesSeen);
        public long Dropped => System.Math.Max(0, Submitted - Results);

        /// <summary>Average inference latency (submit → result) over the recent samples, milliseconds.</summary>
        public double InferenceMs
        {
            get
            {
                lock (_gate)
                {
                    return _inferenceSamples == 0 ? 0 : _inferenceMicrosTotal / 1000.0 / _inferenceSamples;
                }
            }
        }

        public double ResultFps
        {
            get { lock (_gate) return _resultFps.Fps; }
        }

        public void OnSubmitted() => Interlocked.Increment(ref _submitted);

        /// <param name="resultTimeSeconds">Wall-clock time the result arrived.</param>
        /// <param name="latencySeconds">Time between submission and the result.</param>
        public void OnResult(double resultTimeSeconds, double latencySeconds, bool hasFace)
        {
            Interlocked.Increment(ref _results);
            if (hasFace) Interlocked.Increment(ref _facesSeen);
            lock (_gate)
            {
                if (_lastResultTime > 0) _resultFps.AddFrame(resultTimeSeconds - _lastResultTime);
                _lastResultTime = resultTimeSeconds;

                // Keep a rolling window of 60 samples.
                if (_inferenceSamples >= 60)
                {
                    _inferenceMicrosTotal -= _inferenceMicrosTotal / _inferenceSamples;
                    _inferenceSamples--;
                }
                _inferenceMicrosTotal += (long)(latencySeconds * 1_000_000);
                _inferenceSamples++;
            }
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _submitted, 0);
            Interlocked.Exchange(ref _results, 0);
            Interlocked.Exchange(ref _facesSeen, 0);
            lock (_gate)
            {
                _inferenceMicrosTotal = 0;
                _inferenceSamples = 0;
                _lastResultTime = 0;
                _resultFps.Reset();
            }
        }
    }
}
