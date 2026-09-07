using System;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.Audio;
using VRMCast.Avatar;
using VRMCast.Backgrounds;
using VRMCast.CameraControl;
using VRMCast.Core.Backgrounds;
using VRMCast.Core.Vrm;
using VRMCast.Core.Camera;
using VRMCast.Core.Localization;
using VRMCast.Core.Rendering;
using VRMCast.Diagnostics;
using VRMCast.Hotkeys;
using VRMCast.Profiles;
using VRMCast.Core.Profiles;
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
        [Tooltip("MediaPipe hand_landmarker.bytes from the com.github.homuler.mediapipe package.")]
        [SerializeField] private TextAsset _handLandmarkerModel;

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
            var trackingSettings = new FaceTrackingSettings();
            var lipSync = new LipSyncSettings();
            var body = new BodyTrackingSettings();
            var hands = new HandTrackingSettings();
            var microphone = new MicrophoneCaptureService(this, lipSync.Audio);
            var tracking = new TrackingCoordinator(this, avatars, capture, _faceLandmarkerModel, trackingSettings, _poseLandmarkerModel, lipSync, body, microphone,
                _handLandmarkerModel, hands);
            diagnostics.AttachTracking(tracking);

            _services = new AppServices(localizer, render, avatars, background, camera, outputs, preview, debugOutput, diagnostics, capture, tracking);

            _hotkeys = new HotkeyService();
            _hotkeys.SetBindings(Core.Hotkeys.HotkeyBinding.Defaults());
            _services.Hotkeys = _hotkeys;

            _profiles = new ProfileService(Application.persistentDataPath);
            _profiles.Bind(CaptureProfile, ApplyProfile);
            _services.Profiles = _profiles;

            // Any settings change marks the profile dirty; auto-save writes after a quiet period.
            tracking.SettingsChanged += _profiles.MarkDirty;
            tracking.EnabledChanged += _ => _profiles.MarkDirty();
            capture.StateChanged += _ => _profiles.MarkDirty();
            render.SettingsChanged += _ => _profiles.MarkDirty();
            background.SettingsChanged += _profiles.MarkDirty;
            camera.StateChanged += _profiles.MarkDirty;
            avatars.AvatarLoaded += _ => { _hotkeys.ReleaseAll(); _profiles.MarkDirty(); };
            avatars.AvatarUnloaded += () => { _hotkeys.ReleaseAll(); _profiles.MarkDirty(); };
            _hotkeys.BindingsChanged += _profiles.MarkDirty;
        }

        private ProfileService _profiles;
        private HotkeyService _hotkeys;

        /// <summary>Fills a profile from the live services (PRD 23).</summary>
        private ProfileData CaptureProfile(ProfileData p)
        {
            var t = _services.Tracking;
            p.vrmPath = _services.Avatars.HasAvatar ? _services.Avatars.Current.Info.FilePath : p.vrmPath;
            p.vrmVersion = _services.Avatars.HasAvatar ? (int)_services.Avatars.Current.Info.Version : p.vrmVersion;
            p.cameraDevice = _services.Camera2D.SelectedDevice ?? "";
            p.microphoneDevice = _services.Microphone.SelectedDevice ?? "";
            ProfileMapper.CaptureTracking(p, t.Settings, t.Body, t.Enabled, t.Hands);
            ProfileMapper.CaptureMappings(p, t.Solver.Mapper.Mappings, t.UsesDefaultMappings);
            ProfileMapper.CaptureLipSync(p, t.LipSync);
            ProfileMapper.CaptureCamera(p, _services.Camera.State);
            ProfileMapper.CaptureBackground(p, _services.Background.Settings);
            ProfileMapper.CaptureOutput(p, _services.Render.Settings);
            ProfileMapper.CaptureHotkeys(p, _hotkeys.Bindings);
            return p;
        }

        /// <summary>Pushes a profile into the live services. A missing VRM or image is reported, never dropped (PRD 23.2).</summary>
        private void ApplyProfile(ProfileData p)
        {
            var t = _services.Tracking;
            t.SetEnabled(false);

            _services.Render.SetOutputSettings(ProfileMapper.ToOutput(p));

            ProfileMapper.ApplyCamera(p, _services.Camera.State);
            _services.Camera.Reframe();
            _services.Camera.NotifyStateChanged();

            var bg = _services.Background;
            ProfileMapper.ApplyBackground(p, bg.Settings);
            if (!string.IsNullOrEmpty(bg.Settings.ImagePath) && !bg.TryLoadImage(bg.Settings.ImagePath))
            {
                _profiles.MissingImagePath = bg.Settings.ImagePath;
            }
            else
            {
                _profiles.MissingImagePath = null;
            }
            bg.Apply();

            ProfileMapper.ApplyTracking(p, t.Settings, t.Body, t.Hands);
            t.SetMappings(ProfileMapper.ToMappings(p), p.useDefaultMappings || p.mappings == null || p.mappings.Count == 0);
            ProfileMapper.ApplyLipSync(p, t.LipSync);
            if (!string.IsNullOrEmpty(p.cameraDevice)) _services.Camera2D.Select(p.cameraDevice);
            if (!string.IsNullOrEmpty(p.microphoneDevice)) _services.Microphone.Select(p.microphoneDevice);
            t.NotifySettingsChanged();

            _hotkeys.SetBindings(ProfileMapper.ToHotkeys(p));

            _profiles.MissingVrmPath = null;
            if (!string.IsNullOrEmpty(p.vrmPath))
            {
                if (System.IO.File.Exists(p.vrmPath))
                {
                    if (!_services.Avatars.HasAvatar || _services.Avatars.Current.Info.FilePath != p.vrmPath)
                    {
                        _ = _services.Avatars.LoadAsync(p.vrmPath);
                    }
                }
                else
                {
                    _profiles.MissingVrmPath = p.vrmPath;
                    _services.Avatars.Unload();
                }
            }
            else
            {
                _services.Avatars.Unload();
            }

            if (p.trackingEnabled && t.EngineAvailable) t.SetEnabled(true);
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

            _profiles.LoadStartupProfile();

            // "open with" / command line wins over the profile's remembered avatar.
            var startupPath = ResolveStartupPath();
            if (!string.IsNullOrEmpty(startupPath))
            {
                _ = _services.Avatars.LoadAsync(startupPath);
            }
        }

        private void OnApplicationQuit()
        {
            _profiles?.Flush();
        }

        private void Update()
        {
            // Hotkeys first so their weights overlay this frame's tracking; tracking is applied in Update so
            // UniVRM (LateUpdate) sees this frame's bones and expressions.
            _services.Tracking.SetExpressionOverrides(_hotkeys.Tick(Time.unscaledDeltaTime));
            _services.Tracking.Tick(Time.unscaledDeltaTime, Time.realtimeSinceStartupAsDouble);
            _profiles.Tick();

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
