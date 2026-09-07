using System;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.Audio;
using VRMCast.Avatar;
using VRMCast.Backgrounds;
using VRMCast.CameraControl;
using VRMCast.Core.Backgrounds;
using VRMCast.Core.Camera;
using VRMCast.Core.Localization;
using VRMCast.Core.Rendering;
using VRMCast.Diagnostics;
using VRMCast.Output;
using VRMCast.Core.Tracking;
using VRMCast.Rendering;
using VRMCast.Tracking;
using VRMCast.UI;

namespace VRMCast.App
{
    /// <summary>
    /// Composition root. The only MonoBehaviour that knows about every service; it builds the graph in Awake,
    /// ticks the per-frame work, and tears everything down in OnDestroy. References are serialized in the scene
    /// (no GameObject.Find, no Resources.Load).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [Header("Serialized references")]
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private Material _backgroundImageMaterial;
        [Tooltip("MediaPipe face_landmarker_v2_with_blendshapes.bytes from the com.github.homuler.mediapipe package.")]
        [SerializeField] private TextAsset _faceLandmarkerModel;
        [Tooltip("MediaPipe pose_landmarker_lite.bytes from the com.github.homuler.mediapipe package.")]
        [SerializeField] private TextAsset _poseLandmarkerModel;

        [Header("Defaults (PRD 34)")]
        [SerializeField] private int _outputWidth = OutputSettings.DefaultWidth;
        [SerializeField] private int _outputHeight = OutputSettings.DefaultHeight;
        [SerializeField] private int _outputFps = OutputSettings.DefaultFps;
        [SerializeField] private FramingPreset _framing = FramingPreset.Bust;
        [SerializeField] private BackgroundMode _backgroundMode = BackgroundMode.ChromaKey;
        [SerializeField] private string _chromaColorHex = BackgroundSettings.DefaultChromaHex;

        [Header("Development")]
        [Tooltip("Optional .vrm to load on start (editor convenience). Command-line paths take precedence.")]
        [SerializeField] private string _startupVrmPath;

        public const string LanguagePrefKey = "vrmcast.language";
        public const string CameraPrefKey = "vrmcast.camera.device";
        public const string MirrorPrefKey = "vrmcast.tracking.mirror";
        public const string TrackingModePrefKey = "vrmcast.tracking.mode";
        public const string TrackingEnabledPrefKey = "vrmcast.tracking.enabled";
        public const string MicrophonePrefKey = "vrmcast.microphone.device";
        public const string LipSyncModePrefKey = "vrmcast.lipsync.mode";
        public const string LipSyncSensitivityPrefKey = "vrmcast.lipsync.sensitivity";
        public const string LipSyncGatePrefKey = "vrmcast.lipsync.gate";
        public const string BodyModePrefKey = "vrmcast.body.mode";

        private AppServices _services;
        private MainView _view;
        private Transform _avatarRoot;

        public AppServices Services => _services;

