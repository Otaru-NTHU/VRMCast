using UnityEngine;
using VRMCast.Core.Output;

namespace VRMCast.Output
{
    /// <summary>
    /// The in-app preview. The UI binds the OutputRenderTexture directly, so this output only tracks frame
    /// cadence; it exists so the preview is a peer of every other consumer rather than a special case.
    /// </summary>
    public sealed class PreviewOutput : IFrameOutput
    {
        public string Name => "Preview";
        public bool IsAvailable => true;
        public bool IsRunning { get; private set; }
        public bool SupportsAlpha => true;
        public long FramesReceived { get; private set; }
        public double LastTimestamp { get; private set; }

        public void Start(OutputConfiguration config)
        {
            IsRunning = true;
            FramesReceived = 0;
        }

        public void SubmitFrame(RenderTexture texture, double timestamp)
        {
            if (!IsRunning) return;
            FramesReceived++;
            LastTimestamp = timestamp;
        }

        public void Stop() => IsRunning = false;
    }
}
