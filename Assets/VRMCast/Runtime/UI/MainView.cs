using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.App;
using VRMCast.Audio;
using VRMCast.Avatar;
using VRMCast.Core.Backgrounds;
using VRMCast.Core.Camera;
using VRMCast.Core.Localization;
using VRMCast.Core.Rendering;
using VRMCast.Core.Tracking;
using VRMCast.Core.Vrm;
using VRMCast.Hotkeys;
using VRMCast.Profiles;
using VRMCast.Tracking;

namespace VRMCast.UI
{
    /// <summary>
    /// Standard-mode desktop layout (PRD 33, MVP-A subset). Binds the UXML controls to services; it never touches
    /// UniVRM objects and never renders into the OutputRenderTexture — it only displays it in the preview.
    /// Every visible string comes from the <see cref="Localizer"/>; dropdowns map by index so labels can change
    /// language without touching the enum mapping.
    /// </summary>
    public sealed class MainView : IDisposable
    {
        private static readonly string[] VrmExtensions = { ".vrm" };
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
        private const long MessageDisplayMs = 6000;

        private static readonly FramingPreset[] FramingOrder = { FramingPreset.Face, FramingPreset.Bust, FramingPreset.HalfBody, FramingPreset.FullBody };
        private static readonly string[] FramingKeys = { "framing.face", "framing.bust", "framing.halfBody", "framing.fullBody" };
        private static readonly BackgroundMode[] BackgroundOrder = { BackgroundMode.SolidColor, BackgroundMode.Image, BackgroundMode.ChromaKey, BackgroundMode.Transparent };
        private static readonly string[] BackgroundKeys = { "background.solid", "background.image", "background.chroma", "background.transparent" };
        private static readonly ImageFitMode[] FitOrder = { ImageFitMode.Fill, ImageFitMode.Fit, ImageFitMode.Stretch };
        private static readonly string[] FitKeys = { "fit.fill", "fit.fit", "fit.stretch" };
        private static readonly string[] ImportVersionKeys = { "import.auto", "import.force0", "import.force1" };
        private static readonly AppLanguage[] LanguageOrder = { AppLanguage.ZhHant, AppLanguage.En };

        private readonly VisualElement _root;
        private readonly AppServices _services;
        private readonly Localizer _loc;
        private readonly FilePickerView _filePicker;
        private readonly PreviewInput _previewInput;

        // Header / profiles / performance mode
        private readonly DropdownField _language;
        private readonly DropdownField _profileSelect;
        private readonly Button _profileManage;
        private readonly Button _performanceMode;
        private readonly Label _perfOverlay;
        private readonly VisualElement _app;
        private ProfileManagerView _profileManager;
        private HotkeysView _hotkeysView;
        private List<string> _profileNames = new List<string>();
        private bool _performance;
        private readonly Slider _trackBlink;
        private readonly Label _trackBlinkValue;

        // Model
        private readonly Label _modelName;
        private readonly Label _modelStatus;
        private readonly Button _loadVrm;
        private readonly Button _unloadVrm;
        private readonly VisualElement _importAdvanced;
        private readonly DropdownField _importVersion;
        private readonly Button _retryLoad;

        // Camera & tracking
        private readonly DropdownField _cameraDevice;
        private readonly Image _cameraPreview;
        private readonly Label _cameraState;
        private readonly Toggle _cameraMirror;
        private readonly Label _trackingSetupHint;
        private readonly Button _trackingToggle;
        private readonly Button _calibrate;
        private readonly Label _trackingStatus;
        private readonly DropdownField _trackingMode;
        private readonly Foldout _trackingAdvanced;
        private readonly Toggle _mirrorUser;
        private readonly Slider _trackSmoothing;
        private readonly Label _trackSmoothingValue;
        private readonly Slider _trackGain;
        private readonly Label _trackGainValue;
        private readonly Toggle _invertPitch;
        private readonly Toggle _invertYaw;
        private readonly Toggle _invertRoll;
        private readonly Label _diagTracking;
        private readonly Label _statusFace;
        private static readonly FaceTrackingMode[] TrackingModeOrder = { FaceTrackingMode.Basic, FaceTrackingMode.Advanced };
        private static readonly string[] TrackingModeKeys = { "tracking.basic", "tracking.advanced" };
        private List<string> _cameraNames = new List<string>();

        // Lip sync & body
        private readonly DropdownField _lipSyncMode;
        private readonly VisualElement _micRows;
        private readonly DropdownField _micDevice;
        private readonly VisualElement _micLevelFill;
        private readonly VisualElement _micGateMark;
        private readonly Label _micState;
        private readonly Slider _micSensitivity;
        private readonly Label _micSensitivityValue;
        private readonly Slider _micGate;
        private readonly Label _micGateValue;
        private readonly DropdownField _bodyMode;
        private readonly Label _bodyHint;
        private readonly Toggle _handsSwap;
        private readonly Label _diagHands;
        private readonly Label _diagPose;
        private readonly Label _statusAudio;
        private static readonly LipSyncMode[] LipSyncOrder = { LipSyncMode.Camera, LipSyncMode.Microphone, LipSyncMode.Hybrid };
        private static readonly string[] LipSyncKeys = { "lipsync.camera", "lipsync.microphone", "lipsync.hybrid" };
        private static readonly BodyTrackingMode[] BodyOrder = { BodyTrackingMode.Off, BodyTrackingMode.UpperBody, BodyTrackingMode.UpperBodyArms, BodyTrackingMode.UpperBodyArmsFingers };
        private static readonly string[] BodyKeys = { "body.off", "body.upper", "body.upperArms", "body.upperArmsFingers" };
        private List<string> _micNames = new List<string>();

        // Framing
        private readonly DropdownField _framingPreset;
        private readonly Slider _zoom;
        private readonly Label _zoomValue;
        private readonly Slider _fov;
        private readonly Label _fovValue;

        // Background
        private readonly DropdownField _backgroundMode;
        private readonly VisualElement _colorRow;
        private readonly Label _colorLabel;
        private readonly TextField _colorHex;
        private readonly VisualElement _colorSwatch;
        private readonly VisualElement _imageRows;
        private readonly Label _imageName;
        private readonly DropdownField _imageFit;

        // Output & diagnostics
        private readonly DropdownField _outputQuality;
        private readonly Label _diagFps;
        private readonly Label _diagOutput;
        private readonly Label _diagAvatar;

