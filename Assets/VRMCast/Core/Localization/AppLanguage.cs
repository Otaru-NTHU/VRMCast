namespace VRMCast.Core.Localization
{
    /// <summary>UI languages. Traditional Chinese is the product default.</summary>
    public enum AppLanguage
    {
        ZhHant = 0,
        En = 1,
    }

    public static class AppLanguageExtensions
    {
        public const AppLanguage Default = AppLanguage.ZhHant;

        /// <summary>BCP-47 style code used for persistence.</summary>
        public static string Code(this AppLanguage language)
        {
            switch (language)
            {
                case AppLanguage.En: return "en";
                default: return "zh-Hant";
            }
        }

        /// <summary>Name of the language written in that language, for the language switcher.</summary>
        public static string NativeName(this AppLanguage language)
        {
            switch (language)
            {
                case AppLanguage.En: return "English";
                default: return "繁體中文";
            }
        }

        public static bool TryParseCode(string code, out AppLanguage language)
        {
            switch ((code ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "en":
                case "en-us":
                case "en-gb":
                    language = AppLanguage.En;
                    return true;
                case "zh-hant":
                case "zh-tw":
                case "zh-hk":
                case "zh":
                    language = AppLanguage.ZhHant;
                    return true;
                default:
                    language = Default;
                    return false;
            }
        }
    }
}
