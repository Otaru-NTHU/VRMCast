using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.Avatar;
using VRMCast.Core.Hotkeys;
using VRMCast.Core.Localization;
using VRMCast.Core.Tracking;
using VRMCast.Hotkeys;

namespace VRMCast.UI
{
    /// <summary>HOTKEYS section rows (PRD 24): key capture button, expression, trigger mode, intensity.</summary>
    public sealed class HotkeysView : IDisposable
    {
        private static readonly HotkeyTriggerMode[] ModeOrder = { HotkeyTriggerMode.Hold, HotkeyTriggerMode.Toggle, HotkeyTriggerMode.OneShot };
        private static readonly string[] ModeKeys = { "hotkey.hold", "hotkey.toggle", "hotkey.oneShot" };

        private sealed class Row
        {
            public VisualElement Root;
            public Button Key;
            public DropdownField Expression;
            public DropdownField Mode;
            public Slider Intensity;
        }

        private readonly Localizer _loc;
        private readonly HotkeyService _hotkeys;
        private readonly IAvatarService _avatars;
        private readonly VisualElement _container;
        private readonly Label _hint;
        private readonly List<Row> _rows = new List<Row>();
        private List<string> _expressionNames = new List<string>();
        private bool _rebuilding;
        private int _capturingIndex = -1;

        public HotkeysView(VisualElement container, Label hint, Localizer localizer, HotkeyService hotkeys, IAvatarService avatars)
        {
            _container = container;
            _hint = hint;
            _loc = localizer;
            _hotkeys = hotkeys;
            _avatars = avatars;
            _hotkeys.BindingsChanged += Rebuild;
            _avatars.AvatarLoaded += _ => Rebuild();
            _avatars.AvatarUnloaded += Rebuild;
            _loc.LanguageChanged += _ => Rebuild();
            Rebuild();
        }

        private List<string> ExpressionChoices()
        {
            var names = new List<string>(VrmExpressions.Presets);
            if (_avatars.HasAvatar)
            {
                foreach (var n in _avatars.Current.ExpressionNames)
                {
                    if (!names.Contains(n) && VrmExpressions.ToVrm0Preset(n) == null) names.Add(n);
                }
            }
            return names;
        }

        private void Rebuild()
        {
            _rebuilding = true;
            try
            {
                _container.Clear();
                _rows.Clear();
                _expressionNames = ExpressionChoices();
                var modes = new List<string>();
                foreach (var k in ModeKeys) modes.Add(_loc[k]);

                for (var i = 0; i < _hotkeys.Bindings.Count; i++)
                {
                    var index = i;
                    var b = _hotkeys.Bindings[i];
                    var row = new Row { Root = new VisualElement() };
                    row.Root.AddToClassList("row");
                    row.Root.AddToClassList("hotkey-row");

                    row.Key = new Button(() => BeginCapture(index)) { text = KeyLabel(b) };
                    row.Key.AddToClassList("hotkey-key");
                    row.Root.Add(row.Key);

                    row.Expression = new DropdownField { choices = new List<string>(_expressionNames) };
                    row.Expression.AddToClassList("hotkey-expression");
                    if (!_expressionNames.Contains(b.Expression) && !string.IsNullOrEmpty(b.Expression)) row.Expression.choices.Add(b.Expression);
                    row.Expression.SetValueWithoutNotify(string.IsNullOrEmpty(b.Expression) ? _expressionNames[0] : b.Expression);
                    row.Expression.RegisterValueChangedCallback(evt =>
                    {
                        if (_rebuilding) return;
                        var nb = _hotkeys.Bindings[index].Clone();
                        nb.Expression = evt.newValue;
                        _hotkeys.UpdateBinding(index, nb);
                    });
                    row.Root.Add(row.Expression);

                    row.Mode = new DropdownField { choices = modes };
                    row.Mode.AddToClassList("hotkey-mode");
                    row.Mode.SetValueWithoutNotify(modes[Array.IndexOf(ModeOrder, b.Mode)]);
                    row.Mode.RegisterValueChangedCallback(evt =>
                    {
                        if (_rebuilding) return;
                        var nb = _hotkeys.Bindings[index].Clone();
                        var mi = row.Mode.choices.IndexOf(evt.newValue);
                        nb.Mode = ModeOrder[mi < 0 ? 0 : mi];
                        _hotkeys.UpdateBinding(index, nb);
                    });
                    row.Root.Add(row.Mode);

                    row.Intensity = new Slider(0f, 1f) { value = b.Intensity };
                    row.Intensity.AddToClassList("hotkey-intensity");
                    row.Intensity.RegisterValueChangedCallback(evt =>
                    {
                        if (_rebuilding) return;
                        var nb = _hotkeys.Bindings[index].Clone();
                        nb.Intensity = evt.newValue;
                        _hotkeys.UpdateBinding(index, nb);
                    });
                    row.Root.Add(row.Intensity);

                    _container.Add(row.Root);
                    _rows.Add(row);
                }
                _hint.text = _loc["hotkey.hint"];
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private string KeyLabel(HotkeyBinding b) => HotkeyService.Label(b.Key);

        private void BeginCapture(int index)
        {
            if (_capturingIndex >= 0) return;
            _capturingIndex = index;
            _rows[index].Key.text = _loc["hotkey.pressKey"];
            _hotkeys.BeginCapture(code =>
            {
                _capturingIndex = -1;
                var nb = _hotkeys.Bindings[index].Clone();
                nb.Key = code == KeyCode.Backspace || code == KeyCode.Delete ? "" : code.ToString();
                _hotkeys.UpdateBinding(index, nb);
            });
        }

        public void Dispose()
        {
            _hotkeys.BindingsChanged -= Rebuild;
            _hotkeys.CancelCapture();
        }
    }
}