        // Preview & status
        private readonly Image _preview;
        private readonly Label _messageBanner;
        private readonly Label _statusResolution;
        private readonly Label _statusRenderFps;
        private readonly Label _statusOutput;
        private readonly Button _toggleOutput;

        private string _lastVrmPath;
        private string _lastVrmDirectory;
        private string _lastImageDirectory;
        private Message _lastLoadError;
        private IVisualElementScheduledItem _bannerHide;

        /// <summary>True while dropdown choices are being rebuilt; Unity raises change events during that, which must be ignored.</summary>
        private bool _rebuildingChoices;

        public MainView(VisualElement root, AppServices services)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _loc = services.Localizer;

            _language = Q<DropdownField>("language");
            _profileSelect = Q<DropdownField>("profile-select");
            _profileManage = Q<Button>("profile-manage");
            _performanceMode = Q<Button>("performance-mode");
            _perfOverlay = Q<Label>("perf-overlay");
            _app = Q<VisualElement>("app");
            _trackBlink = Q<Slider>("track-blink");
            _trackBlinkValue = Q<Label>("track-blink-value");

            _modelName = Q<Label>("model-name");
            _modelStatus = Q<Label>("model-status");
            _loadVrm = Q<Button>("load-vrm");
            _unloadVrm = Q<Button>("unload-vrm");
            _importAdvanced = Q<VisualElement>("import-advanced");
            _importVersion = Q<DropdownField>("import-version");
            _retryLoad = Q<Button>("retry-load");

            _cameraDevice = Q<DropdownField>("camera-device");
            _cameraPreview = Q<Image>("camera-preview");
            _cameraState = Q<Label>("camera-state");
            _cameraMirror = Q<Toggle>("camera-mirror");
            _trackingSetupHint = Q<Label>("tracking-setup-hint");
            _trackingToggle = Q<Button>("tracking-toggle");
            _calibrate = Q<Button>("calibrate");
            _trackingStatus = Q<Label>("tracking-status");
            _trackingMode = Q<DropdownField>("tracking-mode");
            _trackingAdvanced = Q<Foldout>("tracking-advanced");
            _mirrorUser = Q<Toggle>("mirror-user");
            _trackSmoothing = Q<Slider>("track-smoothing");
            _trackSmoothingValue = Q<Label>("track-smoothing-value");
            _trackGain = Q<Slider>("track-gain");
            _trackGainValue = Q<Label>("track-gain-value");
            _invertPitch = Q<Toggle>("invert-pitch");
            _invertYaw = Q<Toggle>("invert-yaw");
            _invertRoll = Q<Toggle>("invert-roll");
            _diagTracking = Q<Label>("diag-tracking");
            _statusFace = Q<Label>("status-face");

            _lipSyncMode = Q<DropdownField>("lipsync-mode");
            _micRows = Q<VisualElement>("mic-rows");
            _micDevice = Q<DropdownField>("mic-device");
            _micLevelFill = Q<VisualElement>("mic-level-fill");
            _micGateMark = Q<VisualElement>("mic-gate-mark");
            _micState = Q<Label>("mic-state");
            _micSensitivity = Q<Slider>("mic-sensitivity");
            _micSensitivityValue = Q<Label>("mic-sensitivity-value");
            _micGate = Q<Slider>("mic-gate");
            _micGateValue = Q<Label>("mic-gate-value");
            _bodyMode = Q<DropdownField>("body-mode");
            _bodyHint = Q<Label>("body-hint");
            _handsSwap = Q<Toggle>("hands-swap");
            _diagHands = Q<Label>("diag-hands");
            _diagPose = Q<Label>("diag-pose");
            _statusAudio = Q<Label>("status-audio");

            _framingPreset = Q<DropdownField>("framing-preset");
            _zoom = Q<Slider>("zoom");
            _zoomValue = Q<Label>("zoom-value");
            _fov = Q<Slider>("fov");
            _fovValue = Q<Label>("fov-value");

            _backgroundMode = Q<DropdownField>("background-mode");
            _colorRow = Q<VisualElement>("color-row");
            _colorLabel = Q<Label>("color-label");
            _colorHex = Q<TextField>("color-hex");
            _colorSwatch = Q<VisualElement>("color-swatch");
            _imageRows = Q<VisualElement>("image-rows");
            _imageName = Q<Label>("image-name");
            _imageFit = Q<DropdownField>("image-fit");

            _outputQuality = Q<DropdownField>("output-quality");
            _diagFps = Q<Label>("diag-fps");
            _diagOutput = Q<Label>("diag-output");
            _diagAvatar = Q<Label>("diag-avatar");

            _preview = Q<Image>("preview");
            _messageBanner = Q<Label>("message-banner");
            _statusResolution = Q<Label>("status-resolution");
            _statusRenderFps = Q<Label>("status-render-fps");
            _statusOutput = Q<Label>("status-output");
            _toggleOutput = Q<Button>("toggle-output");

            _filePicker = new FilePickerView(_root, _loc);
            _previewInput = new PreviewInput(_preview, _services.Camera);
            _profileManager = new ProfileManagerView(_root, _loc, _services.Profiles, () => _loadVrm.Focus());
            _hotkeysView = new HotkeysView(Q<VisualElement>("hotkey-rows"), Q<Label>("hotkey-hint"), _loc, _services.Hotkeys, _services.Avatars);
            _services.Hotkeys.SetTextInputGuard(() => HotkeyService.IsTextFieldFocused(_root) || _filePicker.IsOpen || _profileManager.IsOpen);
            _root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);

            BindHeader();
            BindModel();
            BindCamera();
            BindTracking();
            BindLipSyncAndBody();
            BindFraming();
            BindBackground();
            BindOutput();
            BindDiagnostics();
            BindPreview();

