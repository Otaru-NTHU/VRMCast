// Compiled only when com.github.homuler.mediapipe (>= 0.16.0) is installed; see Scripts/setup-mediapipe.sh.
#if VRMCAST_MEDIAPIPE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Mediapipe;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.HolisticLandmarker;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using VRMCast.Core.Tracking;
using Debug = UnityEngine.Debug;

namespace VRMCast.Tracking.MediaPipe
{
    /// <summary>
    /// MediaPipe Holistic Landmarker (CPU, LIVE_STREAM): pose plus both hands from one model. The hands are found
    /// from the pose's own wrists, so which hand is which comes straight from the model and never from a handedness
    /// guess: a single raised hand can only ever drive one arm. Publishes a <see cref="TrackingFrame"/> with the
    /// pose block and the left/right hand blocks already on the user's real sides.
    /// </summary>
    public sealed class MediaPipeHolisticProvider : IUnityPoseTrackingProvider
    {
        public const string ProviderName = "MediaPipe Holistic Landmarker";
        private const int PoolSize = 3;

        private readonly PoseProviderContext _ctx;
        private readonly LatestFrameBuffer<TrackingFrame> _latest = new LatestFrameBuffer<TrackingFrame>();
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly object _submitGate = new object();
        private readonly Dictionary<long, double> _submitTimes = new Dictionary<long, double>();

        private HolisticLandmarker _landmarker;
        private TextureFramePool _pool;
        private RenderTexture _scaled;
        private int _poolWidth, _poolHeight;
        private volatile float _aspect = 16f / 9f;
        private long _lastTimestampMs = -1;
        private double _lastSubmitSeconds = -1;

        public MediaPipeHolisticProvider(PoseProviderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public string Name => ProviderName;
        public bool IsAvailable => _ctx.HolisticLandmarkerModel != null;
        public bool IsRunning { get; private set; }
        public string UnavailableReasonKey { get; private set; }

        public void Start()
        {
            if (IsRunning) return;
            if (_ctx.HolisticLandmarkerModel == null)
            {
                UnavailableReasonKey = "tracking.noModel";
                return;
            }

            var options = new HolisticLandmarkerOptions(
                baseOptions: new global::Mediapipe.Tasks.Core.BaseOptions(
                    global::Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU,
                    modelAssetBuffer: _ctx.HolisticLandmarkerModel.bytes),
                runningMode: global::Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                minPoseDetectionConfidence: 0.5f,
                minPoseLandmarksConfidence: 0.5f,
                minHandLandmarksConfidence: 0.5f,
                outputFaceBlendshapes: false,
                outputSegmentationMask: false,
                resultCallback: OnResult);

            _landmarker = HolisticLandmarker.CreateFromOptions(options);
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
                _ctx.HandStats?.OnSubmitted();
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
            _scaled = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "HolisticTrackingInput" };
            _scaled.Create();
            _poolWidth = width;
            _poolHeight = height;
            _aspect = height > 0 ? width / (float)height : 1f;
        }

        private void ReleaseScaled()
        {
            if (_scaled == null) return;
            _scaled.Release();
            UnityEngine.Object.Destroy(_scaled);
            _scaled = null;
        }

        private static bool Has(in Landmarks l, int count) => l.landmarks != null && l.landmarks.Count >= count;
        private static bool Has(in NormalizedLandmarks l, int count) => l.landmarks != null && l.landmarks.Count >= count;

