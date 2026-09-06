using System;
using System.Globalization;

namespace VRMCast.Core.Backgrounds
{
    /// <summary>Engine-agnostic linear-agnostic color with 0..1 channels. Parsed from and formatted as #RRGGBB / #RRGGBBAA.</summary>
    public readonly struct RgbaColor : IEquatable<RgbaColor>
    {
        public float R { get; }
        public float G { get; }
        public float B { get; }
        public float A { get; }

        public RgbaColor(float r, float g, float b, float a = 1f)
        {
            R = Clamp01(r); G = Clamp01(g); B = Clamp01(b); A = Clamp01(a);
        }

        public static RgbaColor Transparent => new RgbaColor(0, 0, 0, 0);
        public static RgbaColor Black => new RgbaColor(0, 0, 0, 1);
        public static RgbaColor ChromaGreen => new RgbaColor(0, 1, 0, 1);

        public static bool TryParseHex(string text, out RgbaColor color)
        {
            color = Black;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
            if (s.Length != 6 && s.Length != 8) return false;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return false;

            var r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var a = s.Length == 8 ? byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : (byte)255;
            color = new RgbaColor(r / 255f, g / 255f, b / 255f, a / 255f);
            return true;
        }

        public static RgbaColor ParseHex(string text)
        {
            if (!TryParseHex(text, out var c)) throw new FormatException($"'{text}' is not a #RRGGBB or #RRGGBBAA color");
            return c;
        }

        public string ToHex(bool includeAlpha = false)
        {
            var r = (int)Math.Round(R * 255f);
            var g = (int)Math.Round(G * 255f);
            var b = (int)Math.Round(B * 255f);
            var a = (int)Math.Round(A * 255f);
            return includeAlpha ? $"#{r:X2}{g:X2}{b:X2}{a:X2}" : $"#{r:X2}{g:X2}{b:X2}";
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public bool Equals(RgbaColor o) => R.Equals(o.R) && G.Equals(o.G) && B.Equals(o.B) && A.Equals(o.A);
        public override bool Equals(object obj) => obj is RgbaColor o && Equals(o);
        public override int GetHashCode() => unchecked(((R.GetHashCode() * 397 ^ G.GetHashCode()) * 397 ^ B.GetHashCode()) * 397 ^ A.GetHashCode());
        public override string ToString() => ToHex(includeAlpha: true);
    }
}
