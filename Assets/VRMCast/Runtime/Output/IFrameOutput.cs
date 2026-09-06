using UnityEngine;
using VRMCast.Core.Output;

namespace VRMCast.Output
{
    /// <summary>
    /// A consumer of the final composited frame (PRD 19). The renderer never knows who reads it; outputs are
    /// registered with the <see cref="OutputService"/> and receive every rendered frame while started.
    /// </summary>
    public interface IFrameOutput
    {
        string Name { get; }
        bool IsAvailable { get; }
        bool IsRunning { get; }

        /// <summary>True when this transport actually preserves alpha end-to-end (D-010). Most do not.</summary>
        bool SupportsAlpha { get; }

        void Start(OutputConfiguration config);
        void SubmitFrame(RenderTexture texture, double timestamp);
        void Stop();
    }
}
