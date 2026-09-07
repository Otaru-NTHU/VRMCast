using System;
using System.Collections.Generic;

namespace VRMCast.Core.Hotkeys
{
    /// <summary>Expression hotkey trigger modes (PRD 24).</summary>
    public enum HotkeyTriggerMode
    {
        Hold = 0,
        Toggle = 1,
        OneShot = 2,
    }

    public sealed class HotkeyBinding
    {
        /// <summary>Key name as understood by the runtime (UnityEngine.KeyCode name). Empty = unassigned.</summary>
        public string Key { get; set; } = "";
        public string Expression { get; set; } = "";
        public float Intensity { get; set; } = 1f;
        public HotkeyTriggerMode Mode { get; set; } = HotkeyTriggerMode.Hold;

        public bool IsAssigned => !string.IsNullOrEmpty(Key) && !string.IsNullOrEmpty(Expression);

        public HotkeyBinding Clone() => (HotkeyBinding)MemberwiseClone();

        /// <summary>Default table from PRD 24: 1..5 → Happy, Angry, Sad, Surprised, Neutral (Toggle).</summary>
        public static List<HotkeyBinding> Defaults() => new List<HotkeyBinding>
        {
            new HotkeyBinding { Key = "Alpha1", Expression = "happy", Mode = HotkeyTriggerMode.Toggle },
            new HotkeyBinding { Key = "Alpha2", Expression = "angry", Mode = HotkeyTriggerMode.Toggle },
            new HotkeyBinding { Key = "Alpha3", Expression = "sad", Mode = HotkeyTriggerMode.Toggle },
            new HotkeyBinding { Key = "Alpha4", Expression = "surprised", Mode = HotkeyTriggerMode.Toggle },
            new HotkeyBinding { Key = "Alpha5", Expression = "neutral", Mode = HotkeyTriggerMode.OneShot },
            new HotkeyBinding { Key = "", Expression = "relaxed", Mode = HotkeyTriggerMode.Hold },
        };
    }

    /// <summary>
    /// Pure state machine for expression hotkeys. Feed it key-down / key-up edges and time; read back the target
    /// expression weights. Hold: active while pressed. Toggle: flips on key-down. OneShot: plays for
    /// <see cref="OneShotSeconds"/> then releases. Weights ease in/out so a hotkey never snaps the face.
    /// </summary>
    public sealed class HotkeyState
    {
        public const float OneShotSeconds = 1.2f;
        public const float EaseSeconds = 0.12f;

        private readonly List<HotkeyBinding> _bindings = new List<HotkeyBinding>();
        private readonly List<bool> _latched = new List<bool>();
        private readonly List<float> _oneShotRemaining = new List<float>();
        private readonly List<float> _current = new List<float>();
        private readonly HashSet<string> _held = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _weights = new Dictionary<string, float>(StringComparer.Ordinal);

        public IReadOnlyList<HotkeyBinding> Bindings => _bindings;

        /// <summary>Expression → weight for every binding that is currently contributing (0 entries removed).</summary>
        public IReadOnlyDictionary<string, float> Weights => _weights;

        public HotkeyState(IEnumerable<HotkeyBinding> bindings = null)
        {
            SetBindings(bindings ?? HotkeyBinding.Defaults());
        }

        public void SetBindings(IEnumerable<HotkeyBinding> bindings)
        {
            _bindings.Clear();
            _latched.Clear();
            _oneShotRemaining.Clear();
            _current.Clear();
            foreach (var b in bindings)
            {
                if (b == null) continue;
                _bindings.Add(b);
                _latched.Add(false);
                _oneShotRemaining.Add(0f);
                _current.Add(0f);
            }
            _weights.Clear();
        }

        public void KeyDown(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _held.Add(key);
            for (var i = 0; i < _bindings.Count; i++)
            {
                var b = _bindings[i];
                if (!b.IsAssigned || b.Key != key) continue;
                switch (b.Mode)
                {
                    case HotkeyTriggerMode.Toggle: _latched[i] = !_latched[i]; break;
                    case HotkeyTriggerMode.OneShot: _oneShotRemaining[i] = OneShotSeconds; break;
                }
            }
        }

        public void KeyUp(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _held.Remove(key);
        }

        /// <summary>Releases every toggle and one-shot (for example when the avatar changes).</summary>
        public void ReleaseAll()
        {
            for (var i = 0; i < _bindings.Count; i++) { _latched[i] = false; _oneShotRemaining[i] = 0f; }
            _held.Clear();
        }

        public IReadOnlyDictionary<string, float> Update(float dt)
        {
            _weights.Clear();
            var alpha = dt <= 0f ? 1f : 1f - (float)Math.Exp(-dt / EaseSeconds);
            for (var i = 0; i < _bindings.Count; i++)
            {
                var b = _bindings[i];
                var active = false;
                if (b.IsAssigned)
                {
                    switch (b.Mode)
                    {
                        case HotkeyTriggerMode.Hold: active = _held.Contains(b.Key); break;
                        case HotkeyTriggerMode.Toggle: active = _latched[i]; break;
                        case HotkeyTriggerMode.OneShot:
                            if (_oneShotRemaining[i] > 0f) { _oneShotRemaining[i] -= dt; active = true; }
                            break;
                    }
                }
                var target = active ? Math.Max(0f, Math.Min(1f, b.Intensity)) : 0f;
                _current[i] += (target - _current[i]) * alpha;
                if (_current[i] < 1e-3f) { _current[i] = 0f; continue; }
                if (string.IsNullOrEmpty(b.Expression)) continue;
                if (_weights.TryGetValue(b.Expression, out var existing)) _weights[b.Expression] = Math.Max(existing, _current[i]);
                else _weights[b.Expression] = _current[i];
            }
            return _weights;
        }

        /// <summary>True while any binding contributes weight (used to show a "hotkey active" indicator).</summary>
        public bool AnyActive => _weights.Count > 0;
    }
}
