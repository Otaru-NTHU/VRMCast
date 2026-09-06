using UnityEngine;
using VRMCast.Core.Output;

namespace VRMCast.Output
{
    /// <summary>
    /// Debug consumer that validates the output path without a transport: it verifies each frame's size against
    /// the configuration and counts mismatches. The virtual camera (MVP-E) will replace it as the "real" output.
    /// </summary>
    public sealed class NullOutput : IFrameOutput
    {
        private OutputConfiguration _config;

        public string Name => "Debug (no transport)";
        public bool IsAvailable => true;
        public bool IsRunning { get; private set; }
        public bool SupportsAlpha => false;
        public long FramesReceived { get; private set; }
        public long SizeMismatches { get; private set; }

        public void Start(OutputConfiguration config)
        {
            _config = config;
            FramesReceived = 0;
            SizeMismatches = 0;
            IsRunning = true;
        }

        public void SubmitFrame(RenderTexture texture, double timestamp)
        {
            if (!IsRunning || texture == null) return;
            FramesReceived++;
            if (_config != null && (texture.width != _config.Settings.Width || texture.height != _config.Settings.Height))
            {
                SizeMismatches++;
            }
        }

        public void Stop() => IsRunning = false;
    }
}
