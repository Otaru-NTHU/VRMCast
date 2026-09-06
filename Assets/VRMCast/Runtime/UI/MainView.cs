using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.App;
using VRMCast.Avatar;
using VRMCast.Core.Backgrounds;
using VRMCast.Core.Camera;
using VRMCast.Core.Rendering;
using VRMCast.Core.Vrm;

namespace VRMCast.UI
{
    /// <summary>
    /// Standard-mode desktop layout (PRD 33, MVP-A subset). Binds the UXML controls to services; it never touches
    /// UniVRM objects and never renders into the OutputRenderTexture — it only displays it in the preview.
    /// </summary>
    public sealed class MainView : IDisposable
    {
        private static readonly string[] VrmExtensions = { ".vrm" };
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
        private const long MessageDisplayMs = 6000;

        private readonly VisualElement _root;
        private readonly AppServices _services;
        private readonly FilePickerView _filePicker;
        private readonly PreviewInput _previewInput;

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
        private IVisualElementScheduledItem _bannerHide;

        public MainView(VisualElement root, AppServices services)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _services = services ?? throw new ArgumentNullException(nameof(services));

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

            _filePicker = new FilePickerView(_root);
            _previewInput = new PreviewInput(_preview, _services.Camera);

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

            RefreshAll();
        }

