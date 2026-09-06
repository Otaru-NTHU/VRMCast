using VRMCast.Core.Rendering;

namespace VRMCast.Core.Output
{
    /// <summary>
    /// Everything an <c>IFrameOutput</c> needs to know when it starts (PRD 19). Alpha is advisory: an output
    /// reports whether it can actually preserve it (D-010).
    /// </summary>
    public sealed class OutputConfiguration
    {
        public OutputSettings Settings { get; }
        public bool WantsAlpha { get; }

        public OutputConfiguration(OutputSettings settings, bool wantsAlpha)
        {
            Settings = settings;
            WantsAlpha = wantsAlpha;
        }
    }
}
