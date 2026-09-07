using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.Core.Localization;
using VRMCast.Profiles;

namespace VRMCast.UI
{
    /// <summary>Profile manager modal (PRD 23.1): list, New, Save, Save As, Duplicate, Rename, Delete, auto-save, relink.</summary>
    public sealed class ProfileManagerView : IDisposable
    {
        private readonly Localizer _loc;
        private readonly ProfileService _profiles;
        private readonly Action _relinkVrm;
        private readonly VisualElement _overlay;
        private readonly Label _title;
        private readonly ListView _list;
        private readonly TextField _name;
        private readonly Toggle _autoSave;
        private readonly Label _missing;
        private readonly Button _relink;
        private readonly Button _new, _save, _saveAs, _duplicate, _rename, _delete, _close;
        private readonly Label _hint;
        private readonly List<string> _names = new List<string>();

        public bool IsOpen => _overlay.style.display == DisplayStyle.Flex;

        public ProfileManagerView(VisualElement root, Localizer localizer, ProfileService profiles, Action relinkVrm)
        {
            _loc = localizer;
            _profiles = profiles;
            _relinkVrm = relinkVrm;

            _overlay = new VisualElement { name = "profile-overlay" };
            _overlay.AddToClassList("modal-overlay");
            _overlay.style.display = DisplayStyle.None;
            root.Add(_overlay);

            var dialog = new VisualElement { name = "profile-dialog" };
            dialog.AddToClassList("modal-dialog");
            dialog.AddToClassList("profile-dialog");
            _overlay.Add(dialog);

            _title = new Label();
            _title.AddToClassList("modal-title");
            dialog.Add(_title);

            _list = new ListView(_names, 26, () => { var l = new Label(); l.AddToClassList("file-entry"); return l; }, (e, i) => ((Label)e).text = _names[i])
            {
                name = "profile-list",
                selectionType = SelectionType.Single,
            };
            _list.selectionChanged += OnSelectionChanged;
            dialog.Add(_list);

            var nameRow = new VisualElement();
            nameRow.AddToClassList("row");
            _name = new TextField { name = "profile-name" };
            _name.AddToClassList("grow");
            nameRow.Add(_name);
            _rename = new Button(() => Run(() => _profiles.Rename(_name.value)));
            nameRow.Add(_rename);
            dialog.Add(nameRow);

            var actions = new VisualElement();
            actions.AddToClassList("row");
            _new = new Button(() => Run(() => { _profiles.NewProfile(_name.value, fromCurrent: false); return true; }));
            _saveAs = new Button(() => Run(() => _profiles.SaveAs(_name.value)));
            _duplicate = new Button(() => Run(() => _profiles.Duplicate()));
            _delete = new Button(() => Run(() => _profiles.Delete()));
            actions.Add(_new); actions.Add(_saveAs); actions.Add(_duplicate); actions.Add(_delete);
            dialog.Add(actions);

            _autoSave = new Toggle();
            _autoSave.RegisterValueChangedCallback(evt => _profiles.SetAutoSave(evt.newValue));
            dialog.Add(_autoSave);

            _missing = new Label { name = "profile-missing" };
            _missing.AddToClassList("error-text");
            _missing.style.whiteSpace = WhiteSpace.Normal;
            dialog.Add(_missing);
            _relink = new Button(() => { Hide(); _relinkVrm?.Invoke(); });
            dialog.Add(_relink);

            _hint = new Label();
            _hint.AddToClassList("hint");
            dialog.Add(_hint);

            var buttons = new VisualElement();
            buttons.AddToClassList("row");
            buttons.AddToClassList("row-right");
            _save = new Button(() => Run(() => _profiles.Save()));
            _close = new Button(Hide);
            _close.AddToClassList("primary");
            buttons.Add(_save); buttons.Add(_close);
            dialog.Add(buttons);

            _overlay.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) Hide(); });

            _loc.LanguageChanged += OnLanguageChanged;
            _profiles.ProfileChanged += Refresh;
            _profiles.ProfileListChanged += Refresh;
            ApplyLanguage();
        }

        public void Show()
        {
            _overlay.style.display = DisplayStyle.Flex;
            Refresh();
            _name.Focus();
        }

        public void Hide() => _overlay.style.display = DisplayStyle.None;

        private void Run(Func<bool> action)
        {
            var ok = action();
            if (!ok && _profiles.LastError != null) _hint.text = _loc[_profiles.LastError];
            Refresh();
        }

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            var name = selection.FirstOrDefault() as string;
            if (string.IsNullOrEmpty(name) || _profiles.Current == null || name == _profiles.Current.name) return;
            _profiles.Save();
            _profiles.Load(name);
        }

        private void Refresh()
        {
            if (!IsOpen) return;
            _names.Clear();
            _names.AddRange(_profiles.ListNames());
            _list.RefreshItems();
            var current = _profiles.Current;
            if (current != null)
            {
                var index = _names.IndexOf(current.name);
                _list.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
                _name.SetValueWithoutNotify(current.name);
                _autoSave.SetValueWithoutNotify(current.autoSave);
                _delete.SetEnabled(_names.Count > 1 || current.name != Core.Profiles.ProfileData.DefaultName);
            }
            var missing = !string.IsNullOrEmpty(_profiles.MissingVrmPath);
            _missing.style.display = missing ? DisplayStyle.Flex : DisplayStyle.None;
            _relink.style.display = missing ? DisplayStyle.Flex : DisplayStyle.None;
            if (missing) _missing.text = _loc.Format("profile.missingVrm", _profiles.MissingVrmPath);
        }

        private void OnLanguageChanged(AppLanguage _) => ApplyLanguage();

        private void ApplyLanguage()
        {
            _title.text = _loc["profile.title"];
            _name.label = _loc["profile.name"];
            _rename.text = _loc["profile.rename"];
            _new.text = _loc["profile.new"];
            _saveAs.text = _loc["profile.saveAs"];
            _duplicate.text = _loc["profile.duplicate"];
            _delete.text = _loc["profile.delete"];
            _autoSave.label = _loc["profile.autoSave"];
            _relink.text = _loc["profile.relink"];
            _save.text = _loc["profile.save"];
            _close.text = _loc["profile.close"];
            _hint.text = _loc["profile.hint"];
        }

        public void Dispose()
        {
            _loc.LanguageChanged -= OnLanguageChanged;
            _profiles.ProfileChanged -= Refresh;
            _profiles.ProfileListChanged -= Refresh;
        }
    }
}
