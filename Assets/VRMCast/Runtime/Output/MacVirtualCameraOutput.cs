using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using VRMCast.Core.Output;

namespace VRMCast.Output
{
    /// <summary>
    /// Publishes the OutputRenderTexture to the "VRM Live Camera" Core Media I/O extension (PRD 20). Frames are read
    /// back asynchronously from the GPU as BGRA and handed to the native bridge, which wraps them in IOSurface-backed
    /// pixel buffers on the extension's sink stream. The extension streams a fixed 1920×1080 at 30 fps, so other
    /// output sizes are scaled into a 1080p texture first and faster outputs are throttled to 30 frames per second.
    /// At most two readbacks are in flight so a stalled extension never backs up the renderer (bounded latency).
    /// </summary>
    public sealed class MacVirtualCameraOutput : IFrameOutput
    {
        public const string DeviceName = "VRM Live Camera";
        public const string SinkStreamName = "VRM Live Camera Sink";
        public const int Width = 1920;
        public const int Height = 1080;
        public const double FrameInterval = 1.0 / 30.0;
        private const int MaxInFlight = 2;

        private RenderTexture _scaled;
        private int _inFlight;
        private double _lastSent = -1;
        private OutputConfiguration _config;

        public string Name => "VRM Live Camera";
        public bool IsAvailable => FrameBridgeNative.IsAvailable;
        public bool IsRunning { get; private set; }
        /// <summary>No capture app consumes alpha from a camera device (D-010).</summary>
        public bool SupportsAlpha => false;

        public long FramesSubmitted { get; private set; }
        public long FramesSent => FrameBridgeNative.FramesSent;
        public long FramesDropped => FrameBridgeNative.FramesDropped + _readbackFailures;
        public string LastError { get; private set; } = "";
        private long _readbackFailures;

        /// <summary>Raised on the main thread when the bridge could not open the device (extension missing / not approved).</summary>
        public event Action<string> OpenFailed;

        public void Start(OutputConfiguration config)
        {
            _config = config;
            FramesSubmitted = 0;
            _readbackFailures = 0;
            _lastSent = -1;
            LastError = "";
            var result = FrameBridgeNative.Open(DeviceName, SinkStreamName);
            if (result != FrameBridgeNative.VcamOk)
            {
                LastError = FrameBridgeNative.LastError();
                if (string.IsNullOrEmpty(LastError)) LastError = $"open failed ({result})";
                Debug.LogWarning($"VRMCast virtual camera: {LastError}");
                OpenFailed?.Invoke(LastError);
                IsRunning = false;
                return;
            }
            IsRunning = true;
        }

        public void SubmitFrame(RenderTexture texture, double timestamp)
        {
            if (!IsRunning || texture == null) return;
            if (_lastSent >= 0 && timestamp - _lastSent < FrameInterval * 0.98) return;
            if (_inFlight >= MaxInFlight) { _readbackFailures++; return; }
            _lastSent = timestamp;

            var source = texture;
            if (texture.width != Width || texture.height != Height)
            {
                if (_scaled == null || !_scaled.IsCreated())
                {
                    _scaled = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32) { name = "VirtualCameraFrame" };
                    _scaled.Create();
                }
                Graphics.Blit(texture, _scaled);
                source = _scaled;
            }

            _inFlight++;
            FramesSubmitted++;
            var stamp = timestamp;
            AsyncGPUReadback.Request(source, 0, TextureFormat.BGRA32, request => OnReadback(request, stamp));
        }

        private void OnReadback(AsyncGPUReadbackRequest request, double timestamp)
        {
            _inFlight = Math.Max(0, _inFlight - 1);
            if (!IsRunning) return;
            if (request.hasError)
            {
                _readbackFailures++;
                return;
            }
            var data = request.GetData<byte>();
            if (!data.IsCreated || data.Length < request.width * request.height * 4)
            {
                _readbackFailures++;
                return;
            }
            int result;
            unsafe
            {
                var ptr = (IntPtr)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data);
                // GPU readbacks come out bottom-up; the camera wants the first row at the top.
                result = FrameBridgeNative.Send(ptr, request.width, request.height, request.layerDataSize / request.height, flipVertically: true, timestampSeconds: 0);
            }
            if (result != FrameBridgeNative.VcamOk && result != 6 /* queue full: the extension drains at 30 fps */)
            {
                LastError = FrameBridgeNative.LastError();
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            // The extension shows its own fallback pattern once frames stop (PRD 37.6).
            FrameBridgeNative.Close();
            if (_scaled != null)
            {
                _scaled.Release();
                UnityEngine.Object.Destroy(_scaled);
                _scaled = null;
            }
        }
    }
}