        private T Q<T>(string name) where T : VisualElement
        {
            var element = _root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"Main.uxml is missing element '{name}' of type {typeof(T).Name}.");
            return element;
        }

        // ---------------------------------------------------------------- Model

        private void BindModel()
        {
            _loadVrm.clicked += () => _filePicker.Show("Load VRM", VrmExtensions, _lastVrmDirectory, path =>
            {
                _lastVrmPath = path;
                _lastVrmDirectory = Path.GetDirectoryName(path);
                _importVersion.SetValueWithoutNotify(_importVersion.choices[0]);
                _importAdvanced.style.display = DisplayStyle.None;
                _ = _services.Avatars.LoadAsync(path);
            });

            _unloadVrm.clicked += () => _services.Avatars.Unload();

            _importVersion.choices = new List<string> { "Auto Detect", "Force VRM 0.x", "Force VRM 1.0" };
            _importVersion.SetValueWithoutNotify(_importVersion.choices[0]);
            _importAdvanced.style.display = DisplayStyle.None;
            _retryLoad.clicked += () =>
            {
                if (string.IsNullOrEmpty(_lastVrmPath)) return;
                var index = Mathf.Max(0, _importVersion.choices.IndexOf(_importVersion.value));
                _ = _services.Avatars.LoadAsync(_lastVrmPath, (VrmVersionOverride)index);
            };
        }

        private void OnAvatarLoaded(LoadedAvatar avatar)
        {
            var info = avatar.Info;
            _modelName.text = info.Title;
            var author = string.IsNullOrEmpty(info.Author) ? string.Empty : $" · {info.Author}";
            _modelStatus.text = $"{info.VersionLabel}{author} · {info.ExpressionCount} expressions · SpringBone {(info.HasSpringBones ? "on" : "none")}";
            _modelStatus.RemoveFromClassList("error-text");
            _importAdvanced.style.display = DisplayStyle.None;
            _unloadVrm.SetEnabled(true);
            ShowMessage($"Loaded {info.FileName}", isError: false);
            RefreshDiagnostics();
        }

        private void OnAvatarUnloaded()
        {
            _modelName.text = "No avatar loaded";
            _modelStatus.text = "Load a .vrm file to begin.";
            _modelStatus.RemoveFromClassList("error-text");
            _unloadVrm.SetEnabled(false);
            RefreshDiagnostics();
        }

        private void OnLoadFailed(string error)
        {
            _modelStatus.text = error;
            _modelStatus.AddToClassList("error-text");
            // Automatic detection failed or the importer rejected the file: offer the advanced override (PRD 5.1).
            _importAdvanced.style.display = string.IsNullOrEmpty(_lastVrmPath) ? DisplayStyle.None : DisplayStyle.Flex;
            ShowMessage(error, isError: true);
        }

        private void OnLoadingStateChanged(bool loading)
        {
            _loadVrm.SetEnabled(!loading);
            _retryLoad.SetEnabled(!loading);
            _loadVrm.text = loading ? "Loading…" : "Load VRM";
            if (loading)
            {
                _modelStatus.text = "Loading…";
                _modelStatus.RemoveFromClassList("error-text");
            }
        }

        // -------------------------------------------------------------- Framing

        private void BindFraming()
        {
            var labels = new List<string>();
            foreach (FramingPreset p in Enum.GetValues(typeof(FramingPreset))) labels.Add(p.Label());
            _framingPreset.choices = labels;
            _framingPreset.RegisterValueChangedCallback(evt =>
            {
                if (FramingPresetExtensions.TryParseLabel(evt.newValue, out var preset)) _services.Camera.SetPreset(preset);
            });

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
            _framingPreset.SetValueWithoutNotify(state.Preset.Label());
            _fov.SetValueWithoutNotify(state.FovDeg);
            _fovValue.text = $"{state.FovDeg:0}°";
        }

        // ----------------------------------------------------------- Background

        private void BindBackground()
        {
            var labels = new List<string>();
            foreach (BackgroundMode m in Enum.GetValues(typeof(BackgroundMode))) labels.Add(m.Label());
            _backgroundMode.choices = labels;
            _backgroundMode.RegisterValueChangedCallback(evt =>
            {
                if (BackgroundModeExtensions.TryParseLabel(evt.newValue, out var mode)) _services.Background.SetMode(mode);
            });

            _colorHex.RegisterCallback<FocusOutEvent>(_ => ApplyColorField());
            _colorHex.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) ApplyColorField();
            });

            Q<Button>("choose-image").clicked += () => _filePicker.Show("Choose background image", ImageExtensions, _lastImageDirectory, path =>
            {
                _lastImageDirectory = Path.GetDirectoryName(path);
                if (!_services.Background.TryLoadImage(path))
                {
                    ShowMessage(_services.Background.LastError ?? "The image could not be loaded.", isError: true);
                }
            });
            Q<Button>("clear-image").clicked += () => _services.Background.ClearImage();

            var fits = new List<string>();
            foreach (ImageFitMode f in Enum.GetValues(typeof(ImageFitMode))) fits.Add(f.Label());
            _imageFit.choices = fits;
            _imageFit.RegisterValueChangedCallback(evt =>
            {
                if (Enum.TryParse<ImageFitMode>(evt.newValue, out var fit)) _services.Background.SetImageFit(fit);
            });
        }

        private void ApplyColorField()
        {
            var settings = _services.Background.Settings;
            if (!RgbaColor.TryParseHex(_colorHex.value, out var color))
            {
                ShowMessage("Enter a color as #RRGGBB.", isError: true);
                RefreshBackgroundControls();
                return;
            }
            if (settings.Mode == BackgroundMode.ChromaKey) _services.Background.SetChromaColor(color);
            else if (settings.Mode == BackgroundMode.SolidColor) _services.Background.SetSolidColor(color);
        }

        private void RefreshBackgroundControls()
        {
            var settings = _services.Background.Settings;
            _backgroundMode.SetValueWithoutNotify(settings.Mode.Label());

            var showColor = settings.Mode == BackgroundMode.SolidColor || settings.Mode == BackgroundMode.ChromaKey;
            _colorRow.style.display = showColor ? DisplayStyle.Flex : DisplayStyle.None;
            if (showColor)
            {
                var color = settings.Mode == BackgroundMode.ChromaKey ? settings.ChromaColor : settings.SolidColor;
                _colorLabel.text = settings.Mode == BackgroundMode.ChromaKey ? "Key color" : "Color";
                _colorHex.SetValueWithoutNotify(color.ToHex());
                _colorSwatch.style.backgroundColor = new Color(color.R, color.G, color.B, 1f);
            }

            _imageRows.style.display = settings.Mode == BackgroundMode.Image ? DisplayStyle.Flex : DisplayStyle.None;
            _imageName.text = string.IsNullOrEmpty(settings.ImagePath) ? "No image selected" : Path.GetFileName(settings.ImagePath);
            _imageFit.SetValueWithoutNotify(settings.ImageFit.Label());
            RefreshDiagnostics();
        }

        // --------------------------------------------------------------- Output

        private void BindOutput()
        {
            var labels = new List<string>();
            foreach (var p in OutputSettings.Presets) labels.Add(p.Label);
            _outputQuality.choices = labels;
            _outputQuality.RegisterValueChangedCallback(evt =>
            {
                if (OutputSettings.TryFindPreset(evt.newValue, out var settings)) _services.Render.SetOutputSettings(settings);
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
            foreach (var p in OutputSettings.Presets)
            {
                if (p.Settings == settings) { _outputQuality.SetValueWithoutNotify(p.Label); break; }
            }
            _statusResolution.text = settings.Label;
            RefreshDiagnostics();
        }

        private void RefreshOutputStatus()
        {
            var running = _services.DebugOutput.IsRunning;
            _toggleOutput.text = running ? "Stop Output" : "Start Output";
            _toggleOutput.EnableInClassList("running", running);
            _statusOutput.text = running ? "Output: on" : "Output: off";
            RefreshDiagnostics();
        }

        // ---------------------------------------------------------- Diagnostics

        private void BindDiagnostics()
        {
            Q<Button>("copy-diagnostics").clicked += () =>
            {
                _services.Diagnostics.CopyReportToClipboard();
                ShowMessage("Diagnostics copied to the clipboard.", isError: false);
            };
        }

        public void RefreshDiagnostics()
        {
            var snap = _services.Diagnostics.Snapshot();
            _diagFps.text = $"Render: {snap.RenderFps:0.0} fps ({snap.FrameTimeMs:0.0} ms)";
            _diagOutput.text = $"Output: {snap.Output.Width}×{snap.Output.Height} @ {snap.Output.Fps} · target {snap.TargetFrameRate}";
            _diagAvatar.text = $"Avatar: {snap.AvatarName} [{snap.AvatarVersion}]";
            _statusRenderFps.text = $"Render {snap.RenderFps:0.0} fps";
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
            if (_services.Avatars.HasAvatar) OnAvatarLoaded(_services.Avatars.Current);
            else OnAvatarUnloaded();
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
            _previewInput.Dispose();
        }
    }
}
