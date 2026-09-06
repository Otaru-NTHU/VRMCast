using System;
using NUnit.Framework;
using VRMCast.Core.Camera;

namespace VRMCast.Core.Tests
{
    public class AvatarCameraSolverTests
    {
        private const float Aspect16By9 = 16f / 9f;
        private static readonly AvatarMetrics Metrics = AvatarMetrics.FromBounds(0f, 1.6f, 0f, 0f, 0.5f);

        private static float VisibleHalfHeight(CameraPose pose) =>
            pose.Distance * (float)Math.Tan(pose.FovDeg * 0.5 * Math.PI / 180.0);

        [Test]
        public void DefaultStateIsBust()
        {
            var state = new AvatarCameraState();
            Assert.That(state.Preset, Is.EqualTo(FramingPreset.Bust));
            Assert.That(state.Zoom, Is.EqualTo(1f));
            Assert.That(state.FovDeg, Is.EqualTo(AvatarCameraState.DefaultFovDeg));
        }

        [TestCase(FramingPreset.Face)]
        [TestCase(FramingPreset.Bust)]
        [TestCase(FramingPreset.HalfBody)]
        [TestCase(FramingPreset.FullBody)]
        public void PresetKeepsItsSpanInsideTheFrame(FramingPreset preset)
        {
            var state = new AvatarCameraState { Preset = preset };
            var pose = AvatarCameraSolver.Solve(state, Metrics, Aspect16By9);
            var spec = AvatarCameraSolver.GetSpec(preset, Metrics);

            var halfVisible = VisibleHalfHeight(pose);
            var centerY = (spec.LowY + spec.HighY) * 0.5f;
            Assert.That(pose.LookAt.Y, Is.EqualTo(centerY).Within(1e-4f));
            Assert.That(pose.LookAt.Y + halfVisible, Is.GreaterThanOrEqualTo(spec.HighY));
            Assert.That(pose.LookAt.Y - halfVisible, Is.LessThanOrEqualTo(spec.LowY));
            // and not absurdly far away: margin should be bounded
            Assert.That(halfVisible * 2f, Is.LessThan((spec.HighY - spec.LowY) * 1.5f));
        }

        [Test]
        public void PresetsGetProgressivelyWider()
        {
            float Distance(FramingPreset p) => AvatarCameraSolver.Solve(new AvatarCameraState { Preset = p }, Metrics, Aspect16By9).Distance;

            Assert.That(Distance(FramingPreset.Face), Is.LessThan(Distance(FramingPreset.Bust)));
            Assert.That(Distance(FramingPreset.Bust), Is.LessThan(Distance(FramingPreset.HalfBody)));
            Assert.That(Distance(FramingPreset.HalfBody), Is.LessThan(Distance(FramingPreset.FullBody)));
        }

