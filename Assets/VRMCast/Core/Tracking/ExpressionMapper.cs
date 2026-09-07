using System;
using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// Evaluates a mapping table against raw blendshapes (PRD 10.1): many sources may feed one destination (max
    /// wins), one source may feed many destinations, and every row smooths independently. Output names are
    /// canonical VRM expressions; nothing here knows about MediaPipe or UniVRM objects.
    /// </summary>
    public sealed class ExpressionMapper
    {
        private readonly List<ExpressionMapping> _mappings = new List<ExpressionMapping>();
        private readonly List<ExponentialSmoother> _smoothers = new List<ExponentialSmoother>();
        private readonly Dictionary<string, float> _output = new Dictionary<string, float>(StringComparer.Ordinal);

        public float MaxSmoothingSeconds { get; set; } = 0.25f;

        public IReadOnlyList<ExpressionMapping> Mappings => _mappings;

        /// <summary>Latest evaluated weights by destination name.</summary>
        public IReadOnlyDictionary<string, float> Output => _output;

        public ExpressionMapper(IEnumerable<ExpressionMapping> mappings = null)
        {
            if (mappings != null) SetMappings(mappings);
        }

        public void SetMappings(IEnumerable<ExpressionMapping> mappings)
        {
            _mappings.Clear();
            _smoothers.Clear();
            foreach (var m in mappings)
            {
                if (m == null || string.IsNullOrEmpty(m.Source) || string.IsNullOrEmpty(m.Destination)) continue;
                _mappings.Add(m);
                _smoothers.Add(NewSmoother());
            }
            _output.Clear();
        }

        /// <summary>
        /// Evaluates one frame. With <paramref name="blendshapes"/> null every destination decays toward 0.
        /// </summary>
        public IReadOnlyDictionary<string, float> Evaluate(IReadOnlyDictionary<string, float> blendshapes, float dt, float smoothingScale = 1f)
        {
            foreach (var key in new List<string>(_output.Keys)) _output[key] = 0f;

            for (var i = 0; i < _mappings.Count; i++)
            {
                var m = _mappings[i];
                var target = 0f;
                if (m.Enabled && blendshapes != null && blendshapes.TryGetValue(m.Source, out var raw))
                {
                    target = m.Shape(raw);
                }

                var smoother = _smoothers[i];
                var value = smoother.Update(target, dt, SmoothingMath.Tau(m.Smoothing * smoothingScale, MaxSmoothingSeconds));
                _smoothers[i] = smoother;

                if (!m.Enabled) continue;
                if (_output.TryGetValue(m.Destination, out var existing)) _output[m.Destination] = Math.Max(existing, value);
                else _output[m.Destination] = value;
            }
            return _output;
        }

        public void Reset()
        {
            for (var i = 0; i < _smoothers.Count; i++) _smoothers[i] = NewSmoother();
            _output.Clear();
        }

        /// <summary>Expressions start closed and ease in; a fresh smoother would otherwise snap to the first value.</summary>
        private static ExponentialSmoother NewSmoother()
        {
            var s = new ExponentialSmoother();
            s.Reset(0f);
            return s;
        }
    }
}
