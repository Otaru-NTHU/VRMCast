namespace VRMCast.Core.Backgrounds
{
    /// <summary>Background compositor modes required for MVP-A (PRD 17). Chroma is the default (D-009).</summary>
    public enum BackgroundMode
    {
        SolidColor = 0,
        Image = 1,
        ChromaKey = 2,
        Transparent = 3,
    }

    public enum ImageFitMode
    {
        Fill = 0,
        Fit = 1,
        Stretch = 2,
    }

    public static class BackgroundModeExtensions
    {
        public const BackgroundMode Default = BackgroundMode.ChromaKey;

        public static string Label(this BackgroundMode mode)
        {
            switch (mode)
            {
                case BackgroundMode.SolidColor: return "Solid Color";
                case BackgroundMode.Image: return "Image";
                case BackgroundMode.ChromaKey: return "Chroma Key";
                case BackgroundMode.Transparent: return "Transparent";
                default: return mode.ToString();
            }
        }

        public static bool TryParseLabel(string label, out BackgroundMode mode)
        {
            foreach (BackgroundMode m in System.Enum.GetValues(typeof(BackgroundMode)))
            {
                if (m.Label() == label) { mode = m; return true; }
            }
            mode = Default;
            return false;
        }

        public static string Label(this ImageFitMode mode) => mode.ToString();
    }
}
