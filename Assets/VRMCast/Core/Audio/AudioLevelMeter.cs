using System;

namespace VRMCast.Core.Audio
{
    /// <summary>Microphone lip-sync tunables (PRD 13.2, 22).</summary>
    public sealed class AudioLevelSettings
    {
        /// <summary>Multiplier on the normalized level (0.2 … 4).</summary>
        public float Sensitivity { get; set; } = 1.0f;

        /// <summary>Levels below this (dBFS) count as silence; hysteresis of <see cref="GateHysteresisDb"/> avoids chatter.</summary>
        public float GateDb { get; set; } = -45f;
        public float GateHysteresisDb { get; set; } = 3f;

        public float AttackSeconds { get; set; } = 0.03f;
        public float ReleaseSeconds { get; set; } = 0.12f;

        /// <summary>dBFS mapped to level 0 and 1 before sensitivity.</summary>
        public float FloorDb { get; set; } = -50f;
        public float CeilingDb { get; set; } = -12f;

        public void Clamp()
        {
            Sensitivity = Math.Min(Math.Max(Sensitivity, 0.2f), 4f);
            GateDb = Math.Min(Math.Max(GateDb, -80f), 0f);
            AttackSeconds = Math.Max(0.001f, AttackSeconds);
            ReleaseSeconds = Math.Max(0.001f, ReleaseSeconds);
            if (CeilingDb <= FloorDb + 1f) CeilingDb = FloorDb + 1f;
        }
    }

    /// <summary>
    /// RMS → dBFS → gated, normalized envelope with attack/release. Feed it blocks of PCM samples (-1..1) in order;
    /// timing comes from the block length and sample rate, so it is independent of frame rate.
    /// </summary>
    public sealed class AudioLevelMeter
    {
        public const float SilenceDb = -100f;

        private bool _gateOpen;

        public AudioLevelSettings Settings { get; }

        /// <summary>Instantaneous block level in dBFS (SilenceDb when empty).</summary>
        public float RawDb { get; private set; } = SilenceDb;

        /// <summary>0..1 speaking energy after gate, sensitivity and attack/release.</summary>
        public float Envelope { get; private set; }

        /// <summary>True while the gate is open (the user is producing sound above the threshold).</summary>
        public bool IsOpen => _gateOpen;

        public AudioLevelMeter(AudioLevelSettings settings = null)
        {
            Settings = settings ?? new AudioLevelSettings();
        }

        /// <summary>Processes one block. Returns the new envelope.</summary>
        public float Process(float[] samples, int count, int sampleRate)
        {
            if (samples == null || count <= 0 || sampleRate <= 0) return Envelope;
            count = Math.Min(count, samples.Length);

            double sum = 0;
            for (var i = 0; i < count; i++) sum += (double)samples[i] * samples[i];
            var rms = Math.Sqrt(sum / count);
            RawDb = rms > 1e-9 ? (float)(20.0 * Math.Log10(rms)) : SilenceDb;

            return Advance(RawDb, (float)count / sampleRate);
        }

        /// <summary>Advances the envelope for a block of the given duration at the given level (used by Process and tests).</summary>
        public float Advance(float levelDb, float blockSeconds)
        {
            var s = Settings;
            s.Clamp();

            // Gate with hysteresis.
            if (_gateOpen) { if (levelDb < s.GateDb - s.GateHysteresisDb) _gateOpen = false; }
            else if (levelDb >= s.GateDb) _gateOpen = true;

            var target = 0f;
            if (_gateOpen)
            {
                var normalized = (levelDb - s.FloorDb) / (s.CeilingDb - s.FloorDb);
                target = Math.Min(1f, Math.Max(0f, normalized * s.Sensitivity));
            }

            var tau = target > Envelope ? s.AttackSeconds : s.ReleaseSeconds;
            var alpha = blockSeconds <= 0f ? 1f : 1f - (float)Math.Exp(-blockSeconds / tau);
            Envelope += (target - Envelope) * alpha;
            if (Envelope < 1e-4f) Envelope = 0f;
            return Envelope;
        }

        public void Reset()
        {
            RawDb = SilenceDb;
            Envelope = 0f;
            _gateOpen = false;
        }
    }
}