            _services.Avatars.AvatarLoaded += OnAvatarLoaded;
            _services.Avatars.AvatarUnloaded += OnAvatarUnloaded;
            _services.Avatars.LoadFailed += OnLoadFailed;
            _services.Avatars.LoadingStateChanged += OnLoadingStateChanged;
            _services.Render.SettingsChanged += OnOutputSettingsChanged;
            _services.Outputs.OutputsChanged += RefreshOutputStatus;
            _services.Camera.StateChanged += RefreshCameraControls;
            _services.Background.SettingsChanged += RefreshBackgroundControls;
            _services.Camera2D.StateChanged += OnCameraStateChanged;
            _services.Camera2D.DevicesChanged += RefreshWebcamControls;
            _services.Tracking.StatusChanged += OnTrackingStatusChanged;
            _services.Tracking.SettingsChanged += RefreshTrackingControls;
            _services.Tracking.SettingsChanged += RefreshLipSyncControls;
            _services.Tracking.EnabledChanged += _ => RefreshTrackingControls();
            _services.Tracking.CalibrationFinished += OnCalibrationFinished;
            _services.Microphone.StateChanged += _ => RefreshLipSyncControls();
            _services.Profiles.ProfileChanged += OnProfileChanged;
            _services.Profiles.ProfileListChanged += RefreshProfileControls;
            _loc.LanguageChanged += OnLanguageChanged;