        [Test]
        public void CameraSitsInFrontOfAvatarLookingBack()
        {
            var pose = AvatarCameraSolver.Solve(new AvatarCameraState(), Metrics, Aspect16By9);
            Assert.That(pose.Position.Z, Is.GreaterThan(pose.LookAt.Z));
            Assert.That(pose.Position.X, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(pose.Position.Y, Is.EqualTo(pose.LookAt.Y).Within(1e-5f));
        }

        [Test]
        public void ZoomHalvesDistance()
        {
            var one = AvatarCameraSolver.Solve(new AvatarCameraState { Zoom = 1f }, Metrics, Aspect16By9);
            var two = AvatarCameraSolver.Solve(new AvatarCameraState { Zoom = 2f }, Metrics, Aspect16By9);
            Assert.That(two.Distance, Is.EqualTo(one.Distance / 2f).Within(1e-4f));
        }

        [Test]
        public void ZoomIsClamped()
        {
            var huge = AvatarCameraSolver.Solve(new AvatarCameraState { Zoom = 1000f }, Metrics, Aspect16By9);
            var max = AvatarCameraSolver.Solve(new AvatarCameraState { Zoom = AvatarCameraState.MaxZoom }, Metrics, Aspect16By9);
            Assert.That(huge.Distance, Is.EqualTo(max.Distance).Within(1e-5f));
            Assert.That(huge.Distance, Is.GreaterThanOrEqualTo(AvatarCameraSolver.MinDistance));
        }

        [Test]
        public void OrbitYawMovesCameraAroundTargetAtSameDistance()
        {
            var front = AvatarCameraSolver.Solve(new AvatarCameraState(), Metrics, Aspect16By9);
            var side = AvatarCameraSolver.Solve(new AvatarCameraState { OrbitYawDeg = 90f }, Metrics, Aspect16By9);
            var back = AvatarCameraSolver.Solve(new AvatarCameraState { OrbitYawDeg = 180f }, Metrics, Aspect16By9);

            Assert.That(side.Distance, Is.EqualTo(front.Distance).Within(1e-4f));
            Assert.That(side.Position.X, Is.EqualTo(front.Distance).Within(1e-4f));
            Assert.That(side.Position.Z, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(back.Position.Z, Is.EqualTo(-front.Position.Z).Within(1e-4f));
            Assert.That(side.LookAt, Is.EqualTo(front.LookAt));
        }

        [Test]
        public void OrbitPitchRaisesCamera()
        {
            var pose = AvatarCameraSolver.Solve(new AvatarCameraState { OrbitPitchDeg = 30f }, Metrics, Aspect16By9);
            Assert.That(pose.Position.Y, Is.GreaterThan(pose.LookAt.Y));
            Assert.That(pose.Distance, Is.EqualTo(AvatarCameraSolver.Solve(new AvatarCameraState(), Metrics, Aspect16By9).Distance).Within(1e-4f));
        }

        [Test]
        public void PanShiftsCameraAndTargetTogetherInViewPlane()
        {
            var basePose = AvatarCameraSolver.Solve(new AvatarCameraState(), Metrics, Aspect16By9);
            var panned = AvatarCameraSolver.Solve(new AvatarCameraState { PanX = 0.5f, PanY = 0.25f }, Metrics, Aspect16By9);

            var delta = panned.LookAt - basePose.LookAt;
            Assert.That(panned.Position - basePose.Position, Is.EqualTo(delta));
            // Positive PanX moves the avatar to the right on screen: the camera moves toward its own left.
            // Camera on +Z looking toward -Z has screen-right = world -X, so camera-left = world +X.
            Assert.That(delta.X, Is.EqualTo(0.5f * Metrics.Height).Within(1e-4f));
            Assert.That(delta.Y, Is.EqualTo(-0.25f * Metrics.Height).Within(1e-4f));
            Assert.That(delta.Z, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void NarrowAspectBacksOffToFitWidth()
        {
            var wide = AvatarCameraSolver.Solve(new AvatarCameraState { Preset = FramingPreset.FullBody }, Metrics, Aspect16By9);
            var narrow = AvatarCameraSolver.Solve(new AvatarCameraState { Preset = FramingPreset.FullBody }, Metrics, 0.2f);
            Assert.That(narrow.Distance, Is.GreaterThan(wide.Distance));
        }

        [Test]
        public void ChangingAspectBetween16By9PresetsPreservesVerticalFraming()
        {
            var a = AvatarCameraSolver.Solve(new AvatarCameraState(), Metrics, 1920f / 1080f);
            var b = AvatarCameraSolver.Solve(new AvatarCameraState(), Metrics, 3840f / 2160f);
            Assert.That(a.Distance, Is.EqualTo(b.Distance).Within(1e-5f));
        }

        [Test]
        public void InvalidAspectFallsBackTo16By9()
        {
            var good = AvatarCameraSolver.Solve(new AvatarCameraState(), Metrics, Aspect16By9);
            var bad = AvatarCameraSolver.Solve(new AvatarCameraState(), Metrics, 0f);
            Assert.That(bad.Distance, Is.EqualTo(good.Distance).Within(1e-5f));
        }

        [Test]
        public void ResetViewClearsAdjustmentsButKeepsPreset()
        {
            var state = new AvatarCameraState { Preset = FramingPreset.Face, Zoom = 2f, PanX = 1f, OrbitYawDeg = 45f, FovDeg = 60f };
            state.ResetView();
            Assert.That(state.Preset, Is.EqualTo(FramingPreset.Face));
            Assert.That(state.Zoom, Is.EqualTo(1f));
            Assert.That(state.PanX, Is.EqualTo(0f));
            Assert.That(state.OrbitYawDeg, Is.EqualTo(0f));
            Assert.That(state.FovDeg, Is.EqualTo(AvatarCameraState.DefaultFovDeg));
        }

        [Test]
        public void WrapDegreesKeepsHalfOpenRange()
        {
            Assert.That(AvatarCameraState.WrapDegrees(190f), Is.EqualTo(-170f));
            Assert.That(AvatarCameraState.WrapDegrees(-190f), Is.EqualTo(170f));
            Assert.That(AvatarCameraState.WrapDegrees(360f), Is.EqualTo(0f));
            Assert.That(AvatarCameraState.WrapDegrees(-180f), Is.EqualTo(180f));
        }

        [Test]
        public void PlaceholderMetricsHaveSaneProportions()
        {
            var m = AvatarMetrics.Placeholder;
            Assert.That(m.FootY, Is.LessThan(m.HipsY));
            Assert.That(m.HipsY, Is.LessThan(m.ChestY));
            Assert.That(m.ChestY, Is.LessThan(m.NeckY));
            Assert.That(m.NeckY, Is.LessThan(m.HeadY));
            Assert.That(m.HeadY, Is.LessThan(m.TopY));
            Assert.That(m.Height, Is.EqualTo(1.6f).Within(1e-5f));
        }

        [Test]
        public void FramingPresetLabelsRoundTrip()
        {
            foreach (FramingPreset p in Enum.GetValues(typeof(FramingPreset)))
            {
                Assert.That(FramingPresetExtensions.TryParseLabel(p.Label(), out var parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(p));
            }
            Assert.That(FramingPresetExtensions.TryParseLabel("nope", out _), Is.False);
        }
    }
}