        private void Awake()
        {
            if (_uiDocument == null)
            {
                Debug.LogError("AppBootstrap: UIDocument reference is missing. Run 'VRMCast > Setup > Rebuild Main Scene' in the editor.");
            }

            _avatarRoot = new GameObject("AvatarRoot").transform;
            _avatarRoot.SetParent(transform, worldPositionStays: false);

            var localizer = new Localizer(LoadLanguagePreference());
            localizer.LanguageChanged += language =>
            {
                PlayerPrefs.SetString(LanguagePrefKey, language.Code());
                PlayerPrefs.Save();
            };

            var outputSettings = SafeOutputSettings();
            var render = new RenderService(transform, outputSettings);
            var avatars = new AvatarService(_avatarRoot);

            var backgroundSettings = new BackgroundSettings { Mode = _backgroundMode };
            if (RgbaColor.TryParseHex(_chromaColorHex, out var chroma)) backgroundSettings.ChromaColor = chroma;
            var background = new BackgroundService(render, _backgroundImageMaterial, backgroundSettings);

            var camera = new AvatarCameraController(render, avatars, new AvatarCameraState { Preset = _framing });

            var outputs = new OutputService(render);
            outputs.SetAlphaProvider(() => background.Settings.RequiresAlpha);
            var preview = new PreviewOutput();
            var debugOutput = new NullOutput();
            outputs.Register(preview);
            outputs.Register(debugOutput);
            outputs.Start(preview);

            var diagnostics = new DiagnosticsService(render, avatars, background, camera, outputs);

            var capture = new CameraCaptureService(this);
            var savedCamera = PlayerPrefs.GetString(CameraPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(savedCamera)) capture.Select(savedCamera);

            var trackingSettings = new FaceTrackingSettings
            {
                MirrorUser = PlayerPrefs.GetInt(MirrorPrefKey, 1) != 0,
                Mode = PlayerPrefs.GetInt(TrackingModePrefKey, 0) == 1 ? FaceTrackingMode.Advanced : FaceTrackingMode.Basic,
            };
            var lipSync = new LipSyncSettings
            {
                Mode = (LipSyncMode)Mathf.Clamp(PlayerPrefs.GetInt(LipSyncModePrefKey, (int)LipSyncMode.Hybrid), 0, 2),
            };
            lipSync.Audio.Sensitivity = PlayerPrefs.GetFloat(LipSyncSensitivityPrefKey, lipSync.Audio.Sensitivity);
            lipSync.Audio.GateDb = PlayerPrefs.GetFloat(LipSyncGatePrefKey, lipSync.Audio.GateDb);
            var body = new BodyTrackingSettings
            {
                Mode = PlayerPrefs.GetInt(BodyModePrefKey, (int)BodyTrackingMode.UpperBody) == 0 ? BodyTrackingMode.Off : BodyTrackingMode.UpperBody,
            };
            var microphone = new MicrophoneCaptureService(this, lipSync.Audio);
            var savedMic = PlayerPrefs.GetString(MicrophonePrefKey, string.Empty);
            if (!string.IsNullOrEmpty(savedMic)) microphone.Select(savedMic);

            var tracking = new TrackingCoordinator(this, avatars, capture, _faceLandmarkerModel, trackingSettings, _poseLandmarkerModel, lipSync, body, microphone);
            tracking.SettingsChanged += () =>
            {
                PlayerPrefs.SetInt(MirrorPrefKey, tracking.Settings.MirrorUser ? 1 : 0);
                PlayerPrefs.SetInt(TrackingModePrefKey, tracking.Settings.Mode == FaceTrackingMode.Advanced ? 1 : 0);
                PlayerPrefs.SetInt(LipSyncModePrefKey, (int)tracking.LipSync.Mode);
                PlayerPrefs.SetFloat(LipSyncSensitivityPrefKey, tracking.LipSync.Audio.Sensitivity);
                PlayerPrefs.SetFloat(LipSyncGatePrefKey, tracking.LipSync.Audio.GateDb);
                PlayerPrefs.SetInt(BodyModePrefKey, (int)tracking.Body.Mode);
                if (!string.IsNullOrEmpty(microphone.SelectedDevice)) PlayerPrefs.SetString(MicrophonePrefKey, microphone.SelectedDevice);
                PlayerPrefs.Save();
            };
            capture.StateChanged += _ =>
            {
                if (!string.IsNullOrEmpty(capture.SelectedDevice)) PlayerPrefs.SetString(CameraPrefKey, capture.SelectedDevice);
            };
            tracking.EnabledChanged += enabled =>
            {
                PlayerPrefs.SetInt(TrackingEnabledPrefKey, enabled ? 1 : 0);
                PlayerPrefs.Save();
            };
            diagnostics.AttachTracking(tracking);

            _services = new AppServices(localizer, render, avatars, background, camera, outputs, preview, debugOutput, diagnostics, capture, tracking);
        }

        private void Start()
        {
            // UIDocument creates its root visual element in OnEnable, which runs after every Awake; this
            // bootstrap runs first (DefaultExecutionOrder), so the UI can only be bound from Start.
            if (_uiDocument != null)
            {
                var root = _uiDocument.rootVisualElement;
                if (root == null)
                {
                    Debug.LogError("AppBootstrap: UIDocument has no root visual element. Check its Panel Settings and Source Asset, or run 'VRMCast > Setup > Rebuild Main Scene'.");
                }
                else
                {
                    _view = new MainView(root, _services);
                }
            }

            _services.Camera.Reframe();
            _services.Camera.Apply(force: true);

            var startupPath = ResolveStartupPath();
            if (!string.IsNullOrEmpty(startupPath))
            {
                _ = _services.Avatars.LoadAsync(startupPath);
            }

            if (PlayerPrefs.GetInt(TrackingEnabledPrefKey, 0) != 0 && _services.Tracking.EngineAvailable)
            {
                _services.Tracking.SetEnabled(true);
            }
        }

        private void Update()
        {
            // Tracking is applied in Update so UniVRM (LateUpdate) sees this frame's bones and expressions.
            _services.Tracking.Tick(Time.unscaledDeltaTime, Time.realtimeSinceStartupAsDouble);

            _view?.TickFast();
            if (_services.Diagnostics.Tick(Time.unscaledDeltaTime))
            {
                _view?.RefreshDiagnostics();
            }
        }


        private void LateUpdate()
        {
            // Camera first, then the image plane that hangs off it.
            _services.Camera.Apply();
            _services.Background.UpdateImagePlane();
        }

        private void OnDestroy()
        {
            _view?.Dispose();
            _view = null;
            _services?.Dispose();
            _services = null;
        }

        private OutputSettings SafeOutputSettings()
        {
            try
            {
                return new OutputSettings(_outputWidth, _outputHeight, _outputFps);
            }
            catch (ArgumentOutOfRangeException)
            {
                Debug.LogWarning("AppBootstrap: invalid serialized output settings, falling back to 1920x1080 @ 30.");
                return OutputSettings.Default;
            }
        }

        private static AppLanguage LoadLanguagePreference()
        {
            var code = PlayerPrefs.GetString(LanguagePrefKey, string.Empty);
            return AppLanguageExtensions.TryParseCode(code, out var language) ? language : AppLanguageExtensions.Default;
        }

        private string ResolveStartupPath()
        {
            // "open with" / command line: the first argument that names an existing .vrm wins.
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(arg)) return arg;
            }
            return !string.IsNullOrEmpty(_startupVrmPath) && System.IO.File.Exists(_startupVrmPath) ? _startupVrmPath : null;
        }
    }
}
