using System;
using UnityEngine;
using VRMCast.Avatar;
using VRMCast.Core.Localization;
using VRMCast.Core.Tracking;

namespace VRMCast.Tracking
{
    /// <summary>
    /// Main-thread owner of the tracking pipeline (PRD 29): camera → provider → latest frame → calibration →
    /// MotionSolver → AvatarDriver. Tracking runs at its own cadence; the solver is stepped every render frame.
    /// </summary>
    public sealed class TrackingCoordinator : IDisposable
    {
        public const int DefaultTrackingFps = 30;
        public const int DefaultMaxInputWidth = 640;

        public enum Status { Off, NoEngine, NoModel, Starting, Searching, Tracking, CameraError, Calibrating }

        private readonly MonoBehaviour _host;
        private readonly IAvatarService _avatars;
        private readonly TextAsset _model;
        private readonly AvatarDriver _driver;
        private readonly CalibrationSampler _calibration = new CalibrationSampler();
        private IUnityFaceTrackingProvider _provider;
        private long _lastSequence;
        private bool _disposed;

        public CameraCaptureService Camera { get; }
        public FaceTrackingSettings Settings { get; }
        public MotionSolver Solver { get; }
        public TrackingStats Stats { get; } = new TrackingStats();
        public bool Enabled { get; private set; }
        public Status CurrentStatus { get; private set; } = Status.Off;
        public bool EngineAvailable => FaceTrackingProviderRegistry.HasProviders;
        public bool ModelAvailable => _model != null;
        public CalibrationSampler Calibration => _calibration;
        public float CalibrationProgress => _calibration.Progress;

        /// <summary>Raised on the main thread whenever <see cref="CurrentStatus"/> changes.</summary>
        public event Action<Status> StatusChanged;
        public event Action<bool> CalibrationFinished;
        public event Action SettingsChanged;
        public event Action<bool> EnabledChanged;

        public TrackingCoordinator(MonoBehaviour host, IAvatarService avatars, CameraCaptureService camera, TextAsset faceLandmarkerModel, FaceTrackingSettings settings = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _model = faceLandmarkerModel;
            Settings = settings ?? new FaceTrackingSettings();
            Solver = new MotionSolver(Settings);
            _driver = new AvatarDriver(avatars);

            Camera.StateChanged += _ => RefreshStatus();
            _avatars.AvatarLoaded += _ => _driver.ApplyRest();
        }

        public void SetEnabled(bool enabled)
        {
            if (Enabled == enabled) return;
            Enabled = enabled;
            if (enabled) StartProvider();
            else StopProvider();
            RefreshStatus();
            EnabledChanged?.Invoke(enabled);
        }

        public void SetMode(FaceTrackingMode mode)
        {
            if (Settings.Mode == mode) return;
            Settings.Mode = mode;
            Solver.SetMappings(ExpressionMappingDefaults.For(mode));
            SettingsChanged?.Invoke();
        }

        public void NotifySettingsChanged() => SettingsChanged?.Invoke();

        /// <summary>Starts the neutral-pose capture (PRD 12). Completes on its own after the window.</summary>
        public bool StartCalibration()
        {
            if (!Enabled || CurrentStatus == Status.NoEngine) return false;
            _calibration.Start();
            RefreshStatus();
            return true;
        }

        public void ClearCalibration()
        {
            Settings.Calibration = CalibrationData.Identity;
            SettingsChanged?.Invoke();
        }

        private void StartProvider()
        {
            if (_provider != null) return;
            if (!EngineAvailable || _model == null) return;

            var context = new FaceProviderContext(Camera, _model, Stats, DefaultTrackingFps, DefaultMaxInputWidth);
            try
            {
                _provider = FaceTrackingProviderRegistry.CreateDefault(context);
                _provider?.Start();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _provider?.Dispose();
                _provider = null;
            }
            Camera.Start();
            Stats.Reset();
            _lastSequence = 0;
            Solver.Reset();
        }

        private void StopProvider()
        {
            _calibration.Cancel();
            if (_provider != null)
            {
                try { _provider.Stop(); _provider.Dispose(); }
                catch (Exception e) { Debug.LogException(e); }
                _provider = null;
            }
            Camera.Stop();
            Solver.Reset();
            _driver.ApplyRest();
        }

        /// <summary>Call every Update.</summary>
        public void Tick(float dt, double now)
        {
            if (_disposed) return;
            Camera.Tick();

            if (!Enabled || _provider == null)
            {
                _driver.ApplyRest();
                return;
            }

            _provider.Tick();

            if (_provider.TryGetLatest(out var frame) && frame.Timestamp > 0)
            {
                var sequence = (long)(frame.Timestamp * 1_000_000);
                if (sequence != _lastSequence)
                {
                    _lastSequence = sequence;
                    Solver.Submit(frame);
                    if (_calibration.IsRunning && _calibration.Add(frame))
                    {
                        var ok = _calibration.Result != null;
                        if (ok) Settings.Calibration = _calibration.Result;
                        CalibrationFinished?.Invoke(ok);
                        if (ok) SettingsChanged?.Invoke();
                    }
                }
            }

            var pose = Solver.Update(dt, now);
            _driver.Apply(pose);
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            Status status;
            if (!Enabled) status = Status.Off;
            else if (!EngineAvailable) status = Status.NoEngine;
            else if (_model == null) status = Status.NoModel;
            else if (Camera.CurrentState == CameraCaptureService.State.PermissionDenied
                     || Camera.CurrentState == CameraCaptureService.State.NoDevice
                     || Camera.CurrentState == CameraCaptureService.State.Failed
                     || Camera.CurrentState == CameraCaptureService.State.Stalled) status = Status.CameraError;
            else if (Camera.CurrentState != CameraCaptureService.State.Running) status = Status.Starting;
            else if (_calibration.IsRunning) status = Status.Calibrating;
            else status = Solver.IsTracking ? Status.Tracking : Status.Searching;

            if (status == CurrentStatus) return;
            CurrentStatus = status;
            StatusChanged?.Invoke(status);
        }

        public string ProviderUnavailableReasonKey => _provider?.UnavailableReasonKey;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopProvider();
        }
    }
}
