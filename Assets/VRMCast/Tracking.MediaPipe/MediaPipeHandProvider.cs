// Compiled only when com.github.homuler.mediapipe (>= 0.16.0) is installed; see Scripts/setup-mediapipe.sh.
#if VRMCAST_MEDIAPIPE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mediapipe;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using VRMCast.Core.Tracking;
using Debug = UnityEngine.Debug;

namespace VRMCast.Tracking.MediaPipe
{
    /// <summary>
    /// MediaPipe Hand Landmarker (CPU, LIVE_STREAM, up to two hands) at its own cadence. World landmarks, the raw
    /// handedness label and the wrist / palm image positions are copied out on the worker thread; which real hand
    /// each detection is gets decided later against the pose wrists (<see cref="HandFrameBuilder.ResolveSides"/>).
    /// </summary>
    public sealed class MediaPipeHandProvider : IUnityHandTrackingProvider
    {
        public const string ProviderName = "MediaPipe Hand Landmarker";
        private const int PoolSize = 3;

        private readonly HandProviderContext _ctx;
        private readonly LatestFrameBuffer<TrackingFrame> _latest = new LatestFrameBuffer<TrackingFrame>();
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly object _submitGate = new object();
        private readonly Dictionary<long, double> _submitTimes = new Dictionary<long, double>();

        private HandLandmarker _landmarker;
        private TextureFramePool _pool;
        private RenderTexture _scaled;
        private int _poolWidth, _poolHeight;
        private long _lastTimestampMs = -1;
        private double _lastSubmitSeconds = -1;

        public MediaPipeHandProvider(HandProviderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public string Name => ProviderName;
        public bool IsAvailable => _ctx.HandLandmarkerModel != null;
        public bool IsRunning { get; private set; }
        public string UnavailableReasonKey { get; private set; }

        public void Start()
        {
            if (IsRunning) return;
            if (_ctx.HandLandmarkerModel == null)
            {
                UnavailableReasonKey = "tracking.noModel";
                return;
            }

            var options = new HandLandmarkerOptions(
                baseOptions: new global::Mediapipe.Tasks.Core.BaseOptions(
                    global::Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
                    modelAssetBuffer: _ctx.HandLandmarkerModel.bytes),
                runningMode: global::Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                numHands: 2,
                minHandDetectionConfidence: 0.5f,
                minHandPresenceConfidence: 0.5f,
                minTrackingConfidence: 0.5f,
                resultCallback: OnResult);

            _landmarker = HandLandmarker.CreateFromOptions(options);
            _clock.Restart();
            _lastTimestampMs = -1;
            IsRunning = true;
            UnavailableReasonKey = null;
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;
            try { _landmarker?.Close(); }
            catch (Exception e) { Debug.LogException(e); }
            _landmarker = null;
            _pool?.Dispose();
            _pool = null;
            ReleaseScaled();
            lock (_submitGate) _submitTimes.Clear();
            _latest.Clear();
        }

        public bool TryGetLatest(out TrackingFrame frame) => _latest.TryRead(out frame);

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

            Graphics.Blit(tex, _scaled);
            textureFrame.ReadTextureOnCPU(_scaled, flipHorizontally: false, flipVertically: MediaPipePoseProvider.FlipVertically);
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
            ReleaseScaled();
            _scaled = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "HandTrackingInput" };
            _scaled.Create();
            _poolWidth = width;
            _poolHeight = height;
        }

        private void ReleaseScaled()
        {
            if (_scaled == null) return;
            _scaled.Release();
            UnityEngine.Object.Destroy(_scaled);
            _scaled = null;
        }

        private void OnResult(HandLandmarkerResult result, Image image, long timestampMs)
        {
            var now = _clock.Elapsed.TotalSeconds;
            double submitted;
            lock (_submitGate)
            {
                if (_submitTimes.TryGetValue(timestampMs, out submitted)) _submitTimes.Remove(timestampMs);
                else submitted = now;
            }

            var seconds = timestampMs / 1000.0;
            HandTracking? first = null, second = null;
            var count = result.handWorldLandmarks != null ? result.handWorldLandmarks.Count : 0;
            for (var h = 0; h < count && h < 2; h++)
            {
                var landmarks = result.handWorldLandmarks[h].landmarks;
                if (landmarks == null || landmarks.Count < HandFrameBuilder.LandmarkCount) continue;

                var labelLeft = true;
                var score = 1f;
                if (result.handedness != null && h < result.handedness.Count && result.handedness[h].categories != null
                    && result.handedness[h].categories.Count > 0)
                {
                    var top = result.handedness[h].categories[0];
                    labelLeft = string.Equals(top.categoryName, "Left", StringComparison.OrdinalIgnoreCase);
                    score = top.score;
                }

                var world = new float[HandFrameBuilder.LandmarkCount * 3];
                for (var i = 0; i < HandFrameBuilder.LandmarkCount; i++)
                {
                    var l = landmarks[i];
                    world[i * 3] = l.x;
                    world[i * 3 + 1] = l.y;
                    world[i * 3 + 2] = l.z;
                }
                var hand = new HandTracking { Confidence = score, LandmarksXyz = world, LabelLeft = labelLeft };
                if (result.handLandmarks != null && h < result.handLandmarks.Count && result.handLandmarks[h].landmarks != null
                    && result.handLandmarks[h].landmarks.Count >= HandFrameBuilder.LandmarkCount)
                {
                    var imageLandmarks = result.handLandmarks[h].landmarks;
                    hand.WristU = imageLandmarks[HandFrameBuilder.Wrist].x; hand.WristV = imageLandmarks[HandFrameBuilder.Wrist].y;
                    hand.MiddleMcpU = imageLandmarks[HandFrameBuilder.MiddleMcp].x; hand.MiddleMcpV = imageLandmarks[HandFrameBuilder.MiddleMcp].y;
                    hand.IndexMcpU = imageLandmarks[HandFrameBuilder.IndexMcp].x; hand.IndexMcpV = imageLandmarks[HandFrameBuilder.IndexMcp].y;
                    hand.LittleMcpU = imageLandmarks[HandFrameBuilder.LittleMcp].x; hand.LittleMcpV = imageLandmarks[HandFrameBuilder.LittleMcp].y;
                }
                if (first == null) first = hand; else second = hand;
            }

            var frame = HandFrameBuilder.Build(seconds, first, second);
            _latest.Publish(frame);
            _ctx.Stats.OnResult(now, Math.Max(0, now - submitted), first != null);
        }

        public void Dispose() => Stop();
    }

    internal static class MediaPipeHandProviderRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            HandTrackingProviderRegistry.Register(MediaPipeHandProvider.ProviderName, ctx => new MediaPipeHandProvider(ctx));
        }
    }
}
#endif
