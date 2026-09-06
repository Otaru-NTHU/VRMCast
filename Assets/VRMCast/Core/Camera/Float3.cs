using System;

namespace VRMCast.Core.Camera
{
    /// <summary>
    /// Tiny engine-agnostic vector so the framing math can be unit tested without UnityEngine.
    /// The runtime converts to/from UnityEngine.Vector3 at the boundary.
    /// </summary>
    public readonly struct Float3 : IEquatable<Float3>
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public Float3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static Float3 Zero => new Float3(0, 0, 0);
        public static Float3 Up => new Float3(0, 1, 0);

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        public Float3 Normalized
        {
            get
            {
                var len = Length;
                return len > 1e-6f ? this / len : Zero;
            }
        }

        public static Float3 operator +(Float3 a, Float3 b) => new Float3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Float3 operator -(Float3 a, Float3 b) => new Float3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Float3 operator *(Float3 a, float s) => new Float3(a.X * s, a.Y * s, a.Z * s);
        public static Float3 operator /(Float3 a, float s) => new Float3(a.X / s, a.Y / s, a.Z / s);

        public static float Dot(Float3 a, Float3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Float3 Cross(Float3 a, Float3 b) =>
            new Float3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static float Distance(Float3 a, Float3 b) => (a - b).Length;

        public bool Equals(Float3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Float3 o && Equals(o);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }
}
