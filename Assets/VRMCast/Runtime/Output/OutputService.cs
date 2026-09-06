using System;
using System.Collections.Generic;
using UnityEngine;
using VRMCast.Core.Output;
using VRMCast.Rendering;

namespace VRMCast.Output
{
    /// <summary>
    /// Frame Output Bus (PRD 3.5). Subscribes to the render service and fans every rendered frame out to the
    /// running outputs. Outputs are registered explicitly by the bootstrap; nothing is discovered globally.
    /// </summary>
    public sealed class OutputService : IDisposable
    {
        private readonly IRenderService _render;
        private readonly List<IFrameOutput> _outputs = new List<IFrameOutput>();
        private Func<bool> _wantsAlpha = () => false;
        private bool _disposed;

        public IReadOnlyList<IFrameOutput> Outputs => _outputs;
        public long FramesDispatched { get; private set; }

        public event Action OutputsChanged;

        public OutputService(IRenderService render)
        {
            _render = render ?? throw new ArgumentNullException(nameof(render));
            _render.FrameRendered += OnFrameRendered;
            _render.SettingsChanged += OnSettingsChanged;
        }

        /// <summary>Lets the bootstrap tell outputs whether the current background wants alpha preserved.</summary>
        public void SetAlphaProvider(Func<bool> wantsAlpha) => _wantsAlpha = wantsAlpha ?? (() => false);

        public void Register(IFrameOutput output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (_outputs.Contains(output)) return;
            _outputs.Add(output);
            OutputsChanged?.Invoke();
        }

        public bool IsRunning(IFrameOutput output) => output != null && output.IsRunning;

        public bool AnyRunning
        {
            get
            {
                foreach (var o in _outputs) if (o.IsRunning) return true;
                return false;
            }
        }

        public OutputConfiguration CurrentConfiguration => new OutputConfiguration(_render.Settings, _wantsAlpha());

        public bool Start(IFrameOutput output)
        {
            if (output == null || !_outputs.Contains(output) || !output.IsAvailable) return false;
            if (output.IsRunning) return true;
            output.Start(CurrentConfiguration);
            OutputsChanged?.Invoke();
            return true;
        }

        public void Stop(IFrameOutput output)
        {
            if (output == null || !output.IsRunning) return;
            output.Stop();
            OutputsChanged?.Invoke();
        }

        public void StopAll()
        {
            foreach (var o in _outputs) if (o.IsRunning) o.Stop();
            OutputsChanged?.Invoke();
        }

        private void OnFrameRendered(RenderTexture texture, double timestamp)
        {
            if (_disposed) return;
            FramesDispatched++;
            foreach (var o in _outputs)
            {
                if (!o.IsRunning) continue;
                try
                {
                    o.SubmitFrame(texture, timestamp);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    o.Stop();
                    OutputsChanged?.Invoke();
                }
            }
        }

        private void OnSettingsChanged(Core.Rendering.OutputSettings settings)
        {
            // Restart running outputs so they renegotiate the new frame size.
            var config = CurrentConfiguration;
            foreach (var o in _outputs)
            {
                if (!o.IsRunning) continue;
                o.Stop();
                o.Start(config);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _render.FrameRendered -= OnFrameRendered;
            _render.SettingsChanged -= OnSettingsChanged;
            StopAll();
        }
    }
}
