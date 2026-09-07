using System;
using System.Collections.Generic;
using UnityEngine;
using VRMCast.Core.Tracking;

namespace VRMCast.Tracking
{
    /// <summary>Everything a face provider needs from the app; keeps provider assemblies free of app-level types.</summary>
    public sealed class FaceProviderContext
    {
        public CameraCaptureService Camera { get; }
        public TextAsset FaceLandmarkerModel { get; }
        public TrackingStats Stats { get; }
        public int TargetFps { get; }
        public int MaxInputWidth { get; }

        public FaceProviderContext(CameraCaptureService camera, TextAsset faceLandmarkerModel, TrackingStats stats, int targetFps, int maxInputWidth)
        {
            Camera = camera;
            FaceLandmarkerModel = faceLandmarkerModel;
            Stats = stats;
            TargetFps = targetFps;
            MaxInputWidth = maxInputWidth;
        }
    }

    /// <summary>Provider contract used by the runtime: a Core provider plus a main-thread tick to push camera frames.</summary>
    public interface IUnityFaceTrackingProvider : IFaceTrackingProvider
    {
        /// <summary>Called every Update on the main thread.</summary>
        void Tick();

        /// <summary>User-facing status key when the provider cannot run (null when healthy).</summary>
        string UnavailableReasonKey { get; }
    }

    /// <summary>
    /// Face providers live in optional assemblies (the MediaPipe one compiles only when the package is installed) and
    /// register a factory here at load time. The app asks the registry instead of referencing any engine directly.
    /// </summary>
    public static class FaceTrackingProviderRegistry
    {
        private static readonly List<(string name, Func<FaceProviderContext, IUnityFaceTrackingProvider> factory)> Factories =
            new List<(string, Func<FaceProviderContext, IUnityFaceTrackingProvider>)>();

        public static bool HasProviders => Factories.Count > 0;

        public static IReadOnlyList<string> Names
        {
            get
            {
                var names = new List<string>();
                foreach (var f in Factories) names.Add(f.name);
                return names;
            }
        }

        public static void Register(string name, Func<FaceProviderContext, IUnityFaceTrackingProvider> factory)
        {
            if (string.IsNullOrEmpty(name) || factory == null) return;
            for (var i = 0; i < Factories.Count; i++)
            {
                if (Factories[i].name == name) { Factories[i] = (name, factory); return; }
            }
            Factories.Add((name, factory));
        }

        /// <summary>Creates the first registered provider, or null when no tracking engine is installed.</summary>
        public static IUnityFaceTrackingProvider CreateDefault(FaceProviderContext context)
        {
            return Factories.Count == 0 ? null : Factories[0].factory(context);
        }
    }
}
