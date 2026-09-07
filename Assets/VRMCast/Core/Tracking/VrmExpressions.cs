using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// Version-neutral VRM expression names used as mapping destinations. They are the VRM 1.0 preset names;
    /// VRM 0.x avatars translate them to BlendShapePreset names through <see cref="ToVrm0Preset"/>.
    /// </summary>
    public static class VrmExpressions
    {
        public const string Happy = "happy";
        public const string Angry = "angry";
        public const string Sad = "sad";
        public const string Relaxed = "relaxed";
        public const string Surprised = "surprised";
        public const string Aa = "aa";
        public const string Ih = "ih";
        public const string Ou = "ou";
        public const string Ee = "ee";
        public const string Oh = "oh";
        public const string Blink = "blink";
        public const string BlinkLeft = "blinkLeft";
        public const string BlinkRight = "blinkRight";
        public const string LookUp = "lookUp";
        public const string LookDown = "lookDown";
        public const string LookLeft = "lookLeft";
        public const string LookRight = "lookRight";
        public const string Neutral = "neutral";

        public static readonly IReadOnlyList<string> Presets = new[]
        {
            Happy, Angry, Sad, Relaxed, Surprised, Aa, Ih, Ou, Ee, Oh, Blink, BlinkLeft, BlinkRight,
            LookUp, LookDown, LookLeft, LookRight, Neutral,
        };

        private static readonly Dictionary<string, string> Vrm0Names = new Dictionary<string, string>
        {
            [Happy] = "Joy", [Angry] = "Angry", [Sad] = "Sorrow", [Relaxed] = "Fun",
            [Aa] = "A", [Ih] = "I", [Ou] = "U", [Ee] = "E", [Oh] = "O",
            [Blink] = "Blink", [BlinkLeft] = "Blink_L", [BlinkRight] = "Blink_R",
            [LookUp] = "LookUp", [LookDown] = "LookDown", [LookLeft] = "LookLeft", [LookRight] = "LookRight",
            [Neutral] = "Neutral",
        };

        /// <summary>VRM 0.x BlendShapePreset name for a canonical name, or null when 0.x has no such preset (e.g. surprised).</summary>
        public static string ToVrm0Preset(string canonical)
        {
            return canonical != null && Vrm0Names.TryGetValue(canonical, out var name) ? name : null;
        }

        /// <summary>Swaps the left/right presets, used when mirroring the user.</summary>
        public static string Mirror(string canonical)
        {
            switch (canonical)
            {
                case BlinkLeft: return BlinkRight;
                case BlinkRight: return BlinkLeft;
                case LookLeft: return LookRight;
                case LookRight: return LookLeft;
                default: return canonical;
            }
        }
    }
}
