// This assembly only compiles when com.github.homuler.mediapipe (>= 0.16.0) is installed; see Scripts/setup-mediapipe.sh.
#if VRMCAST_MEDIAPIPE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mediapipe;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using VRMCast.Core.Tracking;
using Debug = UnityEngine.Debug;

namespace VRMCast.Tracking.MediaPipe
{
    /// <summary>
    /// MediaPipe Face Landmarker (Tasks API, CPU delegate, LIVE_STREAM) behind the provider-neutral contract.
    /// The main thread downsamples the webcam frame into a small texture and submits it; MediaPipe calls back on a
    /// worker thread where the result is converted into a <see cref="TrackingFrame"/> and published through a
    /// <see cref="LatestFrameBuffer{T}"/>. No Unity object is touched off the main thread (PRD 28).
    /// </summary>
    public sealed class MediaPipeFaceProvider : IUnityFaceTrackingProvider
    {
        public const string ProviderName = "MediaPipe Face Landmarker";
        private const int PoolSize = 4;
        private const int FlipProbeResults = 24;

        private readonly FaceProviderContext _ctx;
        private readonly LatestFrameBuffer<TrackingFrame> _latest = new LatestFrameBuffer<TrackingFrame>();
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly object _submitGate = new object();
        private readonly Dictionary<long, double> _submitTimes = new Dictionary<long, double>();

        private FaceLandmarker _landmarker;
        private TextureFramePool _pool;
        private int _poolWidth, _poolHeight;
        private long _lastTimestampMs = -1;
        private double _lastSubmitSeconds = -1;
        private bool _flipVertically = true;
        private int _probeResults;
        private int _probeFaces;
        private bool _probeDone;

