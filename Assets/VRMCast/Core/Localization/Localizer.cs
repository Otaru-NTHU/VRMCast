using System;
using System.Collections.Generic;
using System.Globalization;

namespace VRMCast.Core.Localization
{
    /// <summary>
    /// Looks up UI strings for the active language. Unknown keys return the key itself so a missing translation
    /// is visible rather than silent. Pure C#; the runtime persists the choice and rebinds the UI on change.
    /// </summary>
    public sealed class Localizer
    {
        private AppLanguage _language;
        private IReadOnlyDictionary<string, string> _table;

        public event Action<AppLanguage> LanguageChanged;

        public Localizer(AppLanguage language = AppLanguageExtensions.Default)
        {
            _language = language;
            _table = LocalizationTable.For(language);
        }

        public AppLanguage Language
        {
            get => _language;
            set
            {
                if (_language == value) return;
                _language = value;
                _table = LocalizationTable.For(value);
                LanguageChanged?.Invoke(value);
            }
        }

        public string this[string key] => Get(key);

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return _table.TryGetValue(key, out var text) ? text : key;
        }

        public string Format(string key, params object[] args) => Translate(new Message(key, args));

        public string Translate(Message message)
        {
            if (message.IsEmpty) return string.Empty;
            var template = Get(message.Key);
            if (message.Args.Length == 0) return template;

            var resolved = new object[message.Args.Length];
            for (var i = 0; i < resolved.Length; i++)
            {
                resolved[i] = message.Args[i] is Message nested ? Translate(nested) : message.Args[i];
            }
            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, resolved);
            }
            catch (FormatException)
            {
                return template;
            }
        }
    }
}
