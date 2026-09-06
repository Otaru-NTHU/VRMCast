using System;
using UnityEngine;
using VRMCast.Core.Rendering;

namespace VRMCast.Rendering
{
    /// <summary>
    /// Owns the avatar camera and the dedicated OutputRenderTexture (PRD 18.3). The Game view / UI framebuffer
    /// is never the broadcast source; every consumer reads <see cref="OutputTexture"/>.
    /// </summary>
    public interface IRenderService : IDisposable
    {
        Camera AvatarCamera { get; }
        RenderTexture OutputTexture { get; }
        OutputSettings Settings { get; }

        event Action<OutputSettings> SettingsChanged;

        /// <summary>Raised on the render thread-safe main-thread callback right after the avatar camera finished rendering into <see cref="OutputTexture"/>.</summary>
        event Action<RenderTexture, double> FrameRendered;

        void SetOutputSettings(OutputSettings settings);
        void SetClearColor(Color color);
    }
}
