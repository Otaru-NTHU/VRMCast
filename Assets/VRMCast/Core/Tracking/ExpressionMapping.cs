using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>One row of the Expression Mapping Editor (PRD 10): source blendshape → avatar expression.</summary>
    public sealed class ExpressionMapping
    {
        public string Source { get; set; }
        public string Destination { get; set; }
        public float Gain { get; set; } = 1f;
        public float Threshold { get; set; }
        public float Min { get; set; }
        public float Max { get; set; } = 1f;
        public float Smoothing { get; set; } = 0.35f;
        public bool Invert { get; set; }
        public bool Enabled { get; set; } = true;

        public ExpressionMapping() { }

        public ExpressionMapping(string source, string destination, float gain = 1f, float threshold = 0f, float smoothing = 0.35f)
        {
            Source = source;
            Destination = destination;
            Gain = gain;
            Threshold = threshold;
            Smoothing = smoothing;
        }

        public ExpressionMapping Clone() => (ExpressionMapping)MemberwiseClone();

        /// <summary>Applies invert, threshold, gain and range to a raw 0..1 coefficient (no smoothing).</summary>
        public float Shape(float raw)
        {
            var v = SmoothingMath.Clamp01(raw);
            if (Invert) v = 1f - v;
            if (Threshold > 0f)
            {
                // Re-normalize above the threshold so full input still reaches 1.
                v = Threshold >= 1f ? 0f : (v - Threshold) / (1f - Threshold);
            }
            v = SmoothingMath.Clamp01(v * Gain);
            return SmoothingMath.Clamp(v, Min, Max);
        }
    }

    /// <summary>Default mapping tables for the Basic and Advanced modes (PRD 9). Data, not code, so profiles can override them.</summary>
    public static class ExpressionMappingDefaults
    {
        public static List<ExpressionMapping> Basic() => new List<ExpressionMapping>
        {
            new ExpressionMapping(MediaPipeBlendshapes.EyeBlinkLeft, VrmExpressions.BlinkLeft, gain: 1.15f, threshold: 0.1f, smoothing: 0.12f),
            new ExpressionMapping(MediaPipeBlendshapes.EyeBlinkRight, VrmExpressions.BlinkRight, gain: 1.15f, threshold: 0.1f, smoothing: 0.12f),
            new ExpressionMapping(MediaPipeBlendshapes.JawOpen, VrmExpressions.Aa, gain: 1.3f, threshold: 0.06f, smoothing: 0.25f),
            new ExpressionMapping(MediaPipeBlendshapes.MouthSmileLeft, VrmExpressions.Happy, gain: 1.0f, threshold: 0.25f, smoothing: 0.4f),
            new ExpressionMapping(MediaPipeBlendshapes.MouthSmileRight, VrmExpressions.Happy, gain: 1.0f, threshold: 0.25f, smoothing: 0.4f),
        };

        public static List<ExpressionMapping> Advanced()
        {
            var list = Basic();
            list.AddRange(new[]
            {
                new ExpressionMapping(MediaPipeBlendshapes.MouthPucker, VrmExpressions.Ou, gain: 1.0f, threshold: 0.3f, smoothing: 0.3f),
                new ExpressionMapping(MediaPipeBlendshapes.MouthFunnel, VrmExpressions.Oh, gain: 1.0f, threshold: 0.25f, smoothing: 0.3f),
                new ExpressionMapping(MediaPipeBlendshapes.BrowDownLeft, VrmExpressions.Angry, gain: 0.9f, threshold: 0.35f, smoothing: 0.45f),
                new ExpressionMapping(MediaPipeBlendshapes.BrowDownRight, VrmExpressions.Angry, gain: 0.9f, threshold: 0.35f, smoothing: 0.45f),
                new ExpressionMapping(MediaPipeBlendshapes.MouthFrownLeft, VrmExpressions.Sad, gain: 0.9f, threshold: 0.3f, smoothing: 0.45f),
                new ExpressionMapping(MediaPipeBlendshapes.MouthFrownRight, VrmExpressions.Sad, gain: 0.9f, threshold: 0.3f, smoothing: 0.45f),
                new ExpressionMapping(MediaPipeBlendshapes.EyeWideLeft, VrmExpressions.Surprised, gain: 0.8f, threshold: 0.4f, smoothing: 0.4f),
                new ExpressionMapping(MediaPipeBlendshapes.EyeWideRight, VrmExpressions.Surprised, gain: 0.8f, threshold: 0.4f, smoothing: 0.4f),
                new ExpressionMapping(MediaPipeBlendshapes.BrowInnerUp, VrmExpressions.Surprised, gain: 0.6f, threshold: 0.45f, smoothing: 0.4f),
            });
            return list;
        }

        public static List<ExpressionMapping> For(FaceTrackingMode mode) => mode == FaceTrackingMode.Advanced ? Advanced() : Basic();
    }
}