            ApplyLanguage();
            RefreshAll();
        }

        private T Q<T>(string name) where T : VisualElement
        {
            var element = _root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"Main.uxml is missing element '{name}' of type {typeof(T).Name}.");
            return element;
        }

        private static int IndexOf<T>(T[] order, T value) where T : struct
        {
            for (var i = 0; i < order.Length; i++) if (order[i].Equals(value)) return i;
            return 0;
        }

        private static int ChoiceIndex(DropdownField field, string value)
        {
            var index = field.choices != null ? field.choices.IndexOf(value) : -1;
            return index < 0 ? 0 : index;
        }

        private static void SetChoice(DropdownField field, int index)
        {
            var choices = field.choices;
            if (choices == null || index < 0 || index >= choices.Count) return;
            field.SetValueWithoutNotify(choices[index]);
        }

        private List<string> Localized(string[] keys)
        {
            var list = new List<string>(keys.Length);
            foreach (var k in keys) list.Add(_loc[k]);
            return list;
        }

        // --------------------------------------------------------------- Header

        private void BindHeader()
        {
            var names = new List<string>();
            foreach (var l in LanguageOrder) names.Add(l.NativeName());
            _language.choices = names;
            _language.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                _loc.Language = LanguageOrder[ChoiceIndex(_language, evt.newValue)];
            });
            _profileSelect.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                var index = ChoiceIndex(_profileSelect, evt.newValue);
                if (index < 0 || index >= _profileNames.Count) return;
                var name = _profileNames[index];
                if (_services.Profiles.Current != null && name == _services.Profiles.Current.name) return;
                _services.Profiles.Save();
                _services.Profiles.Load(name);
            });
            _profileManage.clicked += () => _profileManager.Show();
            _performanceMode.clicked += () => SetPerformanceMode(!_performance);
        }

        private void OnProfileChanged()
        {
            RefreshProfileControls();
            RefreshAll();
            if (!string.IsNullOrEmpty(_services.Profiles.MissingVrmPath))
            {
                ShowMessage(_loc.Format("profile.missingVrm", _services.Profiles.MissingVrmPath), isError: true);
            }
        }

        private void RefreshProfileControls()
        {
            _profileNames = new List<string>(_services.Profiles.ListNames());
            var choices = new List<string>(_profileNames);
            if (choices.Count == 0) choices.Add(_loc["profile.none"]);
            _rebuildingChoices = true;
            try
            {
                _profileSelect.choices = choices;
                var current = _services.Profiles.Current?.name;
                var index = current != null ? _profileNames.IndexOf(current) : -1;
                _profileSelect.SetValueWithoutNotify(choices[index >= 0 ? index : 0]);
            }
            finally
            {
                _rebuildingChoices = false;
            }
        }

        // ------------------------------------------------------ Performance mode

        /// <summary>PRD 4.3: hide every control, keep rendering and output, show an optional status overlay. Tab toggles.</summary>
        public void SetPerformanceMode(bool enabled)
        {
            if (_performance == enabled) return;
            _performance = enabled;
            _app.EnableInClassList("performance", enabled);
            _perfOverlay.style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            _cameraPreview.image = enabled ? null : _services.Camera2D.Texture;
            _performanceMode.text = _loc[enabled ? "view.standard" : "view.performance"];
            if (enabled) ShowMessage(_loc["view.performanceHint"], isError: false);
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (HotkeyService.IsTextFieldFocused(_root)) return;
            if (evt.keyCode == KeyCode.Tab)
            {
                SetPerformanceMode(!_performance);
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape && _performance)
            {
                SetPerformanceMode(false);
                evt.StopPropagation();
            }
        }

        private void RefreshPerfOverlay()
        {
            if (!_performance) return;
            var snap = _services.Diagnostics.Snapshot();
            var tracking = _services.Tracking;
            var face = _loc[tracking.CurrentStatus == TrackingCoordinator.Status.Tracking ? "status.faceOn" : tracking.Enabled ? "status.faceSearching" : "status.faceOff"];
            var audio = _loc[!_services.Microphone.IsRunning ? "status.audioOff" : _services.Microphone.Meter.IsOpen ? "status.audioSpeaking" : "status.audioListening"];
            var output = _loc[_services.DebugOutput.IsRunning ? "status.outputOn" : "status.outputOff"];
            _perfOverlay.text = $"{snap.RenderFps:0} fps · {face} · {audio} · {output} · Tab";
        }

        private void OnLanguageChanged(AppLanguage _)
        {
            ApplyLanguage();
            RefreshAll();
        }

        /// <summary>Writes every static string and rebuilds dropdown choices for the current language.</summary>
        private void ApplyLanguage()
        {
            _rebuildingChoices = true;
            try
            {
                ApplyLanguageCore();
            }
            finally
            {
                _rebuildingChoices = false;
            }
        }

        private void ApplyLanguageCore()
        {
            _language.SetValueWithoutNotify(_loc.Language.NativeName());
            _language.label = _loc["header.language"];
            Q<Label>("header-subtitle").text = _loc["header.subtitle"];
            _profileSelect.label = _loc["header.profile"];
            _profileManage.text = _loc["profile.manage"];
            _performanceMode.text = _loc[_performance ? "view.standard" : "view.performance"];
            Q<Label>("section-hotkeys").text = _loc["section.hotkeys"];
            _trackBlink.label = _loc["tracking.blinkGain"];

            Q<Label>("section-model").text = _loc["section.model"];
            _loadVrm.text = _services.Avatars.IsLoading ? _loc["model.loading"] : _loc["model.load"];
            _unloadVrm.text = _loc["model.unload"];
            Q<Label>("import-title").text = _loc["import.title"];
            _importVersion.label = _loc["import.version"];
            RebuildChoices(_importVersion, Localized(ImportVersionKeys));
            _retryLoad.text = _loc["import.retry"];

            Q<Label>("section-camera").text = _loc["section.camera"];
            _cameraDevice.label = _loc["camera.device"];
            _cameraMirror.label = _loc["camera.mirror"];

            Q<Label>("section-tracking").text = _loc["section.tracking"];
            _calibrate.text = _loc["tracking.calibrate"];
            _trackingMode.label = _loc["tracking.mode"];
            RebuildChoices(_trackingMode, Localized(TrackingModeKeys));
            _trackingAdvanced.text = _loc["tracking.advancedSettings"];
            _mirrorUser.label = _loc["tracking.mirrorUser"];
            _trackSmoothing.label = _loc["tracking.smoothing"];
            _trackGain.label = _loc["tracking.headGain"];
            _invertPitch.label = _loc["tracking.invertPitch"];
            _invertYaw.label = _loc["tracking.invertYaw"];
            _invertRoll.label = _loc["tracking.invertRoll"];
            Q<Button>("clear-calibration").text = _loc["tracking.clearCalibration"];

            Q<Label>("section-lipsync").text = _loc["section.lipsync"];
            _lipSyncMode.label = _loc["lipsync.mode"];
            RebuildChoices(_lipSyncMode, Localized(LipSyncKeys));
            _micDevice.label = _loc["lipsync.microphone"];
            Q<Label>("mic-level-label").text = _loc["lipsync.level"];
            _micSensitivity.label = _loc["lipsync.sensitivity"];
            _micGate.label = _loc["lipsync.gate"];
            Q<Label>("section-body").text = _loc["section.body"];
            _bodyMode.label = _loc["body.mode"];
            RebuildChoices(_bodyMode, Localized(BodyKeys));
            _handsSwap.label = _loc["hands.swap"];

            Q<Label>("section-framing").text = _loc["section.framing"];
            _framingPreset.label = _loc["framing.preset"];
            RebuildChoices(_framingPreset, Localized(FramingKeys));
            _zoom.label = _loc["framing.zoom"];
            _fov.label = _loc["framing.fov"];
            Q<Button>("reset-camera").text = _loc["framing.resetCamera"];
            Q<Button>("reset-orientation").text = _loc["framing.resetOrientation"];
            Q<Button>("reframe").text = _loc["framing.reframe"];
            Q<Label>("framing-hint").text = _loc["framing.hint"];

            Q<Label>("section-background").text = _loc["section.background"];
            _backgroundMode.label = _loc["background.mode"];
            RebuildChoices(_backgroundMode, Localized(BackgroundKeys));
            Q<Button>("choose-image").text = _loc["background.chooseImage"];
            Q<Button>("clear-image").text = _loc["background.clearImage"];
            _imageFit.label = _loc["background.fit"];
            RebuildChoices(_imageFit, Localized(FitKeys));

            Q<Label>("section-output").text = _loc["section.output"];
            _outputQuality.label = _loc["output.quality"];
            RebuildChoices(_outputQuality, OutputPresetLabels());
            Q<Label>("output-hint").text = _loc["output.hint"];

            Q<Label>("section-diagnostics").text = _loc["section.diagnostics"];
            Q<Button>("copy-diagnostics").text = _loc["diag.copy"];

        }

        private static void RebuildChoices(DropdownField field, List<string> choices)
        {
            var index = field.choices != null ? field.choices.IndexOf(field.value) : -1;
            field.choices = choices;
            if (index >= 0 && index < choices.Count) field.SetValueWithoutNotify(choices[index]);
        }

        private List<string> OutputPresetLabels()
        {
            var labels = new List<string>();
            foreach (var p in OutputSettings.Presets)
            {
                var label = $"{p.Settings.Height}p {p.Settings.Fps}";
                if (p.Settings == OutputSettings.Default) label += " — " + _loc["output.recommended"];
                labels.Add(label);
            }
            return labels;
        }

        // ---------------------------------------------------------------- Model

        private void BindModel()
        {
            _loadVrm.clicked += () => _filePicker.Show("picker.loadVrm", VrmExtensions, _lastVrmDirectory, path =>
            {
                _lastVrmPath = path;
                _lastVrmDirectory = Path.GetDirectoryName(path);
                SetChoice(_importVersion, 0);
                _importAdvanced.style.display = DisplayStyle.None;
                _ = _services.Avatars.LoadAsync(path);
            });

            _unloadVrm.clicked += () => _services.Avatars.Unload();

            _importAdvanced.style.display = DisplayStyle.None;
            _retryLoad.clicked += () =>
            {
                if (string.IsNullOrEmpty(_lastVrmPath)) return;
                var index = ChoiceIndex(_importVersion, _importVersion.value);
                _ = _services.Avatars.LoadAsync(_lastVrmPath, (VrmVersionOverride)index);
            };
        }

        private void OnAvatarLoaded(LoadedAvatar avatar)
        {
            _lastLoadError = default;
            RefreshModel();
            _importAdvanced.style.display = DisplayStyle.None;
            ShowMessage(_loc.Format("model.loaded", avatar.Info.FileName), isError: false);
            RefreshDiagnostics();
        }

        private void OnAvatarUnloaded()
        {
            _lastLoadError = default;
            RefreshModel();
            RefreshDiagnostics();
        }

        private void OnLoadFailed(Message error)
        {
            _lastLoadError = error;
            RefreshModel();
            // Automatic detection failed or the importer rejected the file: offer the advanced override (PRD 5.1).
            _importAdvanced.style.display = string.IsNullOrEmpty(_lastVrmPath) ? DisplayStyle.None : DisplayStyle.Flex;
            ShowMessage(_loc.Translate(error), isError: true);
        }

        private void OnLoadingStateChanged(bool loading)
        {
            _loadVrm.SetEnabled(!loading);
            _retryLoad.SetEnabled(!loading);
            _loadVrm.text = loading ? _loc["model.loading"] : _loc["model.load"];
            if (loading)
            {
                _modelStatus.text = _loc["model.loading"];
                _modelStatus.RemoveFromClassList("error-text");
            }
        }

        private void RefreshModel()
        {
            if (_services.Avatars.HasAvatar)
            {
                var info = _services.Avatars.Current.Info;
                _modelName.text = info.Title;
                var author = string.IsNullOrEmpty(info.Author) ? string.Empty : _loc.Format("model.author", info.Author);
                _modelStatus.text = _loc.Format("model.status", _loc[info.Version.LabelKey()], author, info.ExpressionCount,
                    _loc[info.HasSpringBones ? "model.springOn" : "model.springOff"]);
                _modelStatus.RemoveFromClassList("error-text");
                _unloadVrm.SetEnabled(true);
                return;
            }

            _modelName.text = _loc["model.none"];
            _unloadVrm.SetEnabled(false);
            if (!_lastLoadError.IsEmpty)
            {
                _modelStatus.text = _loc.Translate(_lastLoadError);
                _modelStatus.AddToClassList("error-text");
            }
            else
            {
                _modelStatus.text = _loc["model.hint"];
                _modelStatus.RemoveFromClassList("error-text");
            }
        }

        // --------------------------------------------------------------- Camera

        private void BindCamera()
        {
            _cameraDevice.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                var index = ChoiceIndex(_cameraDevice, evt.newValue);
                if (index >= 0 && index < _cameraNames.Count) _services.Camera2D.Select(_cameraNames[index]);
            });
            Q<Button>("camera-refresh").clicked += () => _services.Camera2D.RefreshDevices();
            _cameraMirror.RegisterValueChangedCallback(evt =>
            {
                _services.Camera2D.MirrorPreview = evt.newValue;
                _cameraPreview.EnableInClassList("mirrored", evt.newValue);
            });
            _cameraPreview.scaleMode = ScaleMode.ScaleToFit;
            _cameraPreview.EnableInClassList("mirrored", _services.Camera2D.MirrorPreview);
            _cameraMirror.SetValueWithoutNotify(_services.Camera2D.MirrorPreview);
        }

        private void OnCameraStateChanged(CameraCaptureService.State state)
        {
            RefreshWebcamControls();
            RefreshTrackingControls();
        }

        private void RefreshWebcamControls()
        {
            var camera = _services.Camera2D;
            _cameraNames = new List<string>(camera.Devices);
            var choices = new List<string>(_cameraNames);
            if (choices.Count == 0) choices.Add(_loc["camera.none"]);

            _rebuildingChoices = true;
            try
            {
                _cameraDevice.choices = choices;
                var selected = _cameraNames.IndexOf(camera.SelectedDevice ?? string.Empty);
                _cameraDevice.SetValueWithoutNotify(choices[selected >= 0 ? selected : 0]);
            }
            finally
            {
                _rebuildingChoices = false;
            }
            _cameraDevice.SetEnabled(_cameraNames.Count > 0);

            _cameraPreview.image = _performance ? null : camera.Texture;
            _cameraState.text = _loc[CameraStateKey(camera.CurrentState)];
        }

        private static string CameraStateKey(CameraCaptureService.State state)
        {
            switch (state)
            {
                case CameraCaptureService.State.Stopped: return "camera.state.stopped";
                case CameraCaptureService.State.RequestingPermission: return "camera.state.permission";
                case CameraCaptureService.State.PermissionDenied: return "camera.state.denied";
                case CameraCaptureService.State.Starting: return "camera.state.starting";
                case CameraCaptureService.State.Running: return "camera.state.running";
                case CameraCaptureService.State.Stalled: return "camera.state.stalled";
                case CameraCaptureService.State.NoDevice: return "camera.state.noDevice";
                default: return "camera.state.failed";
            }
        }

        // ------------------------------------------------------------- Tracking

        private void BindTracking()
        {
            var tracking = _services.Tracking;
            _trackingToggle.clicked += () => tracking.SetEnabled(!tracking.Enabled);
            _calibrate.clicked += () =>
            {
                if (tracking.StartCalibration()) ShowMessage(_loc["tracking.calibrateHint"], isError: false);
            };
            _trackingMode.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                tracking.SetMode(TrackingModeOrder[ChoiceIndex(_trackingMode, evt.newValue)]);
            });
            _mirrorUser.RegisterValueChangedCallback(evt => { tracking.Settings.MirrorUser = evt.newValue; tracking.NotifySettingsChanged(); });
            _handsSwap.RegisterValueChangedCallback(evt => { tracking.Hands.SwapHands = evt.newValue; tracking.NotifySettingsChanged(); });
            _trackSmoothing.RegisterValueChangedCallback(evt =>
            {
                tracking.Settings.HeadSmoothing = evt.newValue;
                tracking.Settings.ExpressionSmoothing = evt.newValue * 0.8f;
                tracking.Settings.LookSmoothing = evt.newValue;
                _trackSmoothingValue.text = $"{evt.newValue:0.00}";
                tracking.NotifySettingsChanged();
            });
            _trackGain.RegisterValueChangedCallback(evt =>
            {
                tracking.Settings.HeadGain = evt.newValue;
                _trackGainValue.text = $"{evt.newValue:0.0}×";
                tracking.NotifySettingsChanged();
            });
            _trackBlink.RegisterValueChangedCallback(evt =>
            {
                tracking.Settings.BlinkGain = evt.newValue;
                _trackBlinkValue.text = $"{evt.newValue:0.0}×";
                tracking.NotifySettingsChanged();
            });
            _invertPitch.RegisterValueChangedCallback(evt => { tracking.Settings.InvertPitch = evt.newValue; tracking.NotifySettingsChanged(); });
            _invertYaw.RegisterValueChangedCallback(evt => { tracking.Settings.InvertYaw = evt.newValue; tracking.NotifySettingsChanged(); });
            _invertRoll.RegisterValueChangedCallback(evt => { tracking.Settings.InvertRoll = evt.newValue; tracking.NotifySettingsChanged(); });
            Q<Button>("clear-calibration").clicked += () =>
            {
                tracking.ClearCalibration();
                ShowMessage(_loc["tracking.calibrationCleared"], isError: false);
            };
        }

        private bool _calibrationHintShown;

        private void OnTrackingStatusChanged(TrackingCoordinator.Status status)
        {
            RefreshTrackingControls();
            var calibrated = _services.Tracking.Settings.Calibration != null && _services.Tracking.Settings.Calibration.IsCalibrated;
            if (status == TrackingCoordinator.Status.Tracking && !calibrated && !_calibrationHintShown)
            {
                _calibrationHintShown = true;
                ShowMessage(_loc["tracking.pleaseCalibrate"], isError: false);
            }
        }

        private void OnCalibrationFinished(bool ok)
        {
            ShowMessage(_loc[ok ? "tracking.calibrated" : "tracking.calibrationFailed"], isError: !ok);
            RefreshTrackingControls();
        }

        private void RefreshTrackingControls()
        {
            var tracking = _services.Tracking;
            var settings = tracking.Settings;
            var engine = tracking.EngineAvailable && tracking.ModelAvailable;

            _trackingSetupHint.style.display = engine ? DisplayStyle.None : DisplayStyle.Flex;
            _trackingSetupHint.text = _loc[tracking.EngineAvailable ? "tracking.noModel" : "tracking.noEngine"];
            _trackingToggle.SetEnabled(engine);
            _trackingToggle.text = _loc[tracking.Enabled ? "tracking.stop" : "tracking.start"];
            _trackingToggle.EnableInClassList("running", tracking.Enabled);
            _calibrate.SetEnabled(tracking.Enabled && (tracking.CurrentStatus == TrackingCoordinator.Status.Tracking || tracking.CurrentStatus == TrackingCoordinator.Status.Searching));

            var statusKey = TrackingStatusKey(tracking.CurrentStatus);
            var statusText = _loc[statusKey];
            if (tracking.CurrentStatus == TrackingCoordinator.Status.Calibrating) statusText += $" {tracking.CalibrationProgress * 100f:0}%";
            if (settings.Calibration != null && settings.Calibration.IsCalibrated) statusText += " · " + _loc["tracking.calibratedTag"];
            _trackingStatus.text = statusText;
            _trackingStatus.EnableInClassList("tracking", tracking.CurrentStatus == TrackingCoordinator.Status.Tracking);
            _trackingStatus.EnableInClassList("problem", tracking.CurrentStatus == TrackingCoordinator.Status.CameraError || tracking.CurrentStatus == TrackingCoordinator.Status.NoEngine || tracking.CurrentStatus == TrackingCoordinator.Status.NoModel);

            SetChoice(_trackingMode, IndexOf(TrackingModeOrder, settings.Mode));
            _mirrorUser.SetValueWithoutNotify(settings.MirrorUser);
            _trackSmoothing.SetValueWithoutNotify(settings.HeadSmoothing);
            _trackSmoothingValue.text = $"{settings.HeadSmoothing:0.00}";
            _trackGain.SetValueWithoutNotify(settings.HeadGain);
            _trackGainValue.text = $"{settings.HeadGain:0.0}×";
            _trackBlink.SetValueWithoutNotify(settings.BlinkGain);
            _trackBlinkValue.text = $"{settings.BlinkGain:0.0}×";
            _invertPitch.SetValueWithoutNotify(settings.InvertPitch);
            _invertYaw.SetValueWithoutNotify(settings.InvertYaw);
            _invertRoll.SetValueWithoutNotify(settings.InvertRoll);

            _statusFace.text = _loc[tracking.CurrentStatus == TrackingCoordinator.Status.Tracking ? "status.faceOn"
                : tracking.Enabled ? "status.faceSearching" : "status.faceOff"];
            _statusFace.EnableInClassList("status-dim", tracking.CurrentStatus != TrackingCoordinator.Status.Tracking);
        }

        private static string TrackingStatusKey(TrackingCoordinator.Status status)
        {
            switch (status)
            {
                case TrackingCoordinator.Status.Off: return "tracking.status.off";
                case TrackingCoordinator.Status.NoEngine: return "tracking.status.noEngine";
                case TrackingCoordinator.Status.NoModel: return "tracking.status.noModel";
                case TrackingCoordinator.Status.Starting: return "tracking.status.starting";
                case TrackingCoordinator.Status.Searching: return "tracking.status.searching";
                case TrackingCoordinator.Status.Tracking: return "tracking.status.tracking";
                case TrackingCoordinator.Status.CameraError: return "tracking.status.cameraError";
                case TrackingCoordinator.Status.Calibrating: return "tracking.status.calibrating";
                default: return "tracking.status.off";
            }
        }

        // ------------------------------------------------------ Lip sync & body

        private void BindLipSyncAndBody()
        {
            var tracking = _services.Tracking;
            var mic = _services.Microphone;
            _lipSyncMode.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                tracking.SetLipSyncMode(LipSyncOrder[ChoiceIndex(_lipSyncMode, evt.newValue)]);
            });
            _micDevice.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                var index = ChoiceIndex(_micDevice, evt.newValue);
                if (index >= 0 && index < _micNames.Count) { mic.Select(_micNames[index]); tracking.NotifySettingsChanged(); }
            });
            Q<Button>("mic-refresh").clicked += RefreshLipSyncControls;
            _micSensitivity.RegisterValueChangedCallback(evt =>
            {
                tracking.LipSync.Audio.Sensitivity = evt.newValue;
                _micSensitivityValue.text = $"{evt.newValue:0.0}×";
                tracking.NotifySettingsChanged();
            });
            _micGate.RegisterValueChangedCallback(evt =>
            {
                tracking.LipSync.Audio.GateDb = evt.newValue;
                _micGateValue.text = $"{evt.newValue:0} dB";
                PositionGateMark();
                tracking.NotifySettingsChanged();
            });
            _bodyMode.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                tracking.SetBodyMode(BodyOrder[ChoiceIndex(_bodyMode, evt.newValue)]);
                RefreshLipSyncControls();
            });
        }

        private void RefreshLipSyncControls()
        {
            var tracking = _services.Tracking;
            var mic = _services.Microphone;
            var settings = tracking.LipSync;

            SetChoice(_lipSyncMode, IndexOf(LipSyncOrder, settings.Mode));
            _micRows.style.display = settings.Mode == LipSyncMode.Camera ? DisplayStyle.None : DisplayStyle.Flex;

            _micNames = new List<string>(mic.Devices);
            var choices = new List<string>(_micNames);
            if (choices.Count == 0) choices.Add(_loc["lipsync.noMicrophone"]);
            _rebuildingChoices = true;
            try
            {
                _micDevice.choices = choices;
                var selected = _micNames.IndexOf(mic.SelectedDevice ?? string.Empty);
                _micDevice.SetValueWithoutNotify(choices[selected >= 0 ? selected : 0]);
            }
            finally
            {
                _rebuildingChoices = false;
            }
            _micDevice.SetEnabled(_micNames.Count > 0);

            _micSensitivity.SetValueWithoutNotify(settings.Audio.Sensitivity);
            _micSensitivityValue.text = $"{settings.Audio.Sensitivity:0.0}×";
            _micGate.SetValueWithoutNotify(settings.Audio.GateDb);
            _micGateValue.text = $"{settings.Audio.GateDb:0} dB";
            PositionGateMark();
            _micState.text = _loc[MicrophoneStateKey(mic.CurrentState)];

            SetChoice(_bodyMode, IndexOf(BodyOrder, tracking.Body.Mode));
            var poseOk = tracking.PoseEngineAvailable;
            _bodyMode.SetEnabled(poseOk);
            _bodyHint.text = poseOk ? _loc["body.hint"] : _loc["body.noEngine"];
            _handsSwap.SetValueWithoutNotify(tracking.Hands.SwapHands);
            _handsSwap.style.display = tracking.Body.ArmsEnabled && poseOk ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void PositionGateMark()
        {
            var a = _services.Tracking.LipSync.Audio;
            var t = (a.GateDb - a.FloorDb) / (a.CeilingDb - a.FloorDb);
            _micGateMark.style.left = new Length(Mathf.Clamp01(t) * 100f, LengthUnit.Percent);
        }

        private static string MicrophoneStateKey(MicrophoneCaptureService.State state)
        {
            switch (state)
            {
                case MicrophoneCaptureService.State.Stopped: return "mic.state.stopped";
                case MicrophoneCaptureService.State.RequestingPermission: return "mic.state.permission";
                case MicrophoneCaptureService.State.PermissionDenied: return "mic.state.denied";
                case MicrophoneCaptureService.State.Starting: return "mic.state.starting";
                case MicrophoneCaptureService.State.Running: return "mic.state.running";
                case MicrophoneCaptureService.State.NoDevice: return "mic.state.noDevice";
                default: return "mic.state.failed";
            }
        }

        /// <summary>Per-frame updates that are too fast for the diagnostics window: the microphone level bar and the audio status.</summary>
        public void TickFast()
        {
            if (_performance) return;
            var mic = _services.Microphone;
            var meter = mic.Meter;
            var running = mic.IsRunning;
            var level = running ? meter.Envelope : 0f;
            var raw = running ? Mathf.Clamp01((meter.RawDb - meter.Settings.FloorDb) / (meter.Settings.CeilingDb - meter.Settings.FloorDb)) : 0f;
            _micLevelFill.style.width = new Length(Mathf.Max(level, raw * 0.5f) * 100f, LengthUnit.Percent);
            _micLevelFill.EnableInClassList("silent", !meter.IsOpen);

            var key = !running ? "status.audioOff" : meter.IsOpen ? "status.audioSpeaking" : "status.audioListening";
            var text = _loc[key];
            if (_statusAudio.text != text)
            {
                _statusAudio.text = text;
                _statusAudio.EnableInClassList("status-dim", !running || !meter.IsOpen);
            }
        }

        // -------------------------------------------------------------- Framing

        private void BindFraming()
        {
            _framingPreset.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                _services.Camera.SetPreset(FramingOrder[ChoiceIndex(_framingPreset, evt.newValue)]);
            });

            _zoom.lowValue = AvatarCameraState.MinZoom;
            _zoom.highValue = AvatarCameraState.MaxZoom;
            _zoom.RegisterValueChangedCallback(evt => _services.Camera.SetZoom(evt.newValue));
            _fov.lowValue = AvatarCameraState.MinFovDeg;
            _fov.highValue = AvatarCameraState.MaxFovDeg;
            _fov.RegisterValueChangedCallback(evt => _services.Camera.SetFov(evt.newValue));

            Q<Button>("reset-camera").clicked += () => _services.Camera.ResetCamera();
            Q<Button>("reset-orientation").clicked += () => _services.Camera.ResetOrientation();
            Q<Button>("reframe").clicked += () => _services.Camera.Reframe();
        }

        private void RefreshCameraControls()
        {
            var state = _services.Camera.State;
            SetChoice(_framingPreset, IndexOf(FramingOrder, state.Preset));
            _zoom.SetValueWithoutNotify(state.Zoom);
            _zoomValue.text = $"{state.Zoom:0.0}×";
            _fov.SetValueWithoutNotify(state.FovDeg);
            _fovValue.text = $"{state.FovDeg:0}°";
        }

        // ----------------------------------------------------------- Background

        private void BindBackground()
        {
            _backgroundMode.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                _services.Background.SetMode(BackgroundOrder[ChoiceIndex(_backgroundMode, evt.newValue)]);
            });

            _colorHex.RegisterCallback<FocusOutEvent>(_ => ApplyColorField());
            _colorHex.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) ApplyColorField();
            });

            Q<Button>("choose-image").clicked += () => _filePicker.Show("picker.chooseImage", ImageExtensions, _lastImageDirectory, path =>
            {
                _lastImageDirectory = Path.GetDirectoryName(path);
                if (!_services.Background.TryLoadImage(path))
                {
                    ShowMessage(_loc[_services.Background.LastError ?? "error.imageLoad"], isError: true);
                }
            });
            Q<Button>("clear-image").clicked += () => _services.Background.ClearImage();

            _imageFit.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                _services.Background.SetImageFit(FitOrder[ChoiceIndex(_imageFit, evt.newValue)]);
            });
        }

        private void ApplyColorField()
        {
            var settings = _services.Background.Settings;
            if (!RgbaColor.TryParseHex(_colorHex.value, out var color))
            {
                ShowMessage(_loc["background.colorFormat"], isError: true);
                RefreshBackgroundControls();
                return;
            }
            if (settings.Mode == BackgroundMode.ChromaKey) _services.Background.SetChromaColor(color);
            else if (settings.Mode == BackgroundMode.SolidColor) _services.Background.SetSolidColor(color);
        }

        private void RefreshBackgroundControls()
        {
            var settings = _services.Background.Settings;
            SetChoice(_backgroundMode, IndexOf(BackgroundOrder, settings.Mode));

            var showColor = settings.Mode == BackgroundMode.SolidColor || settings.Mode == BackgroundMode.ChromaKey;
            _colorRow.style.display = showColor ? DisplayStyle.Flex : DisplayStyle.None;
            if (showColor)
            {
                var color = settings.Mode == BackgroundMode.ChromaKey ? settings.ChromaColor : settings.SolidColor;
                _colorLabel.text = _loc[settings.Mode == BackgroundMode.ChromaKey ? "background.keyColor" : "background.color"];
                _colorHex.SetValueWithoutNotify(color.ToHex());
                _colorSwatch.style.backgroundColor = new Color(color.R, color.G, color.B, 1f);
            }

            _imageRows.style.display = settings.Mode == BackgroundMode.Image ? DisplayStyle.Flex : DisplayStyle.None;
            _imageName.text = string.IsNullOrEmpty(settings.ImagePath) ? _loc["background.noImage"] : Path.GetFileName(settings.ImagePath);
            SetChoice(_imageFit, IndexOf(FitOrder, settings.ImageFit));
            RefreshDiagnostics();
        }

        // --------------------------------------------------------------- Output

        private void BindOutput()
        {
            _outputQuality.RegisterValueChangedCallback(evt =>
            {
                if (_rebuildingChoices) return;
                var index = ChoiceIndex(_outputQuality, evt.newValue);
                if (index < OutputSettings.Presets.Count) _services.Render.SetOutputSettings(OutputSettings.Presets[index].Settings);
            });

            _toggleOutput.clicked += () =>
            {
                var output = _services.DebugOutput;
                if (output.IsRunning) _services.Outputs.Stop(output);
                else _services.Outputs.Start(output);
            };
        }

        private void OnOutputSettingsChanged(OutputSettings settings)
        {
            _preview.image = _services.Render.OutputTexture;
            for (var i = 0; i < OutputSettings.Presets.Count; i++)
            {
                if (OutputSettings.Presets[i].Settings == settings)
                {
                    SetChoice(_outputQuality, i);
                    break;
                }
            }
            _statusResolution.text = _loc.Format("status.resolution", settings.Width, settings.Height, settings.Fps);
            RefreshDiagnostics();
        }

        private void RefreshOutputStatus()
        {
            var running = _services.DebugOutput.IsRunning;
            _toggleOutput.text = _loc[running ? "output.stop" : "output.start"];
            _toggleOutput.EnableInClassList("running", running);
            _statusOutput.text = _loc[running ? "status.outputOn" : "status.outputOff"];
            RefreshDiagnostics();
        }

        // ---------------------------------------------------------- Diagnostics

        private void BindDiagnostics()
        {
            Q<Button>("copy-diagnostics").clicked += () =>
            {
                _services.Diagnostics.CopyReportToClipboard();
                ShowMessage(_loc["diag.copied"], isError: false);
            };
        }

        public void RefreshDiagnostics()
        {
            var snap = _services.Diagnostics.Snapshot();
            var avatarVersion = _services.Avatars.HasAvatar ? _loc[_services.Avatars.Current.Info.Version.LabelKey()] : "-";
            _diagFps.text = _loc.Format("diag.render", snap.RenderFps.ToString("0.0"), snap.FrameTimeMs.ToString("0.0"));
            _diagOutput.text = _loc.Format("diag.output", snap.Output.Width, snap.Output.Height, snap.Output.Fps, snap.TargetFrameRate);
            _diagAvatar.text = _loc.Format("diag.avatar", snap.AvatarName, avatarVersion);
            _statusRenderFps.text = _loc.Format("status.render", snap.RenderFps.ToString("0.0"));
            _diagTracking.text = _loc.Format("diag.tracking", snap.TrackingFps.ToString("0.0"), snap.InferenceMs.ToString("0"), snap.TrackingDropped);
            _diagPose.text = _loc.Format("diag.pose", snap.PoseFps.ToString("0.0"), snap.PoseInferenceMs.ToString("0"), snap.MicrophoneDb.ToString("0"));
            _diagHands.text = _loc.Format("diag.hands", snap.HandFps.ToString("0.0"), snap.HandInferenceMs.ToString("0"));
            if (_services.Tracking.CurrentStatus == TrackingCoordinator.Status.Calibrating) RefreshTrackingControls();
            RefreshPerfOverlay();
        }

        // -------------------------------------------------------------- Preview

        private void BindPreview()
        {
            _preview.scaleMode = ScaleMode.ScaleToFit;
            _preview.image = _services.Render.OutputTexture;
            _messageBanner.style.display = DisplayStyle.None;
        }

        private void ShowMessage(string text, bool isError)
        {
            _messageBanner.text = text;
            _messageBanner.EnableInClassList("error", isError);
            _messageBanner.style.display = DisplayStyle.Flex;
            _bannerHide?.Pause();
            _bannerHide = _messageBanner.schedule.Execute(() => _messageBanner.style.display = DisplayStyle.None).StartingIn(MessageDisplayMs);
        }

        private void RefreshAll()
        {
            RefreshModel();
            OnLoadingStateChanged(_services.Avatars.IsLoading);
            RefreshCameraControls();
            RefreshBackgroundControls();
            OnOutputSettingsChanged(_services.Render.Settings);
            RefreshOutputStatus();
            RefreshDiagnostics();
        }

        public void Dispose()
        {
            _services.Avatars.AvatarLoaded -= OnAvatarLoaded;
            _services.Avatars.AvatarUnloaded -= OnAvatarUnloaded;
            _services.Avatars.LoadFailed -= OnLoadFailed;
            _services.Avatars.LoadingStateChanged -= OnLoadingStateChanged;
            _services.Render.SettingsChanged -= OnOutputSettingsChanged;
            _services.Outputs.OutputsChanged -= RefreshOutputStatus;
            _services.Camera.StateChanged -= RefreshCameraControls;
            _services.Background.SettingsChanged -= RefreshBackgroundControls;
            _services.Camera2D.StateChanged -= OnCameraStateChanged;
            _services.Camera2D.DevicesChanged -= RefreshWebcamControls;
            _services.Tracking.StatusChanged -= OnTrackingStatusChanged;
            _services.Tracking.SettingsChanged -= RefreshTrackingControls;
            _services.Tracking.SettingsChanged -= RefreshLipSyncControls;
            _services.Tracking.CalibrationFinished -= OnCalibrationFinished;
            _loc.LanguageChanged -= OnLanguageChanged;
            _services.Profiles.ProfileChanged -= OnProfileChanged;
            _services.Profiles.ProfileListChanged -= RefreshProfileControls;
            _root.UnregisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            _profileManager.Dispose();
            _hotkeysView.Dispose();
            _filePicker.Dispose();
            _previewInput.Dispose();
        }
    }
}
