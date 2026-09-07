using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.App;
using VRMCast.Avatar;
using VRMCast.Core.Backgrounds;
using VRMCast.Core.Camera;
using VRMCast.Core.Localization;
using VRMCast.Core.Rendering;
using VRMCast.Core.Vrm;

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

        // Header
        private readonly DropdownField _language;

        // Model
        private readonly Label _modelName;
        private readonly Label _modelStatus;
        private readonly Button _loadVrm;
        private readonly Button _unloadVrm;
        private readonly VisualElement _importAdvanced;
        private readonly DropdownField _importVersion;
        private readonly Button _retryLoad;

        // Framing
        private readonly DropdownField _framingPreset;
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

        public MainView(VisualElement root, AppServices services)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _loc = services.Localizer;

            _language = Q<DropdownField>("language");

            _modelName = Q<Label>("model-name");
            _modelStatus = Q<Label>("model-status");
            _loadVrm = Q<Button>("load-vrm");
            _unloadVrm = Q<Button>("unload-vrm");
            _importAdvanced = Q<VisualElement>("import-advanced");
            _importVersion = Q<DropdownField>("import-version");
            _retryLoad = Q<Button>("retry-load");

            _framingPreset = Q<DropdownField>("framing-preset");
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

            BindHeader();
            BindModel();
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
            var index = field.choices.IndexOf(value);
            return index < 0 ? 0 : index;
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
                _loc.Language = LanguageOrder[ChoiceIndex(_language, evt.newValue)];
            });
        }

        private void OnLanguageChanged(AppLanguage _)
        {
            ApplyLanguage();
            RefreshAll();
        }

        /// <summary>Writes every static string and rebuilds dropdown choices for the current language.</summary>
        private void ApplyLanguage()
        {
            _language.SetValueWithoutNotify(_loc.Language.NativeName());
            _language.label = _loc["header.language"];
            Q<Label>("header-subtitle").text = _loc["header.subtitle"];
            Q<Label>("header-profile").text = _loc["header.profile"];

            Q<Label>("section-model").text = _loc["section.model"];
            _loadVrm.text = _services.Avatars.IsLoading ? _loc["model.loading"] : _loc["model.load"];
            _unloadVrm.text = _loc["model.unload"];
            Q<Label>("import-title").text = _loc["import.title"];
            _importVersion.label = _loc["import.version"];
            RebuildChoices(_importVersion, Localized(ImportVersionKeys));
            _retryLoad.text = _loc["import.retry"];

            Q<Label>("section-framing").text = _loc["section.framing"];
            _framingPreset.label = _loc["framing.preset"];
            RebuildChoices(_framingPreset, Localized(FramingKeys));
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

            Q<Label>("status-face").text = _loc["status.face"];
            Q<Label>("status-audio").text = _loc["status.audio"];
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
                _importVersion.SetValueWithoutNotify(_importVersion.choices[0]);
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

        // -------------------------------------------------------------- Framing

        private void BindFraming()
        {
            _framingPreset.RegisterValueChangedCallback(evt =>
                _services.Camera.SetPreset(FramingOrder[ChoiceIndex(_framingPreset, evt.newValue)]));

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
            _framingPreset.SetValueWithoutNotify(_framingPreset.choices[IndexOf(FramingOrder, state.Preset)]);
            _fov.SetValueWithoutNotify(state.FovDeg);
            _fovValue.text = $"{state.FovDeg:0}°";
        }

        // ----------------------------------------------------------- Background

        private void BindBackground()
        {
            _backgroundMode.RegisterValueChangedCallback(evt =>
                _services.Background.SetMode(BackgroundOrder[ChoiceIndex(_backgroundMode, evt.newValue)]));

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
                _services.Background.SetImageFit(FitOrder[ChoiceIndex(_imageFit, evt.newValue)]));
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
            _backgroundMode.SetValueWithoutNotify(_backgroundMode.choices[IndexOf(BackgroundOrder, settings.Mode)]);

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
            _imageFit.SetValueWithoutNotify(_imageFit.choices[IndexOf(FitOrder, settings.ImageFit)]);
            RefreshDiagnostics();
        }

        // --------------------------------------------------------------- Output

        private void BindOutput()
        {
            _outputQuality.RegisterValueChangedCallback(evt =>
            {
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
                    _outputQuality.SetValueWithoutNotify(_outputQuality.choices[i]);
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
            _loc.LanguageChanged -= OnLanguageChanged;
            _filePicker.Dispose();
            _previewInput.Dispose();
        }
    }
}
