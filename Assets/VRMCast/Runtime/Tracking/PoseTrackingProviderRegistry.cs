using System;
using System.Collections.Generic;
using UnityEngine;
using VRMCast.Core.Tracking;

namespace VRMCast.Tracking
{
    public sealed class PoseProviderContext
    {
        public CameraCaptureService Camera { get; }
        public TextAsset PoseLandmarkerModel { get; }
        public TrackingStats Stats { get; }
        public int TargetFps { get; }
        public int MaxInputWidth { get; }
        /// <summary>Holistic model (pose + hands in one), used by the holistic provider only.</summary>
        public TextAsset HolisticLandmarkerModel { get; }
        /// <summary>Optional second stats sink for the hands when one provider delivers both.</summary>
        public TrackingStats HandStats { get; }

        public PoseProviderContext(CameraCaptureService camera, TextAsset poseLandmarkerModel, TrackingStats stats, int targetFps, int maxInputWidth,
            TextAsset holisticLandmarkerModel = null, TrackingStats handStats = null)
        {
            Camera = camera;
            PoseLandmarkerModel = poseLandmarkerModel;
            Stats = stats;
            TargetFps = targetFps;
            MaxInputWidth = maxInputWidth;
            HolisticLandmarkerModel = holisticLandmarkerModel;
            HandStats = handStats;
        }
    }

    public interface IUnityPoseTrackingProvider : IPoseTrackingProvider
    {
        void Tick();
        string UnavailableReasonKey { get; }
    }

    /// <summary>Optional pose engines register here, exactly like <see cref="FaceTrackingProviderRegistry"/>.</summary>
    public static class PoseTrackingProviderRegistry
    {
        private static readonly List<(string name, Func<PoseProviderContext, IUnityPoseTrackingProvider> factory)> Factories =
            new List<(string, Func<PoseProviderContext, IUnityPoseTrackingProvider>)>();

        public static bool HasProviders => Factories.Count > 0;

        public static void Register(string name, Func<PoseProviderContext, IUnityPoseTrackingProvider> factory)
        {
            if (string.IsNullOrEmpty(name) || factory == null) return;
            for (var i = 0; i < Factories.Count; i++)
            {
                if (Factories[i].name == name) { Factories[i] = (name, factory); return; }
            }
            Factories.Add((name, factory));
        }

        public static IUnityPoseTrackingProvider CreateDefault(PoseProviderContext context)
        {
            // Prefer the plain pose landmarker as the default; the holistic one is chosen explicitly by name.
            foreach (var f in Factories) if (f.name != HolisticName) return f.factory(context);
            return Factories.Count == 0 ? null : Factories[0].factory(context);
        }

        public const string HolisticName = "MediaPipe Holistic Landmarker";

        public static bool Has(string name)
        {
            foreach (var f in Factories) if (f.name == name) return true;
            return false;
        }

        public static IUnityPoseTrackingProvider Create(string name, PoseProviderContext context)
        {
            foreach (var f in Factories) if (f.name == name) return f.factory(context);
            return null;
        }
    }
}
