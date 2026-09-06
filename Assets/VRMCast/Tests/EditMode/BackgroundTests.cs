using System;
using NUnit.Framework;
using VRMCast.Core.Backgrounds;

namespace VRMCast.Core.Tests
{
    public class BackgroundTests
    {
        [Test]
        public void DefaultsMatchPrd()
        {
            var s = new BackgroundSettings();
            Assert.That(s.Mode, Is.EqualTo(BackgroundMode.ChromaKey));
            Assert.That(s.ChromaColor.ToHex(), Is.EqualTo("#00FF00"));
            Assert.That(s.ImageFit, Is.EqualTo(ImageFitMode.Fill));
            Assert.That(s.ClearColor, Is.EqualTo(RgbaColor.ChromaGreen));
            Assert.That(s.RequiresAlpha, Is.False);
        }

        [Test]
        public void TransparentClearsToZeroAlpha()
        {
            var s = new BackgroundSettings { Mode = BackgroundMode.Transparent };
            Assert.That(s.ClearColor.A, Is.EqualTo(0f));
            Assert.That(s.RequiresAlpha, Is.True);
        }

        [Test]
        public void SolidColorAlwaysOpaque()
        {
            var s = new BackgroundSettings { Mode = BackgroundMode.SolidColor, SolidColor = new RgbaColor(0.2f, 0.4f, 0.6f, 0.1f) };
            Assert.That(s.ClearColor.A, Is.EqualTo(1f));
            Assert.That(s.ClearColor.R, Is.EqualTo(0.2f).Within(1e-6f));
        }

        [Test]
        public void ImageModeNeedsAPath()
        {
            var s = new BackgroundSettings { Mode = BackgroundMode.Image };
            Assert.That(s.ShowsImage, Is.False);
            s.ImagePath = "/tmp/bg.png";
            Assert.That(s.ShowsImage, Is.True);
        }

        [TestCase("#00FF00", 0f, 1f, 0f, 1f)]
        [TestCase("00ff00", 0f, 1f, 0f, 1f)]
        [TestCase("#FF000080", 1f, 0f, 0f, 128f / 255f)]
        [TestCase(" #102030 ", 16f / 255f, 32f / 255f, 48f / 255f, 1f)]
        public void ParsesHexColors(string text, float r, float g, float b, float a)
        {
            Assert.That(RgbaColor.TryParseHex(text, out var c), Is.True);
            Assert.That(c.R, Is.EqualTo(r).Within(1e-6f));
            Assert.That(c.G, Is.EqualTo(g).Within(1e-6f));
            Assert.That(c.B, Is.EqualTo(b).Within(1e-6f));
            Assert.That(c.A, Is.EqualTo(a).Within(1e-6f));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("#12345")]
        [TestCase("#GGGGGG")]
        [TestCase("green")]
        public void RejectsBadHex(string text)
        {
            Assert.That(RgbaColor.TryParseHex(text, out _), Is.False);
            Assert.Throws<FormatException>(() => RgbaColor.ParseHex(text));
        }

        [Test]
        public void HexRoundTrips()
        {
            Assert.That(RgbaColor.ParseHex("#1A2B3C").ToHex(), Is.EqualTo("#1A2B3C"));
            Assert.That(RgbaColor.ParseHex("#1A2B3C4D").ToHex(true), Is.EqualTo("#1A2B3C4D"));
        }

        [Test]
        public void FillCoversFrame()
        {
            // 4:3 image into 16:9 frame -> width matches, height overflows.
            var fit = ImageFitSolver.Solve(400, 300, 1920, 1080, ImageFitMode.Fill);
            Assert.That(fit.ScaleX, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(fit.ScaleY, Is.GreaterThan(1f));
            Assert.That(fit.ScaleY, Is.EqualTo((16f / 9f) / (4f / 3f)).Within(1e-5f));

            // 21:9 image into 16:9 frame -> height matches, width overflows.
            var wide = ImageFitSolver.Solve(2100, 900, 1920, 1080, ImageFitMode.Fill);
            Assert.That(wide.ScaleY, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(wide.ScaleX, Is.GreaterThan(1f));
        }

        [Test]
        public void FitLetterboxes()
        {
            var fit = ImageFitSolver.Solve(400, 300, 1920, 1080, ImageFitMode.Fit);
            Assert.That(fit.ScaleY, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(fit.ScaleX, Is.LessThan(1f));

            var wide = ImageFitSolver.Solve(2100, 900, 1920, 1080, ImageFitMode.Fit);
            Assert.That(wide.ScaleX, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(wide.ScaleY, Is.LessThan(1f));
        }

        [Test]
        public void StretchAndSameAspectAreIdentity()
        {
            var s = ImageFitSolver.Solve(400, 300, 1920, 1080, ImageFitMode.Stretch);
            Assert.That(s.ScaleX, Is.EqualTo(1f));
            Assert.That(s.ScaleY, Is.EqualTo(1f));

            foreach (ImageFitMode mode in Enum.GetValues(typeof(ImageFitMode)))
            {
                var same = ImageFitSolver.Solve(1920, 1080, 3840, 2160, mode);
                Assert.That(same.ScaleX, Is.EqualTo(1f).Within(1e-5f), mode.ToString());
                Assert.That(same.ScaleY, Is.EqualTo(1f).Within(1e-5f), mode.ToString());
            }
        }

        [Test]
        public void DegenerateSizesFallBackToIdentity()
        {
            var fit = ImageFitSolver.Solve(0, 0, 1920, 1080, ImageFitMode.Fill);
            Assert.That(fit.ScaleX, Is.EqualTo(1f));
            Assert.That(fit.ScaleY, Is.EqualTo(1f));
        }

        [Test]
        public void ModeLabelsRoundTrip()
        {
            foreach (BackgroundMode m in Enum.GetValues(typeof(BackgroundMode)))
            {
                Assert.That(BackgroundModeExtensions.TryParseLabel(m.Label(), out var parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(m));
            }
        }
    }
}