        private static HandTracking? BuildHand(in Landmarks world, in NormalizedLandmarks image, bool labelLeft)
        {
            if (!Has(world, HandFrameBuilder.LandmarkCount)) return null;
            var w = new float[HandFrameBuilder.LandmarkCount * 3];
            for (var i = 0; i < HandFrameBuilder.LandmarkCount; i++)
            {
                var l = world.landmarks[i];
                w[i * 3] = l.x; w[i * 3 + 1] = l.y; w[i * 3 + 2] = l.z;
            }
            var hand = new HandTracking { Confidence = 1f, LandmarksXyz = w, LabelLeft = labelLeft };
            if (Has(image, HandFrameBuilder.LandmarkCount))
            {
                var n = image.landmarks;
                hand.WristU = n[HandFrameBuilder.Wrist].x; hand.WristV = n[HandFrameBuilder.Wrist].y;
                hand.MiddleMcpU = n[HandFrameBuilder.MiddleMcp].x; hand.MiddleMcpV = n[HandFrameBuilder.MiddleMcp].y;
                hand.IndexMcpU = n[HandFrameBuilder.IndexMcp].x; hand.IndexMcpV = n[HandFrameBuilder.IndexMcp].y;
                hand.LittleMcpU = n[HandFrameBuilder.LittleMcp].x; hand.LittleMcpV = n[HandFrameBuilder.LittleMcp].y;
            }
            return hand;
        }

        private void OnResult(in HolisticLandmarkerResult result, Image image, long timestampMs)
        {
            var now = _clock.Elapsed.TotalSeconds;
            double submitted;
            lock (_submitGate)
            {
                if (_submitTimes.TryGetValue(timestampMs, out submitted)) _submitTimes.Remove(timestampMs);
                else submitted = now;
            }

            var seconds = timestampMs / 1000.0;
            TrackingFrame frame;
            var hasBody = Has(result.poseWorldLandmarks, PoseFrameBuilder.LandmarkCount);
            if (!hasBody)
            {
                frame = PoseFrameBuilder.NoBody(seconds);
            }
            else
            {
                var landmarks = result.poseWorldLandmarks.landmarks;
                var world = new float[PoseFrameBuilder.LandmarkCount * 3];
                var visibility = new float[PoseFrameBuilder.LandmarkCount];
                for (var i = 0; i < PoseFrameBuilder.LandmarkCount; i++)
                {
                    var l = landmarks[i];
                    world[i * 3] = l.x; world[i * 3 + 1] = l.y; world[i * 3 + 2] = l.z;
                    visibility[i] = l.visibility ?? 1f;
                }
                float[] normalized = null;
                if (Has(result.poseLandmarks, PoseFrameBuilder.LandmarkCount))
                {
                    var img = result.poseLandmarks.landmarks;
                    normalized = new float[PoseFrameBuilder.LandmarkCount * 3];
                    for (var i = 0; i < PoseFrameBuilder.LandmarkCount; i++)
                    {
                        var l = img[i];
                        normalized[i * 3] = l.x; normalized[i * 3 + 1] = l.y; normalized[i * 3 + 2] = l.z;
                        // Holistic reports pose visibility only on the world set; mirror it when missing.
                        if (l.visibility.HasValue) visibility[i] = Math.Max(visibility[i], l.visibility.Value);
                    }
                }
                // Validated on device: the holistic model's pose and hand labels are already the user's real sides
                // (unlike the stand-alone Pose Landmarker, whose labels are image sides).
                frame = PoseFrameBuilder.Build(seconds, world, visibility, normalized, _aspect, swapSides: false);
                frame.LeftHand = BuildHand(result.leftHandWorldLandmarks, result.leftHandLandmarks, labelLeft: true);
                frame.RightHand = BuildHand(result.rightHandWorldLandmarks, result.rightHandLandmarks, labelLeft: false);
            }

            _latest.Publish(frame);
            var latency = Math.Max(0, now - submitted);
            _ctx.Stats.OnResult(now, latency, hasBody);
            _ctx.HandStats?.OnResult(now, latency, frame.LeftHand.HasValue || frame.RightHand.HasValue);
        }

        public void Dispose() => Stop();
    }

    internal static class MediaPipeHolisticProviderRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PoseTrackingProviderRegistry.Register(MediaPipeHolisticProvider.ProviderName, ctx => new MediaPipeHolisticProvider(ctx));
        }
    }
}
#endif
