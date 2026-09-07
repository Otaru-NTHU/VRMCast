using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.Core.Localization;

namespace VRMCast.UI
{
    /// <summary>
    /// Minimal in-app file browser built from UI Toolkit controls. Standalone Unity has no native open-file dialog
    /// without a plugin, and MVP-A deliberately avoids native code; a macOS NSOpenPanel bridge can replace this later
    /// (see Docs/Architecture.md). Hidden files are skipped; the path field accepts a typed or pasted path.
    /// </summary>
    public sealed class FilePickerView : IDisposable
    {
        private sealed class Entry
        {
            public string Path;
            public string Name;
            public bool IsDirectory;
        }

        private readonly Localizer _loc;
        private readonly VisualElement _overlay;
        private readonly Label _title;
        private readonly Label _error;
        private readonly TextField _pathField;
        private readonly ListView _list;
        private readonly Button _upButton;
        private readonly Button _homeButton;
        private readonly Button _desktopButton;
        private readonly Button _downloadsButton;
        private readonly Button _documentsButton;
        private readonly Button _cancelButton;
        private readonly Button _openButton;
        private readonly List<Entry> _entries = new List<Entry>();
        private string _currentDirectory;
        private string _titleKey = "picker.loadVrm";
        private string _errorKey;
        private string[] _extensions = Array.Empty<string>();
        private Action<string> _onPicked;

        public bool IsOpen => _overlay.style.display == DisplayStyle.Flex;

        public FilePickerView(VisualElement root, Localizer localizer)
        {
            _loc = localizer ?? throw new ArgumentNullException(nameof(localizer));

            _overlay = new VisualElement { name = "file-picker-overlay" };
            _overlay.AddToClassList("modal-overlay");
            _overlay.style.display = DisplayStyle.None;
            root.Add(_overlay);

            var dialog = new VisualElement { name = "file-picker" };
            dialog.AddToClassList("modal-dialog");
            _overlay.Add(dialog);

            _title = new Label { name = "file-picker-title" };
            _title.AddToClassList("modal-title");
            dialog.Add(_title);

            var shortcuts = new VisualElement();
            shortcuts.AddToClassList("row");
            _upButton = new Button(() => Navigate(Directory.GetParent(_currentDirectory)?.FullName));
            shortcuts.Add(_upButton);
            _homeButton = AddShortcut(shortcuts, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            _desktopButton = AddShortcut(shortcuts, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            _downloadsButton = AddShortcut(shortcuts, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            _documentsButton = AddShortcut(shortcuts, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            dialog.Add(shortcuts);

            _pathField = new TextField { name = "file-picker-path" };
            _pathField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) OpenTypedPath();
            });
            dialog.Add(_pathField);

            _list = new ListView(_entries, 26, MakeItem, BindItem)
            {
                name = "file-picker-list",
                selectionType = SelectionType.Single,
            };
            _list.selectionChanged += OnSelectionChanged;
            _list.itemsChosen += OnItemsChosen;
            dialog.Add(_list);

            _error = new Label { name = "file-picker-error" };
            _error.AddToClassList("error-text");
            dialog.Add(_error);

            var buttons = new VisualElement();
            buttons.AddToClassList("row");
            buttons.AddToClassList("row-right");
            _cancelButton = new Button(Hide);
            buttons.Add(_cancelButton);
            _openButton = new Button(OpenTypedPath);
            _openButton.AddToClassList("primary");
            buttons.Add(_openButton);
            dialog.Add(buttons);

            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) Hide();
            });

