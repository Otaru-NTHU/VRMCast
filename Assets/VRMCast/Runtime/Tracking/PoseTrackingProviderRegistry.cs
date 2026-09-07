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

        public PoseProviderContext(CameraCaptureService camera, TextAsset poseLandmarkerModel, TrackingStats stats, int targetFps, int maxInputWidth)
        {
            Camera = camera;
            PoseLandmarkerModel = poseLandmarkerModel;
            Stats = stats;
            TargetFps = targetFps;
            MaxInputWidth = maxInputWidth;
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
            return Factories.Count == 0 ? null : Factories[0].factory(context);
        }
    }
}
