using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VRMCast.Core.Hotkeys;

namespace VRMCast.Hotkeys
{
    /// <summary>
    /// Expression hotkeys (PRD 24) on top of the legacy Input API. Keys are ignored while a text field has
    /// keyboard focus so typing a profile name never triggers an expression. Global (unfocused-window) hotkeys are
    /// deferred, as the PRD allows.
    /// </summary>
    public sealed class HotkeyService
    {
        private readonly List<HotkeyBinding> _bindings = new List<HotkeyBinding>();
        private readonly List<KeyCode> _keyCodes = new List<KeyCode>();
        private readonly HotkeyState _state = new HotkeyState(new HotkeyBinding[0]);
        private Func<bool> _isTextInputFocused = () => false;
        private Action<KeyCode> _captureCallback;

        public IReadOnlyList<HotkeyBinding> Bindings => _bindings;
        public IReadOnlyDictionary<string, float> Weights => _state.Weights;
        public bool AnyActive => _state.AnyActive;
        public bool IsCapturing => _captureCallback != null;

        public event Action BindingsChanged;

        public void SetTextInputGuard(Func<bool> isTextInputFocused) => _isTextInputFocused = isTextInputFocused ?? (() => false);

        public void SetBindings(IEnumerable<HotkeyBinding> bindings)
        {
            _bindings.Clear();
            _keyCodes.Clear();
            foreach (var b in bindings)
            {
                if (b == null) continue;
                _bindings.Add(b);
                _keyCodes.Add(Parse(b.Key));
            }
            _state.SetBindings(_bindings);
            BindingsChanged?.Invoke();
        }

        public void UpdateBinding(int index, HotkeyBinding binding)
        {
            if (index < 0 || index >= _bindings.Count || binding == null) return;
            _bindings[index] = binding;
            _keyCodes[index] = Parse(binding.Key);
            _state.SetBindings(_bindings);
            BindingsChanged?.Invoke();
        }

        /// <summary>The next key press is delivered to the callback instead of triggering hotkeys (Escape cancels).</summary>
        public void BeginCapture(Action<KeyCode> onKey) => _captureCallback = onKey;

        public void CancelCapture() => _captureCallback = null;

        public void ReleaseAll() => _state.ReleaseAll();

        /// <summary>Call every Update. Returns the expression weights to overlay on the tracked ones.</summary>
        public IReadOnlyDictionary<string, float> Tick(float dt)
        {
            if (_captureCallback != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { _captureCallback = null; }
                else if (Input.anyKeyDown)
                {
                    foreach (KeyCode code in CaptureCandidates)
                    {
                        if (!Input.GetKeyDown(code)) continue;
                        var cb = _captureCallback;
                        _captureCallback = null;
                        cb(code);
                        break;
                    }
                }
                return _state.Update(dt);
            }

            if (!_isTextInputFocused())
            {
                for (var i = 0; i < _bindings.Count; i++)
                {
                    var code = _keyCodes[i];
                    if (code == KeyCode.None) continue;
                    if (Input.GetKeyDown(code)) _state.KeyDown(_bindings[i].Key);
                    if (Input.GetKeyUp(code)) _state.KeyUp(_bindings[i].Key);
                }
            }
            return _state.Update(dt);
        }

        public static KeyCode Parse(string name)
        {
            if (string.IsNullOrEmpty(name)) return KeyCode.None;
            return Enum.TryParse<KeyCode>(name, ignoreCase: true, out var code) ? code : KeyCode.None;
        }

        /// <summary>Short display label for a key name (Alpha1 → 1, Keypad5 → Num 5).</summary>
        public static string Label(string name)
        {
            if (string.IsNullOrEmpty(name)) return "—";
            if (name.StartsWith("Alpha", StringComparison.Ordinal) && name.Length == 6) return name.Substring(5);
            if (name.StartsWith("Keypad", StringComparison.Ordinal)) return "Num " + name.Substring(6);
            return name;
        }

        private static readonly KeyCode[] CaptureCandidates = BuildCandidates();

        private static KeyCode[] BuildCandidates()
        {
            var list = new List<KeyCode>();
            foreach (KeyCode code in Enum.GetValues(typeof(KeyCode)))
            {
                if (code == KeyCode.None || code == KeyCode.Escape) continue;
                if (code >= KeyCode.Mouse0 && code <= KeyCode.Mouse6) continue;
                if (code >= KeyCode.JoystickButton0) continue;
                list.Add(code);
            }
            return list.ToArray();
        }

        /// <summary>UI Toolkit guard: true while a TextField (or any text input) has focus.</summary>
        public static bool IsTextFieldFocused(VisualElement root)
        {
            var focused = root?.panel?.focusController?.focusedElement;
            return focused is TextField || (focused as VisualElement)?.GetFirstAncestorOfType<TextField>() != null;
        }
    }
}
