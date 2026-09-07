// Compiled only when com.github.homuler.mediapipe (>= 0.16.0) is installed; see Scripts/setup-mediapipe.sh.
#if VRMCAST_MEDIAPIPE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mediapipe;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using VRMCast.Core.Tracking;
using Debug = UnityEngine.Debug;

namespace VRMCast.Tracking.MediaPipe
{
    /// <summary>
    /// MediaPipe Pose Landmarker (lite model, CPU, LIVE_STREAM) at a lower cadence than the face tracker (PRD 3.4,
    /// 30.1). Only world landmarks and visibilities are copied out on the worker thread.
    /// </summary>
    public sealed class MediaPipePoseProvider : IUnityPoseTrackingProvider
    {
        public const string ProviderName = "MediaPipe Pose Landmarker";
        private const int PoolSize = 3;

        private readonly PoseProviderContext _ctx;
        private readonly LatestFrameBuffer<TrackingFrame> _latest = new LatestFrameBuffer<TrackingFrame>();
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly object _submitGate = new object();
        private readonly Dictionary<long, double> _submitTimes = new Dictionary<long, double>();

        private PoseLandmarker _landmarker;
        private TextureFramePool _pool;
        private int _poolWidth, _poolHeight;
        private long _lastTimestampMs = -1;
        private double _lastSubmitSeconds = -1;

        public MediaPipePoseProvider(PoseProviderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public string Name => ProviderName;
        public bool IsAvailable => _ctx.PoseLandmarkerModel != null;
        public bool IsRunning { get; private set; }
        public string UnavailableReasonKey { get; private set; }

        /// <summary>Set by the face provider's orientation probe so both tasks agree; defaults to the Unity → MediaPipe flip.</summary>
        public static bool FlipVertically = true;

        public void Start()
        {
            if (IsRunning) return;
            if (_ctx.PoseLandmarkerModel == null)
            {
                UnavailableReasonKey = "tracking.noModel";
                return;
            }

            var options = new PoseLandmarkerOptions(
                baseOptions: new global::Mediapipe.Tasks.Core.BaseOptions(
                    global::Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
                    modelAssetBuffer: _ctx.PoseLandmarkerModel.bytes),
                runningMode: global::Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                numPoses: 1,
                minPoseDetectionConfidence: 0.5f,
                minPosePresenceConfidence: 0.5f,
                minTrackingConfidence: 0.5f,
                outputSegmentationMasks: false,
                resultCallback: OnResult);

            _landmarker = PoseLandmarker.CreateFromOptions(options);
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

            textureFrame.ReadTextureOnCPU(tex, flipHorizontally: false, flipVertically: FlipVertically);
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
            _poolWidth = width;
            _poolHeight = height;
        }

        private void OnResult(PoseLandmarkerResult result, Image image, long timestampMs)
        {
            var now = _clock.Elapsed.TotalSeconds;
            double submitted;
            lock (_submitGate)
            {
                if (_submitTimes.TryGetValue(timestampMs, out submitted)) _submitTimes.Remove(timestampMs);
                else submitted = now;
            }

            var seconds = timestampMs / 1000.0;
            var hasBody = result.poseWorldLandmarks != null && result.poseWorldLandmarks.Count > 0
                          && result.poseWorldLandmarks[0].landmarks != null
                          && result.poseWorldLandmarks[0].landmarks.Count >= PoseFrameBuilder.LandmarkCount;
            TrackingFrame frame;
            if (!hasBody)
            {
                frame = PoseFrameBuilder.NoBody(seconds);
            }
            else
            {
                var landmarks = result.poseWorldLandmarks[0].landmarks;
                var world = new float[PoseFrameBuilder.LandmarkCount * 3];
                var visibility = new float[PoseFrameBuilder.LandmarkCount];
                for (var i = 0; i < PoseFrameBuilder.LandmarkCount; i++)
                {
                    var l = landmarks[i];
                    world[i * 3] = l.x;
                    world[i * 3 + 1] = l.y;
                    world[i * 3 + 2] = l.z;
                    visibility[i] = l.visibility ?? 1f;
                }
                frame = PoseFrameBuilder.Build(seconds, world, visibility);
            }

            _latest.Publish(frame);
            _ctx.Stats.OnResult(now, Math.Max(0, now - submitted), hasBody);
        }

        public void Dispose() => Stop();
    }

    internal static class MediaPipePoseProviderRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PoseTrackingProviderRegistry.Register(MediaPipePoseProvider.ProviderName, ctx => new MediaPipePoseProvider(ctx));
        }
    }
}
#endif
