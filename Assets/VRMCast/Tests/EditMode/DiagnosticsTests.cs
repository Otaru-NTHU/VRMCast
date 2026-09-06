using NUnit.Framework;
using VRMCast.Core.Diagnostics;
using VRMCast.Core.Rendering;
using VRMCast.Core.Tracking;

namespace VRMCast.Core.Tests
{
    public class DiagnosticsTests
    {
        [Test]
        public void FpsCounterPublishesOncePerWindow()
        {
            var counter = new FpsCounter(0.5);
            var published = 0;
            for (var i = 0; i < 16; i++)
            {
                if (counter.AddFrame(1.0 / 30.0)) published++;
            }
            Assert.That(published, Is.EqualTo(1));
            Assert.That(counter.Fps, Is.EqualTo(30.0).Within(0.01));
            Assert.That(counter.FrameTimeMs, Is.EqualTo(33.333).Within(0.01));
            Assert.That(counter.TotalFrames, Is.EqualTo(16));
        }

        [Test]
        public void FpsCounterIgnoresGarbage()
        {
            var counter = new FpsCounter(0.1);
            Assert.That(counter.AddFrame(-1), Is.False);
            Assert.That(counter.AddFrame(double.NaN), Is.False);
            Assert.That(counter.TotalFrames, Is.EqualTo(0));
            counter.AddFrame(0.2);
            Assert.That(counter.Fps, Is.EqualTo(5.0).Within(1e-9));
            counter.Reset();
            Assert.That(counter.Fps, Is.EqualTo(0));
            Assert.That(counter.TotalFrames, Is.EqualTo(0));
        }

        [Test]
        public void ReportContainsKeyFieldsAndNoFilePath()
        {
            var snap = new DiagnosticsSnapshot
            {
                RenderFps = 59.9,
                FrameTimeMs = 16.7,
                Output = OutputSettings.Default,
                TargetFrameRate = 30,
                AvatarName = "Alicia.vrm",
                AvatarVersion = "VRM 0.x",
                AppVersion = "0.1.0",
                Platform = "macOS",
            };
            var report = snap.ToReport();
            StringAssert.Contains("Render FPS: 59.9", report);
            StringAssert.Contains("Output: 1920x1080 @ 30", report);
            StringAssert.Contains("Alicia.vrm [VRM 0.x]", report);
            StringAssert.Contains("Background: Chroma Key", report);
            StringAssert.Contains("Framing: Bust", report);
            StringAssert.DoesNotContain("/Users/", report);
        }

        [Test]
        public void LatestFrameBufferKeepsOnlyNewest()
        {
            var buffer = new LatestFrameBuffer<TrackingFrame>();
            Assert.That(buffer.TryRead(out _), Is.False);

            buffer.Publish(TrackingFrame.Empty(1));
            buffer.Publish(TrackingFrame.Empty(2));
            Assert.That(buffer.TryRead(out var frame), Is.True);
            Assert.That(frame.Timestamp, Is.EqualTo(2));

            long seq = 0;
            Assert.That(buffer.TryReadNewer(ref seq, out _), Is.True);
            Assert.That(seq, Is.EqualTo(2));
            Assert.That(buffer.TryReadNewer(ref seq, out _), Is.False);

            buffer.Publish(TrackingFrame.Empty(3));
            Assert.That(buffer.TryReadNewer(ref seq, out frame), Is.True);
            Assert.That(frame.Timestamp, Is.EqualTo(3));

            buffer.Clear();
            Assert.That(buffer.TryRead(out _), Is.False);
        }
    }
}
