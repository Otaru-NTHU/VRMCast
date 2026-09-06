using System;
using NUnit.Framework;
using VRMCast.Core.Rendering;

namespace VRMCast.Core.Tests
{
    public class OutputSettingsTests
    {
        [Test]
        public void DefaultIsExactly1080p30()
        {
            var d = OutputSettings.Default;
            Assert.That(d.Width, Is.EqualTo(1920));
            Assert.That(d.Height, Is.EqualTo(1080));
            Assert.That(d.Fps, Is.EqualTo(30));
            Assert.That(d.Aspect, Is.EqualTo(16f / 9f).Within(1e-6f));
        }

        [Test]
        public void RecommendedPresetIsTheDefault()
        {
            Assert.That(OutputSettings.TryFindPreset("1080p 30 — Recommended", out var s), Is.True);
            Assert.That(s, Is.EqualTo(OutputSettings.Default));
            Assert.That(OutputSettings.TryFindPreset("nope", out var fallback), Is.False);
            Assert.That(fallback, Is.EqualTo(OutputSettings.Default));
        }

        [Test]
        public void AllPresetsAre16By9()
        {
            foreach (var p in OutputSettings.Presets)
            {
                Assert.That(p.Settings.Aspect, Is.EqualTo(16f / 9f).Within(1e-3f), p.Label);
                Assert.That(p.Settings.Fps == 30 || p.Settings.Fps == 60, p.Label);
            }
        }

        [Test]
        public void RejectsOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OutputSettings(0, 1080, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OutputSettings(1920, 100000, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OutputSettings(1920, 1080, 0));
        }

        [Test]
        public void EqualityAndLabel()
        {
            Assert.That(new OutputSettings(1280, 720, 60) == new OutputSettings(1280, 720, 60), Is.True);
            Assert.That(new OutputSettings(1280, 720, 60) != OutputSettings.Default, Is.True);
            Assert.That(OutputSettings.Default.Label, Is.EqualTo("1920×1080 | 30 FPS"));
        }
    }
}