        public MediaPipeFaceProvider(FaceProviderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public string Name => ProviderName;
        public bool IsAvailable => _ctx.FaceLandmarkerModel != null;
        public bool IsRunning { get; private set; }
        public string UnavailableReasonKey { get; private set; }

        public void Start()
        {
            if (IsRunning) return;
            if (_ctx.FaceLandmarkerModel == null)
            {
                UnavailableReasonKey = "tracking.noModel";
                return;
            }

            var options = new FaceLandmarkerOptions(
                baseOptions: new global::Mediapipe.Tasks.Core.BaseOptions(
                    global::Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
                    modelAssetBuffer: _ctx.FaceLandmarkerModel.bytes),
                runningMode: global::Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                numFaces: 1,
                minFaceDetectionConfidence: 0.5f,
                minFacePresenceConfidence: 0.5f,
                minTrackingConfidence: 0.5f,
                outputFaceBlendshapes: true,
                outputFaceTransformationMatrixes: true,
                resultCallback: OnResult);

            _landmarker = FaceLandmarker.CreateFromOptions(options);
            _clock.Restart();
            _lastTimestampMs = -1;
            _probeResults = 0;
            _probeFaces = 0;
            _probeDone = false;
            IsRunning = true;
            UnavailableReasonKey = null;
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            try
            {
                _landmarker?.Close();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            _landmarker = null;
            _pool?.Dispose();
            _pool = null;
            lock (_submitGate) _submitTimes.Clear();
            _latest.Clear();
        }

        public bool TryGetLatest(out TrackingFrame frame) => _latest.TryRead(out frame);

        /// <summary>Main thread: pushes one downscaled camera frame per tracking interval.</summary>
        public void Tick()
        {
            if (!IsRunning || _landmarker == null) return;
            var tex = _ctx.Camera.Texture;
            if (tex == null || !_ctx.Camera.HasFreshFrame) return;

            var now = _clock.Elapsed.TotalSeconds;
            var interval = 1.0 / Math.Max(1, _ctx.TargetFps);
            if (_lastSubmitSeconds >= 0 && now - _lastSubmitSeconds < interval * 0.9) return;

            EnsurePool(tex.width, tex.height);
            if (!_pool.TryGetTextureFrame(out var textureFrame)) return;

            // Tracking resolution is independent of the camera and the 1080p output (PRD 2, D-006): the blit inside
            // ReadTextureOnCPU downsamples to the pool size.
            textureFrame.ReadTextureOnCPU(tex, flipHorizontally: false, flipVertically: _flipVertically);
            var image = textureFrame.BuildCPUImage();
            textureFrame.Release();

            var timestampMs = (long)(now * 1000.0);
            if (timestampMs <= _lastTimestampMs) timestampMs = _lastTimestampMs + 1;
            _lastTimestampMs = timestampMs;
            _lastSubmitSeconds = now;

            lock (_submitGate)
            {
                _submitTimes[timestampMs] = now;
                if (_submitTimes.Count > 120) _submitTimes.Clear();
            }

            try
            {
                // DetectAsync takes ownership of the image (move semantics); do not dispose it here.
                _landmarker.DetectAsync(image, timestampMs);
                _ctx.Stats.OnSubmitted();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void EnsurePool(int sourceWidth, int sourceHeight)
        {
            var width = Math.Min(_ctx.MaxInputWidth, sourceWidth);
            var height = Math.Max(16, (int)Math.Round(width * (double)sourceHeight / sourceWidth));
            width &= ~1;
            height &= ~1;
            if (_pool != null && _poolWidth == width && _poolHeight == height) return;
            _pool?.Dispose();
            _pool = new TextureFramePool(width, height, TextureFormat.RGBA32, PoolSize);
            _poolWidth = width;
            _poolHeight = height;
        }

        /// <summary>Worker thread: converts the result and publishes it. Never touches Unity objects.</summary>
        private void OnResult(FaceLandmarkerResult result, Image image, long timestampMs)
        {
            var now = _clock.Elapsed.TotalSeconds;
            double submitted;
            lock (_submitGate)
            {
                if (_submitTimes.TryGetValue(timestampMs, out submitted)) _submitTimes.Remove(timestampMs);
                else submitted = now;
            }

            var hasFace = result.faceLandmarks != null && result.faceLandmarks.Count > 0;
            var timestampSeconds = timestampMs / 1000.0;
            TrackingFrame frame;
            if (!hasFace)
            {
                frame = FaceFrameBuilder.NoFace(timestampSeconds);
            }
            else
            {
                var shapes = new Dictionary<string, float>(64, StringComparer.Ordinal);
                if (result.faceBlendshapes != null && result.faceBlendshapes.Count > 0 && result.faceBlendshapes[0].categories != null)
                {
                    foreach (var c in result.faceBlendshapes[0].categories)
                    {
                        if (!string.IsNullOrEmpty(c.categoryName)) shapes[c.categoryName] = c.score;
                    }
                }

                float[] forward = null, up = null, position = null;
                if (result.facialTransformationMatrixes != null && result.facialTransformationMatrixes.Count > 0)
                {
                    var m = result.facialTransformationMatrixes[0];
                    var f = m.GetColumn(2);
                    var u = m.GetColumn(1);
                    var p = m.GetColumn(3);
                    forward = new[] { f.x, f.y, f.z };
                    up = new[] { u.x, u.y, u.z };
                    position = new[] { p.x * 0.01f, p.y * 0.01f, p.z * 0.01f }; // MediaPipe reports centimeters
                }
                frame = FaceFrameBuilder.Build(timestampSeconds, shapes, forward, up, position);
            }

            _latest.Publish(frame);
            _ctx.Stats.OnResult(now, Math.Max(0, now - submitted), hasFace);
            ProbeOrientation(hasFace);
        }

        /// <summary>
        /// The texture origin differs between Unity and MediaPipe; if the first results contain no face at all the
        /// image was probably upside down, so the vertical flip is toggled once. Read on the worker, applied on the
        /// next main-thread Tick (bool writes are atomic).
        /// </summary>
        private void ProbeOrientation(bool hasFace)
        {
            if (_probeDone) return;
            _probeResults++;
            if (hasFace) _probeFaces++;
            if (_probeFaces > 0)
            {
                _probeDone = true;
                MediaPipePoseProvider.FlipVertically = _flipVertically;
                return;
            }
            if (_probeResults >= FlipProbeResults)
            {
                _flipVertically = !_flipVertically;
                MediaPipePoseProvider.FlipVertically = _flipVertically;
                _probeResults = 0;
                Debug.Log($"VRMCast tracking: no face in the first frames, trying flipVertically={_flipVertically}.");
            }
        }

        public void Dispose() => Stop();
    }

    internal static class MediaPipeFaceProviderRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            FaceTrackingProviderRegistry.Register(MediaPipeFaceProvider.ProviderName, ctx => new MediaPipeFaceProvider(ctx));
        }
    }
}
#endif
