using System;
using System.Collections.Generic;
using UnityEngine;
using VRMCast.Audio;
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
        public const int DefaultPoseFps = 20;
        public const int DefaultPoseInputWidth = 480;
        public const int DefaultHandFps = 15;
        public const int DefaultHandInputWidth = 640;

        public enum Status { Off, NoEngine, NoModel, Starting, Searching, Tracking, CameraError, Calibrating }

        private readonly MonoBehaviour _host;
        private readonly IAvatarService _avatars;
        private readonly TextAsset _model;
        private readonly TextAsset _poseModel;
        private readonly TextAsset _handModel;
        private readonly TextAsset _holisticModel;
        private bool _poseProviderIsHolistic;
        private readonly AvatarDriver _driver;
        private IUnityPoseTrackingProvider _poseProvider;
        private long _lastPoseSequence;
        private IUnityHandTrackingProvider _handProvider;
        private long _lastHandSequence;
        private readonly HandSideResolver _handSides = new HandSideResolver();
        private TrackingFrame _latestHandFrame;
        private readonly CalibrationSampler _calibration = new CalibrationSampler();
        private IUnityFaceTrackingProvider _provider;
        private long _lastSequence;
        private bool _disposed;

        public CameraCaptureService Camera { get; }
        public FaceTrackingSettings Settings { get; }
        public MotionSolver Solver { get; }
        public TrackingStats Stats { get; } = new TrackingStats();
        public TrackingStats PoseStats { get; } = new TrackingStats();
        public TrackingStats HandStats { get; } = new TrackingStats();
        public MicrophoneCaptureService Microphone { get; }
        public LipSyncSettings LipSync { get; }
        public BodyTrackingSettings Body { get; }
        public BodyPoseSolver BodySolver { get; }
        public ArmPoseSolver ArmSolver { get; }
        public HandTrackingSettings Hands { get; }
        public FingerCurlSolver FingerSolver { get; }
        private TrackingFrame _latestPoseFrame;
        public bool PoseEngineAvailable => PoseTrackingProviderRegistry.HasProviders && _poseModel != null;
        /// <summary>Pose and hands from one model (hands anchored on the pose wrists): preferred whenever its model is present.</summary>
        public bool HolisticAvailable => _holisticModel != null && PoseTrackingProviderRegistry.Has(PoseTrackingProviderRegistry.HolisticName);
        public bool HandEngineAvailable => HolisticAvailable || (HandTrackingProviderRegistry.HasProviders && _handModel != null);
        public bool HandProviderRunning => (_poseProviderIsHolistic && _poseProvider != null && _poseProvider.IsRunning) || (_handProvider != null && _handProvider.IsRunning);
        public int FingerRigBones => _driver.FingerRigBoneCount;
        public TrackingFrame LatestPoseFrame => _latestPoseFrame;
        /// <summary>Last hand frame after side resolution (raw labels still inside each hand).</summary>
        public TrackingFrame LatestHandFrame => _latestHandFrame;
        public bool ArmsFromHands => ArmSolver.Pose.Left.FromHand || ArmSolver.Pose.Right.FromHand;

        /// <summary>Support text for the hand pipeline: why fingers are (not) moving.</summary>
        public string HandsStateKey
        {
            get
            {
                if (!Enabled) return "hands.state.off";
                if (!Body.HandsEnabled) return "hands.state.modeOff";
                if (!HandTrackingProviderRegistry.HasProviders && !PoseTrackingProviderRegistry.Has(PoseTrackingProviderRegistry.HolisticName)) return "hands.state.noEngine";
                if (_handModel == null && _holisticModel == null) return "hands.state.noModel";
                if (!HandProviderRunning) return "hands.state.starting";
                if (FingerRigBones == 0) return _avatars.HasAvatar ? "hands.state.noFingerBones" : "hands.state.noAvatar";
                var l = FingerSolver.Pose.Left.Tracked; var r = FingerSolver.Pose.Right.Tracked;
                if (l && r) return "hands.state.both";
                if (l || r) return "hands.state.one";
                return "hands.state.searching";
            }
        }
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

        public TrackingCoordinator(MonoBehaviour host, IAvatarService avatars, CameraCaptureService camera, TextAsset faceLandmarkerModel,
            FaceTrackingSettings settings = null, TextAsset poseLandmarkerModel = null, LipSyncSettings lipSync = null,
            BodyTrackingSettings body = null, MicrophoneCaptureService microphone = null,
            TextAsset handLandmarkerModel = null, HandTrackingSettings hands = null, TextAsset holisticLandmarkerModel = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _model = faceLandmarkerModel;
            _poseModel = poseLandmarkerModel;
            _handModel = handLandmarkerModel;
            _holisticModel = holisticLandmarkerModel;
            Settings = settings ?? new FaceTrackingSettings();
            Hands = hands ?? new HandTrackingSettings();
            LipSync = lipSync ?? new LipSyncSettings();
            Body = body ?? new BodyTrackingSettings();
            Microphone = microphone ?? new MicrophoneCaptureService(host, LipSync.Audio);
            Solver = new MotionSolver(Settings);
            BodySolver = new BodyPoseSolver(Body);
            ArmSolver = new ArmPoseSolver(Body);
            FingerSolver = new FingerCurlSolver(Hands);
            _driver = new AvatarDriver(avatars) { ArmRestAngleDeg = Body.ArmRestAngleDeg, RestCurl = Hands.RestCurl };

            Camera.StateChanged += _ => RefreshStatus();
            Microphone.StateChanged += _ => SettingsChanged?.Invoke();
            _avatars.AvatarLoaded += _ =>
            {
                _driver.OnAvatarLoaded();
                if (_driver.TryMeasureArm(out var upper, out var fore)) ArmSolver.SetAvatarArm(upper, fore);
                _driver.ApplyRest();
            };
        }

        public void SetLipSyncMode(LipSyncMode mode)
        {
            if (LipSync.Mode == mode) return;
            LipSync.Mode = mode;
            SyncMicrophone();
            SettingsChanged?.Invoke();
        }

        public void SetBodyMode(BodyTrackingMode mode)
        {
            if (Body.Mode == mode) return;
            Body.Mode = mode;
            SyncPoseProvider();
            SyncHandProvider();
            SettingsChanged?.Invoke();
        }

        /// <summary>The microphone runs only while tracking is on and a mode that uses audio is selected.</summary>
        private void SyncMicrophone()
        {
            var wanted = Enabled && LipSync.Mode != LipSyncMode.Camera;
            if (wanted && !Microphone.RequestedRunning) Microphone.Start();
            else if (!wanted && Microphone.RequestedRunning) Microphone.Stop();
        }

        private void SyncPoseProvider()
        {
            var wanted = Enabled && Body.Mode != BodyTrackingMode.Off && PoseEngineAvailable;
            var wantHolistic = wanted && Body.HandsEnabled && HolisticAvailable;
            if (_poseProvider != null && (!wanted || _poseProviderIsHolistic != wantHolistic))
            {
                try { _poseProvider.Stop(); _poseProvider.Dispose(); }
                catch (Exception e) { Debug.LogException(e); }
                _poseProvider = null;
                _poseProviderIsHolistic = false;
                BodySolver.Reset();
                ArmSolver.Reset();
                FingerSolver.Reset();
                _latestPoseFrame = default;
                _latestHandFrame = default;
            }
            if (wanted && _poseProvider == null)
            {
                try
                {
                    var ctx = new PoseProviderContext(Camera, _poseModel, PoseStats, wantHolistic ? DefaultHandFps : DefaultPoseFps,
                        wantHolistic ? DefaultHandInputWidth : DefaultPoseInputWidth, _holisticModel, wantHolistic ? HandStats : null);
                    _poseProvider = wantHolistic
                        ? PoseTrackingProviderRegistry.Create(PoseTrackingProviderRegistry.HolisticName, ctx)
                        : PoseTrackingProviderRegistry.CreateDefault(ctx);
                    _poseProviderIsHolistic = wantHolistic && _poseProvider != null;
                    _poseProvider?.Start();
                    PoseStats.Reset();
                    if (wantHolistic) { HandStats.Reset(); _lastHandSequence = 0; }
                    _lastPoseSequence = 0;
                    if (_poseProviderIsHolistic) Debug.Log($"VRMCast: holistic tracking started (pose + hands), finger rig bones: {_driver.FingerRigBoneCount}");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    _poseProvider?.Dispose();
                    _poseProvider = null;
                    _poseProviderIsHolistic = false;
                }
            }
        }

        private void SyncHandProvider()
        {
            // The separate hand landmarker is the fallback when no holistic model is present.
            var wanted = Enabled && Body.HandsEnabled && !_poseProviderIsHolistic && HandTrackingProviderRegistry.HasProviders && _handModel != null;
            if (wanted && _handProvider == null)
            {
                try
                {
                    _handProvider = HandTrackingProviderRegistry.CreateDefault(
                        new HandProviderContext(Camera, _handModel, HandStats, DefaultHandFps, DefaultHandInputWidth));
                    _handProvider?.Start();
                    HandStats.Reset();
                    _lastHandSequence = 0;
                    Debug.Log($"VRMCast: hand tracking started ({_handProvider?.Name}), finger rig bones: {_driver.FingerRigBoneCount}");
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    _handProvider?.Dispose();
                    _handProvider = null;
                }
            }
            else if (!wanted && _handProvider != null)
            {
                try { _handProvider.Stop(); _handProvider.Dispose(); }
                catch (Exception e) { Debug.LogException(e); }
                _handProvider = null;
                FingerSolver.Reset();
                _handSides.Reset();
                _latestHandFrame = default;
            }
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
            if (UsesDefaultMappings) Solver.SetMappings(ExpressionMappingDefaults.For(mode));
            SettingsChanged?.Invoke();
        }

        /// <summary>Settings were edited directly (UI sliders, profile restore): re-sync the optional providers too.</summary>
        public void NotifySettingsChanged()
        {
            SyncMicrophone();
            SyncPoseProvider();
            SyncHandProvider();
            SettingsChanged?.Invoke();
        }

        /// <summary>Starts the neutral-pose capture (PRD 12). Completes on its own after the window.</summary>
        public bool StartCalibration()
        {
            if (!Enabled || CurrentStatus == Status.NoEngine) return false;
            _calibration.Start();
            if (_poseProvider != null) BodySolver.StartCalibration(Time.realtimeSinceStartupAsDouble);
            RefreshStatus();
            return true;
        }

        public void ClearCalibration()
        {
            Settings.Calibration = CalibrationData.Identity;
            Body.NeutralRollRad = Body.NeutralYawRad = Body.NeutralPitchRad = 0f;
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
            SyncMicrophone();
            SyncPoseProvider();
            SyncHandProvider();
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
            SyncMicrophone();
            SyncPoseProvider();
            SyncHandProvider();
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
                    // Providers stamp frames with their own stopwatch; the solver and calibration compare against
                    // the app clock, so rebase to `now` once the frame is accepted.
                    frame.Timestamp = now;
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

            Microphone.Tick();
            _poseProvider?.Tick();
            if (_poseProvider != null && _poseProvider.TryGetLatest(out var poseFrame) && poseFrame.Timestamp > 0)
            {
                var sequence = (long)(poseFrame.Timestamp * 1_000_000);
                if (sequence != _lastPoseSequence)
                {
                    _lastPoseSequence = sequence;
                    poseFrame.Timestamp = now;
                    _latestPoseFrame = poseFrame;
                    BodySolver.Submit(poseFrame);
                    if (_poseProviderIsHolistic)
                    {
                        // Hands came with the pose, already on the user's real sides: no side resolution needed.
                        _latestHandFrame = poseFrame;
                        FingerSolver.Submit(poseFrame, now, Settings.MirrorUser);
                        ArmSolver.SubmitHands(poseFrame, now);
                    }
                }
            }

            _handProvider?.Tick();
            if (_handProvider != null && _handProvider.TryGetLatest(out var handFrame) && handFrame.Timestamp > 0)
            {
                var sequence = (long)(handFrame.Timestamp * 1_000_000);
                if (sequence != _lastHandSequence)
                {
                    _lastHandSequence = sequence;
                    // Decide which real hand each detection is (continuity, then pose wrists, then label) so fingers
                    // and arms always agree.
                    _handSides.Resolve(ref handFrame, _latestPoseFrame.Pose, swap: false);
                    _latestHandFrame = handFrame;
                    FingerSolver.Submit(handFrame, now, Settings.MirrorUser);
                    ArmSolver.SubmitHands(handFrame, now);
                }
            }

            var pose = Solver.Update(dt, now);
            HybridLipSolver.Apply(pose.Expressions, LipSync, pose.Confidence, Microphone.Meter.Envelope, Microphone.Meter.IsOpen, Microphone.IsRunning);
            var body = BodySolver.Update(dt, now, Settings.MirrorUser);
            var arms = ArmSolver.Update(_latestPoseFrame, BodySolver.IsTracking, now, dt, Settings.MirrorUser, false);
            var hands = FingerSolver.Update(dt, now, HandProviderRunning && Body.HandsEnabled);
            _driver.Apply(pose, body, arms, hands);
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

        /// <summary>Expression weights (hotkeys) layered over tracking every frame.</summary>
        public void SetExpressionOverrides(IReadOnlyDictionary<string, float> overrides) => _driver.SetExpressionOverrides(overrides);

        /// <summary>Replaces the expression mapping table and remembers whether it is the default one.</summary>
        public void SetMappings(IEnumerable<ExpressionMapping> mappings, bool isDefault)
        {
            Solver.SetMappings(mappings);
            UsesDefaultMappings = isDefault;
        }

        public bool UsesDefaultMappings { get; private set; } = true;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopProvider();
            Microphone.Dispose();
        }
    }
}
