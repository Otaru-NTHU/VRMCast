namespace VRMCast.Core.Backgrounds
{
    /// <summary>
    /// Background configuration (stored per profile). The renderer asks <see cref="ClearColor"/> for the
    /// camera clear color of the current mode; only Image mode adds geometry.
    /// </summary>
    public sealed class BackgroundSettings
    {
        public const string DefaultChromaHex = "#00FF00";
        public const string DefaultSolidHex = "#202020";

        public BackgroundMode Mode { get; set; } = BackgroundModeExtensions.Default;
        public RgbaColor SolidColor { get; set; } = RgbaColor.ParseHex(DefaultSolidHex);
        public RgbaColor ChromaColor { get; set; } = RgbaColor.ParseHex(DefaultChromaHex);
        public string ImagePath { get; set; }
        public ImageFitMode ImageFit { get; set; } = ImageFitMode.Fill;

        /// <summary>Whether the internal render must keep an alpha channel for this mode.</summary>
        public bool RequiresAlpha => Mode == BackgroundMode.Transparent;

        public bool ShowsImage => Mode == BackgroundMode.Image && !string.IsNullOrEmpty(ImagePath);

        /// <summary>Camera clear color for the mode. Image mode clears to opaque black behind letterboxing.</summary>
        public RgbaColor ClearColor
        {
            get
            {
                switch (Mode)
                {
                    case BackgroundMode.SolidColor: return new RgbaColor(SolidColor.R, SolidColor.G, SolidColor.B, 1f);
                    case BackgroundMode.ChromaKey: return new RgbaColor(ChromaColor.R, ChromaColor.G, ChromaColor.B, 1f);
                    case BackgroundMode.Transparent: return RgbaColor.Transparent;
                    case BackgroundMode.Image: return RgbaColor.Black;
                    default: return RgbaColor.Black;
                }
            }
        }

        public BackgroundSettings Clone() => new BackgroundSettings
        {
            Mode = Mode,
            SolidColor = SolidColor,
            ChromaColor = ChromaColor,
            ImagePath = ImagePath,
            ImageFit = ImageFit,
        };
    }
}
