using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace VRMCast.UI
{
    /// <summary>
    /// Minimal in-app file browser built from UI Toolkit controls. Standalone Unity has no native open-file dialog
    /// without a plugin, and MVP-A deliberately avoids native code; a macOS NSOpenPanel bridge can replace this later
    /// (see Docs/Architecture.md). Hidden files are skipped; the path field accepts a typed or pasted path.
    /// </summary>
    public sealed class FilePickerView
    {
        private sealed class Entry
        {
            public string Path;
            public string Name;
            public bool IsDirectory;
        }

        private readonly VisualElement _overlay;
        private readonly Label _title;
        private readonly Label _error;
        private readonly TextField _pathField;
        private readonly ListView _list;
        private readonly Button _openButton;
        private readonly List<Entry> _entries = new List<Entry>();
        private string _currentDirectory;
        private string[] _extensions = Array.Empty<string>();
        private Action<string> _onPicked;

        public bool IsOpen => _overlay.style.display == DisplayStyle.Flex;

        public FilePickerView(VisualElement root)
        {
            _overlay = new VisualElement { name = "file-picker-overlay" };
            _overlay.AddToClassList("modal-overlay");
            _overlay.style.display = DisplayStyle.None;
            root.Add(_overlay);

            var dialog = new VisualElement { name = "file-picker" };
            dialog.AddToClassList("modal-dialog");
            _overlay.Add(dialog);

            _title = new Label("Open") { name = "file-picker-title" };
            _title.AddToClassList("modal-title");
            dialog.Add(_title);

            var shortcuts = new VisualElement();
            shortcuts.AddToClassList("row");
            shortcuts.Add(new Button(() => Navigate(Directory.GetParent(_currentDirectory)?.FullName)) { text = "Up" });
            AddShortcut(shortcuts, "Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddShortcut(shortcuts, "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            AddShortcut(shortcuts, "Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            AddShortcut(shortcuts, "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            dialog.Add(shortcuts);

            _pathField = new TextField { name = "file-picker-path" };
            _pathField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) OpenTypedPath();
            });
            dialog.Add(_pathField);

            _list = new ListView(_entries, 22, MakeItem, BindItem)
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
            buttons.Add(new Button(Hide) { text = "Cancel" });
            _openButton = new Button(OpenTypedPath) { text = "Open" };
            _openButton.AddToClassList("primary");
            buttons.Add(_openButton);
            dialog.Add(buttons);

            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) Hide();
            });
        }

        /// <param name="extensions">Lower-case extensions including the dot, e.g. ".vrm". Empty shows every file.</param>
        public void Show(string title, string[] extensions, string initialDirectory, Action<string> onPicked)
        {
            _title.text = title;
            _extensions = extensions ?? Array.Empty<string>();
            _onPicked = onPicked;
            _error.text = string.Empty;
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

        private void AddShortcut(VisualElement parent, string label, string path)
        {
            var button = new Button(() => Navigate(path)) { text = label };
            button.SetEnabled(!string.IsNullOrEmpty(path) && Directory.Exists(path));
            parent.Add(button);
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
            _error.text = string.Empty;
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
                _error.text = "This folder cannot be read.";
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
            if (!File.Exists(path)) { _error.text = "That file does not exist."; return; }
            if (!MatchesFilter(path)) { _error.text = "That file type is not supported here."; return; }
            Pick(path);
        }

        private void Pick(string path)
        {
            var callback = _onPicked;
            Hide();
            callback?.Invoke(path);
        }
    }
}
