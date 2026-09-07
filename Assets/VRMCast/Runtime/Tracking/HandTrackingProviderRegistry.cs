using System;
using System.Collections.Generic;
using UnityEngine;
using VRMCast.Core.Tracking;

namespace VRMCast.Tracking
{
    public sealed class HandProviderContext
    {
        public CameraCaptureService Camera { get; }
        public TextAsset HandLandmarkerModel { get; }
        public TrackingStats Stats { get; }
        public int TargetFps { get; }
        public int MaxInputWidth { get; }

        public HandProviderContext(CameraCaptureService camera, TextAsset handLandmarkerModel, TrackingStats stats, int targetFps, int maxInputWidth)
        {
            Camera = camera;
            HandLandmarkerModel = handLandmarkerModel;
            Stats = stats;
            TargetFps = targetFps;
            MaxInputWidth = maxInputWidth;
        }
    }

    public interface IUnityHandTrackingProvider : IHandTrackingProvider
    {
        void Tick();
        string UnavailableReasonKey { get; }
    }

    /// <summary>Optional hand engines register here, exactly like <see cref="PoseTrackingProviderRegistry"/>.</summary>
    public static class HandTrackingProviderRegistry
    {
        private static readonly List<(string name, Func<HandProviderContext, IUnityHandTrackingProvider> factory)> Factories =
            new List<(string, Func<HandProviderContext, IUnityHandTrackingProvider>)>();

        public static bool HasProviders => Factories.Count > 0;

        public static void Register(string name, Func<HandProviderContext, IUnityHandTrackingProvider> factory)
        {
            if (string.IsNullOrEmpty(name) || factory == null) return;
            for (var i = 0; i < Factories.Count; i++)
            {
                if (Factories[i].name == name) { Factories[i] = (name, factory); return; }
            }
            Factories.Add((name, factory));
        }

        public static IUnityHandTrackingProvider CreateDefault(HandProviderContext context)
        {
            return Factories.Count == 0 ? null : Factories[0].factory(context);
        }
    }
}
