namespace VRMCast.Core.Camera
{
    /// <summary>Avatar camera framing presets (PRD 16.1). Bust is the product default (D-007).</summary>
    public enum FramingPreset
    {
        Face = 0,
        Bust = 1,
        HalfBody = 2,
        FullBody = 3,
    }

    public static class FramingPresetExtensions
    {
        public const FramingPreset Default = FramingPreset.Bust;

        public static string Label(this FramingPreset preset)
        {
            switch (preset)
            {
                case FramingPreset.Face: return "Face";
                case FramingPreset.Bust: return "Bust";
                case FramingPreset.HalfBody: return "Half Body";
                case FramingPreset.FullBody: return "Full Body";
                default: return preset.ToString();
            }
        }

        public static bool TryParseLabel(string label, out FramingPreset preset)
        {
            foreach (FramingPreset p in System.Enum.GetValues(typeof(FramingPreset)))
            {
                if (p.Label() == label) { preset = p; return true; }
            }
            preset = Default;
            return false;
        }
    }
}
