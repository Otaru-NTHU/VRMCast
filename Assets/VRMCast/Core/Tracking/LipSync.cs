using System;
using System.Collections.Generic;
using VRMCast.Core.Audio;

namespace VRMCast.Core.Tracking
{
    /// <summary>Lip sync sources (PRD 13). Hybrid is the default (D-008).</summary>
    public enum LipSyncMode
    {
        Camera = 0,
        Microphone = 1,
        Hybrid = 2,
    }

    public sealed class LipSyncSettings
    {
        public LipSyncMode Mode { get; set; } = LipSyncMode.Hybrid;

        /// <summary>Hybrid weights (PRD 13.3 initial values).</summary>
        public float CameraWeight { get; set; } = 0.45f;
        public float AudioWeight { get; set; } = 0.55f;

        /// <summary>How much of the camera's mouth opening survives while the microphone hears silence (0 = fully suppressed).</summary>
        public float SilentSuppression { get; set; } = 0.3f;

        /// <summary>Face confidence below which the camera shape is not trusted in Hybrid mode.</summary>
        public float MinFaceConfidence { get; set; } = 0.5f;

        public AudioLevelSettings Audio { get; } = new AudioLevelSettings();

        public void Clamp()
        {
            CameraWeight = Math.Min(Math.Max(CameraWeight, 0f), 1f);
            AudioWeight = Math.Min(Math.Max(AudioWeight, 0f), 1f);
            SilentSuppression = Math.Min(Math.Max(SilentSuppression, 0f), 1f);
            Audio.Clamp();
        }
    }

    /// <summary>
    /// Combines the camera's mouth shape with microphone energy (PRD 13). Operates in place on the canonical
    /// expression dictionary produced by the <see cref="ExpressionMapper"/>: aa/ih/ou/ee/oh are rewritten, everything
    /// else is untouched. Camera decides the vowel shape; the chosen source decides the amplitude.
    /// </summary>
    public static class HybridLipSolver
    {
        private static readonly string[] Vowels = { VrmExpressions.Aa, VrmExpressions.Ih, VrmExpressions.Ou, VrmExpressions.Ee, VrmExpressions.Oh };

        /// <param name="expressions">Mapper output; modified in place.</param>
        /// <param name="faceConfidence">0 when no face.</param>
        /// <param name="audioEnvelope">0..1 speaking energy from <see cref="AudioLevelMeter"/>.</param>
        /// <param name="audioGateOpen">True while the gate hears sound.</param>
        /// <param name="audioAvailable">False when no microphone is running; Hybrid then behaves like Camera.</param>
        public static void Apply(IDictionary<string, float> expressions, LipSyncSettings settings, float faceConfidence,
            float audioEnvelope, bool audioGateOpen, bool audioAvailable)
        {
            if (expressions == null || settings == null) return;
            settings.Clamp();
            var mode = settings.Mode;
            if (mode == LipSyncMode.Hybrid && !audioAvailable) mode = LipSyncMode.Camera;
            if (mode == LipSyncMode.Camera) return;

            var energy = SmoothingMath.Clamp01(audioEnvelope);
            var cameraOpen = 0f;
            var shapeTotal = 0f;
            foreach (var v in Vowels)
            {
                if (!expressions.TryGetValue(v, out var w)) continue;
                cameraOpen = Math.Max(cameraOpen, w);
                shapeTotal += w;
            }

            float amplitude;
            if (mode == LipSyncMode.Microphone)
            {
                amplitude = energy;
            }
            else
            {
                var faceOk = faceConfidence >= settings.MinFaceConfidence;
                amplitude = faceOk
                    ? settings.CameraWeight * cameraOpen + settings.AudioWeight * energy
                    : energy;
                if (!audioGateOpen)
                {
                    // Silent microphone: never let landmark noise open the mouth wide (PRD 13.3).
                    amplitude = Math.Min(amplitude, cameraOpen * settings.SilentSuppression);
                }
            }
            amplitude = SmoothingMath.Clamp01(amplitude);

            if (mode == LipSyncMode.Hybrid && shapeTotal > 0.05f && faceConfidence >= settings.MinFaceConfidence)
            {
                // Keep the camera's vowel proportions, scale to the combined amplitude.
                var scale = amplitude / Math.Max(cameraOpen, 1e-4f);
                foreach (var v in Vowels)
                {
                    if (expressions.TryGetValue(v, out var w)) expressions[v] = SmoothingMath.Clamp01(w * scale);
                }
            }
            else
            {
                foreach (var v in Vowels) if (expressions.ContainsKey(v)) expressions[v] = 0f;
                expressions[VrmExpressions.Aa] = amplitude;
            }
        }
    }
}
