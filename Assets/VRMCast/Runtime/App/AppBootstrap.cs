using System;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.Avatar;
using VRMCast.Backgrounds;
using VRMCast.CameraControl;
using VRMCast.Core.Backgrounds;
using VRMCast.Core.Camera;
using VRMCast.Core.Rendering;
using VRMCast.Diagnostics;
using VRMCast.Output;
using VRMCast.Rendering;
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

            _services = new AppServices(render, avatars, background, camera, outputs, preview, debugOutput, diagnostics);

            if (_uiDocument != null)
            {
                _view = new MainView(_uiDocument.rootVisualElement, _services);
            }
        }

        private void Start()
        {
            _services.Camera.Reframe();
            _services.Camera.Apply(force: true);

            var startupPath = ResolveStartupPath();
            if (!string.IsNullOrEmpty(startupPath))
            {
                _ = _services.Avatars.LoadAsync(startupPath);
            }
        }

        private void Update()
        {
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
