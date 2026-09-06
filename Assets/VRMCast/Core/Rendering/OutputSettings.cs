using System;
using System.Collections.Generic;

namespace VRMCast.Core.Rendering
{
    /// <summary>
    /// Broadcast output resolution and frame rate. Immutable value type; the product default is 1920x1080 @ 30 (D-005).
    /// </summary>
    public readonly struct OutputSettings : IEquatable<OutputSettings>
    {
        public const int DefaultWidth = 1920;
        public const int DefaultHeight = 1080;
        public const int DefaultFps = 30;
        public const int MinDimension = 16;
        public const int MaxDimension = 8192;
        public const int MinFps = 1;
        public const int MaxFps = 240;

        public int Width { get; }
        public int Height { get; }
        public int Fps { get; }

        public OutputSettings(int width, int height, int fps)
        {
            if (width < MinDimension || width > MaxDimension) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < MinDimension || height > MaxDimension) throw new ArgumentOutOfRangeException(nameof(height));
            if (fps < MinFps || fps > MaxFps) throw new ArgumentOutOfRangeException(nameof(fps));
            Width = width;
            Height = height;
            Fps = fps;
        }

        public static OutputSettings Default => new OutputSettings(DefaultWidth, DefaultHeight, DefaultFps);

        public float Aspect => (float)Width / Height;

        public string Label => $"{Width}×{Height} | {Fps} FPS";

        /// <summary>Presets from PRD 18.2. The recommended entry is the product default.</summary>
        public static IReadOnlyList<OutputPreset> Presets { get; } = new[]
        {
            new OutputPreset("720p 30", new OutputSettings(1280, 720, 30)),
            new OutputPreset("720p 60", new OutputSettings(1280, 720, 60)),
            new OutputPreset("1080p 30 — Recommended", Default),
            new OutputPreset("1080p 60", new OutputSettings(1920, 1080, 60)),
            new OutputPreset("1440p 30", new OutputSettings(2560, 1440, 30)),
            new OutputPreset("1440p 60", new OutputSettings(2560, 1440, 60)),
            new OutputPreset("4K 30", new OutputSettings(3840, 2160, 30)),
        };

        public static bool TryFindPreset(string label, out OutputSettings settings)
        {
            foreach (var p in Presets)
            {
                if (p.Label == label) { settings = p.Settings; return true; }
            }
            settings = Default;
            return false;
        }

        public bool Equals(OutputSettings other) => Width == other.Width && Height == other.Height && Fps == other.Fps;
        public override bool Equals(object obj) => obj is OutputSettings o && Equals(o);
        public override int GetHashCode() => unchecked((Width * 397 ^ Height) * 397 ^ Fps);
        public static bool operator ==(OutputSettings a, OutputSettings b) => a.Equals(b);
        public static bool operator !=(OutputSettings a, OutputSettings b) => !a.Equals(b);
        public override string ToString() => Label;
    }

    public readonly struct OutputPreset
    {
        public string Label { get; }
        public OutputSettings Settings { get; }

        public OutputPreset(string label, OutputSettings settings)
        {
            Label = label;
            Settings = settings;
        }
    }
}
