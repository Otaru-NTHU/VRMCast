using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VRMCast.Core.Localization;
using VRMCast.Core.Vrm;

namespace VRMCast.Core.Tests
{
    public class LocalizationTests
    {
        [Test]
        public void BothLanguagesDefineTheSameKeys()
        {
            var zh = new HashSet<string>(LocalizationTable.TraditionalChinese.Keys);
            var en = new HashSet<string>(LocalizationTable.English.Keys);
            Assert.That(zh.Except(en), Is.Empty, "keys only in zh-Hant");
            Assert.That(en.Except(zh), Is.Empty, "keys only in en");
        }

        [Test]
        public void NoTranslationIsEmpty()
        {
            foreach (var table in new[] { LocalizationTable.TraditionalChinese, LocalizationTable.English })
            {
                foreach (var kv in table)
                {
                    Assert.That(kv.Value, Is.Not.Null.And.Not.Empty, kv.Key);
                }
            }
        }

        [Test]
        public void EveryLoadErrorHasAKey()
        {
            foreach (var key in VrmLoadErrors.All)
            {
                Assert.That(LocalizationTable.English.ContainsKey(key), key);
            }
        }

        [Test]
        public void DefaultIsTraditionalChinese()
        {
            var loc = new Localizer();
            Assert.That(loc.Language, Is.EqualTo(AppLanguage.ZhHant));
            Assert.That(loc["model.load"], Is.EqualTo("載入 VRM"));
        }

        [Test]
        public void SwitchingLanguageRaisesEventAndChangesText()
        {
            var loc = new Localizer();
            AppLanguage? raised = null;
            loc.LanguageChanged += l => raised = l;
            loc.Language = AppLanguage.En;
            Assert.That(raised, Is.EqualTo(AppLanguage.En));
            Assert.That(loc["model.load"], Is.EqualTo("Load VRM"));

            raised = null;
            loc.Language = AppLanguage.En;
            Assert.That(raised, Is.Null, "no event when unchanged");
        }

        [Test]
        public void UnknownKeyReturnsKey()
        {
            var loc = new Localizer(AppLanguage.En);
            Assert.That(loc["does.not.exist"], Is.EqualTo("does.not.exist"));
            Assert.That(loc[null], Is.EqualTo(string.Empty));
        }

        [Test]
        public void TranslatesNestedMessages()
        {
            var loc = new Localizer(AppLanguage.En);
            var msg = Message.Of(VrmLoadErrors.VersionMismatch, Message.Of(VrmVersion.Vrm1.LabelKey()), Message.Of(VrmVersion.Vrm0.LabelKey()));
            Assert.That(loc.Translate(msg), Is.EqualTo("This file was detected as VRM 1.0; loading it as VRM 0.x is not supported."));

            loc.Language = AppLanguage.ZhHant;
            Assert.That(loc.Translate(msg), Is.EqualTo("偵測到此檔案為 VRM 1.0，無法以 VRM 0.x 載入。"));
        }

        [Test]
        public void FormatWithBadTemplateFallsBackToTemplate()
        {
            var loc = new Localizer(AppLanguage.En);
            // model.load has no placeholders; extra args are ignored by string.Format.
            Assert.That(loc.Format("model.load", 1, 2), Is.EqualTo("Load VRM"));
            Assert.That(loc.Format("diag.avatar", "a.vrm", "VRM 1.0"), Is.EqualTo("Avatar: a.vrm [VRM 1.0]"));
        }

        [Test]
        public void LanguageCodesRoundTrip()
        {
            Assert.That(AppLanguageExtensions.TryParseCode("en", out var en), Is.True);
            Assert.That(en, Is.EqualTo(AppLanguage.En));
            Assert.That(AppLanguageExtensions.TryParseCode("zh-TW", out var zh), Is.True);
            Assert.That(zh, Is.EqualTo(AppLanguage.ZhHant));
            Assert.That(AppLanguageExtensions.TryParseCode("fr", out var fallback), Is.False);
            Assert.That(fallback, Is.EqualTo(AppLanguage.ZhHant));
            Assert.That(AppLanguage.En.Code(), Is.EqualTo("en"));
            Assert.That(AppLanguage.ZhHant.Code(), Is.EqualTo("zh-Hant"));
            Assert.That(AppLanguage.ZhHant.NativeName(), Is.EqualTo("繁體中文"));
        }
    }
}