            _loc.LanguageChanged += OnLanguageChanged;
            ApplyLanguage();
        }

        /// <param name="titleKey">Localization key of the dialog title.</param>
        /// <param name="extensions">Lower-case extensions including the dot, e.g. ".vrm". Empty shows every file.</param>
        public void Show(string titleKey, string[] extensions, string initialDirectory, Action<string> onPicked)
        {
            _titleKey = titleKey;
            _title.text = _loc[titleKey];
            _extensions = extensions ?? Array.Empty<string>();
            _onPicked = onPicked;
            SetError(null);
            _overlay.style.display = DisplayStyle.Flex;

            var start = !string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Navigate(start);
            _pathField.Focus();
        }

        public void Hide()
        {
            _overlay.style.display = DisplayStyle.None;
            _onPicked = null;
        }

        private void OnLanguageChanged(AppLanguage _) => ApplyLanguage();

        private void ApplyLanguage()
        {
            _title.text = _loc[_titleKey];
            _upButton.text = _loc["picker.up"];
            _homeButton.text = _loc["picker.home"];
            _desktopButton.text = _loc["picker.desktop"];
            _downloadsButton.text = _loc["picker.downloads"];
            _documentsButton.text = _loc["picker.documents"];
            _cancelButton.text = _loc["picker.cancel"];
            _openButton.text = _loc["picker.open"];
            _error.text = _errorKey == null ? string.Empty : _loc[_errorKey];
        }

        private void SetError(string key)
        {
            _errorKey = key;
            _error.text = key == null ? string.Empty : _loc[key];
        }

        private Button AddShortcut(VisualElement parent, string path)
        {
            var button = new Button(() => Navigate(path));
            button.SetEnabled(!string.IsNullOrEmpty(path) && Directory.Exists(path));
            parent.Add(button);
            return button;
        }

        private static VisualElement MakeItem()
        {
            var label = new Label();
            label.AddToClassList("file-entry");
            return label;
        }

        private void BindItem(VisualElement element, int index)
        {
            var entry = _entries[index];
            var label = (Label)element;
            label.text = entry.IsDirectory ? entry.Name + "/" : entry.Name;
            label.EnableInClassList("file-entry-dir", entry.IsDirectory);
        }

        private void Navigate(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            _currentDirectory = directory;
            _pathField.SetValueWithoutNotify(directory);
            SetError(null);
            _entries.Clear();

            try
            {
                var dirs = Directory.GetDirectories(directory)
                    .Where(d => !Path.GetFileName(d).StartsWith(".", StringComparison.Ordinal))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new Entry { Path = d, Name = Path.GetFileName(d), IsDirectory = true });
                var files = Directory.GetFiles(directory)
                    .Where(f => !Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal) && MatchesFilter(f))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new Entry { Path = f, Name = Path.GetFileName(f), IsDirectory = false });
                _entries.AddRange(dirs);
                _entries.AddRange(files);
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is IOException)
            {
                SetError("picker.folderUnreadable");
            }

            _list.ClearSelection();
            _list.RefreshItems();
            _list.ScrollToItem(0);
        }

        private bool MatchesFilter(string file)
        {
            if (_extensions.Length == 0) return true;
            var ext = Path.GetExtension(file).ToLowerInvariant();
            return _extensions.Contains(ext);
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            var entry = selection.FirstOrDefault() as Entry;
            if (entry != null) _pathField.SetValueWithoutNotify(entry.Path);
        }

        private void OnItemsChosen(IEnumerable<object> chosen)
        {
            var entry = chosen.FirstOrDefault() as Entry;
            if (entry == null) return;
            if (entry.IsDirectory) Navigate(entry.Path);
            else Pick(entry.Path);
        }

        private void OpenTypedPath()
        {
            var path = _pathField.value?.Trim();
            if (string.IsNullOrEmpty(path)) return;
            if (Directory.Exists(path)) { Navigate(path); return; }
            if (!File.Exists(path)) { SetError("picker.fileMissing"); return; }
            if (!MatchesFilter(path)) { SetError("picker.wrongType"); return; }
            Pick(path);
        }

        private void Pick(string path)
        {
            var callback = _onPicked;
            Hide();
            callback?.Invoke(path);
        }

        public void Dispose()
        {
            _loc.LanguageChanged -= OnLanguageChanged;
        }
    }
}
