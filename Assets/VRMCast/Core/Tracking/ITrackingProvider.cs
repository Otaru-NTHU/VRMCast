using System;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// A source of <see cref="TrackingFrame"/>s. Providers run their inference wherever they like (worker thread,
    /// native callback) but must publish frames through a thread-safe latest-frame buffer; consumers poll from
    /// the main thread. MVP-A defines the contract only; no provider is implemented yet (PRD 41.8).
    /// </summary>
    public interface ITrackingProvider : IDisposable
    {
        string Name { get; }
        bool IsAvailable { get; }
        bool IsRunning { get; }

        void Start();
        void Stop();

        /// <summary>Copies the most recent frame. Returns false when no frame has been produced yet.</summary>
        bool TryGetLatest(out TrackingFrame frame);
    }

    public interface IFaceTrackingProvider : ITrackingProvider { }

    public interface IPoseTrackingProvider : ITrackingProvider { }

    public interface IHandTrackingProvider : ITrackingProvider { }

    /// <summary>External sources such as VMC, OSC or ARKit relays (PRD 15). Reserved.</summary>
    public interface IExternalTrackingProvider : ITrackingProvider { }
}
