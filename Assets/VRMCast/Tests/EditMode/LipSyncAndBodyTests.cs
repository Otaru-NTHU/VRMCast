using System;
using System.Collections.Generic;
using NUnit.Framework;
using VRMCast.Core.Audio;
using VRMCast.Core.Tracking;

namespace VRMCast.Core.Tests
{
    public class LipSyncAndBodyTests
    {
        [SetUp]
        public void RawLabels() => PoseFrameBuilder.SwapLeftRight = false;

        [TearDown]
        public void RestoreLabels() => PoseFrameBuilder.SwapLeftRight = true;

        private static float[] Sine(float amplitude, int count = 320)
        {
            var s = new float[count];
            for (var i = 0; i < count; i++) s[i] = amplitude * (float)Math.Sin(i * 0.3);
            return s;
        }

        // ---------------------------------------------------------------- audio meter

        [Test]
        public void SilenceStaysClosedAndSpeechOpens()
        {
            var meter = new AudioLevelMeter();
            for (var i = 0; i < 20; i++) meter.Process(Sine(0.001f), 320, 16000); // ~-63 dBFS
            Assert.That(meter.IsOpen, Is.False);
            Assert.That(meter.Envelope, Is.EqualTo(0f));

            for (var i = 0; i < 20; i++) meter.Process(Sine(0.3f), 320, 16000); // ~-13.5 dBFS
            Assert.That(meter.IsOpen, Is.True);
            Assert.That(meter.RawDb, Is.EqualTo(-13.5f).Within(1f));
            Assert.That(meter.Envelope, Is.GreaterThan(0.85f));
        }

        [Test]
        public void AttackIsFasterThanRelease()
        {
            var meter = new AudioLevelMeter(new AudioLevelSettings { AttackSeconds = 0.02f, ReleaseSeconds = 0.2f });
            meter.Advance(-15f, 0.02f);
            var afterAttack = meter.Envelope;
            for (var i = 0; i < 10; i++) meter.Advance(-15f, 0.02f);
            var peak = meter.Envelope;
            meter.Advance(-100f, 0.02f);
            var afterRelease = meter.Envelope;
            Assert.That(afterAttack, Is.GreaterThan(peak * 0.5f), "one attack constant reaches ~63%");
            Assert.That(peak - afterRelease, Is.LessThan(peak * 0.15f), "release is slow");
        }

        [Test]
        public void GateHasHysteresisAndSensitivityScales()
        {
            var meter = new AudioLevelMeter(new AudioLevelSettings { GateDb = -40f, GateHysteresisDb = 3f, AttackSeconds = 0.001f, ReleaseSeconds = 0.001f });
            meter.Advance(-41f, 0.02f);
            Assert.That(meter.IsOpen, Is.False);
            meter.Advance(-39f, 0.02f);
            Assert.That(meter.IsOpen, Is.True);
            meter.Advance(-41f, 0.02f);
            Assert.That(meter.IsOpen, Is.True, "stays open within hysteresis");
            meter.Advance(-44f, 0.02f);
            Assert.That(meter.IsOpen, Is.False);

            var low = new AudioLevelMeter(new AudioLevelSettings { Sensitivity = 0.5f, AttackSeconds = 0.001f });
            var high = new AudioLevelMeter(new AudioLevelSettings { Sensitivity = 2f, AttackSeconds = 0.001f });
            for (var i = 0; i < 5; i++) { low.Advance(-30f, 0.05f); high.Advance(-30f, 0.05f); }
            Assert.That(high.Envelope, Is.GreaterThan(low.Envelope * 2.5f));
        }

        // ---------------------------------------------------------------- lip solver

        private static Dictionary<string, float> Mouth(float aa, float ou = 0f, float oh = 0f)
        {
            return new Dictionary<string, float>(StringComparer.Ordinal) { [VrmExpressions.Aa] = aa, [VrmExpressions.Ou] = ou, [VrmExpressions.Oh] = oh, [VrmExpressions.Happy] = 0.4f };
        }

        [Test]
        public void CameraModeLeavesExpressionsAlone()
        {
            var e = Mouth(0.7f, 0.2f);
            HybridLipSolver.Apply(e, new LipSyncSettings { Mode = LipSyncMode.Camera }, 1f, 1f, true, true);
            Assert.That(e[VrmExpressions.Aa], Is.EqualTo(0.7f));
            Assert.That(e[VrmExpressions.Ou], Is.EqualTo(0.2f));
        }

        [Test]
        public void MicrophoneModeUsesEnergyOnly()
        {
            var e = Mouth(0.7f, 0.2f);
            HybridLipSolver.Apply(e, new LipSyncSettings { Mode = LipSyncMode.Microphone }, 0f, 0.6f, true, true);
            Assert.That(e[VrmExpressions.Aa], Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(e[VrmExpressions.Ou], Is.EqualTo(0f));
            Assert.That(e[VrmExpressions.Happy], Is.EqualTo(0.4f), "non-mouth expressions untouched");
        }

        [Test]
        public void HybridBlendsAndKeepsVowelShape()
        {
            var e = Mouth(0.8f, 0.4f);
            var s = new LipSyncSettings { Mode = LipSyncMode.Hybrid, CameraWeight = 0.45f, AudioWeight = 0.55f };
            HybridLipSolver.Apply(e, s, 1f, 1f, true, true);
            var expectedAmplitude = 0.45f * 0.8f + 0.55f * 1f;
            Assert.That(e[VrmExpressions.Aa], Is.EqualTo(expectedAmplitude).Within(1e-4f));
            Assert.That(e[VrmExpressions.Ou], Is.EqualTo(expectedAmplitude * 0.5f).Within(1e-4f), "ou keeps its ratio to aa");
        }

        [Test]
        public void HybridSuppressesFalseOpeningWhenSilent()
        {
            var e = Mouth(0.6f);
            var s = new LipSyncSettings { Mode = LipSyncMode.Hybrid, SilentSuppression = 0.3f };
            HybridLipSolver.Apply(e, s, 1f, 0f, false, true);
            Assert.That(e[VrmExpressions.Aa], Is.EqualTo(0.18f).Within(1e-4f));
        }

        [Test]
        public void HybridFallsBackToAudioWithoutFaceAndToCameraWithoutMic()
        {
            var e = Mouth(0.6f);
            HybridLipSolver.Apply(e, new LipSyncSettings { Mode = LipSyncMode.Hybrid }, 0f, 0.5f, true, true);
            Assert.That(e[VrmExpressions.Aa], Is.EqualTo(0.5f).Within(1e-5f), "no face: audio only");

            var e2 = Mouth(0.6f);
            HybridLipSolver.Apply(e2, new LipSyncSettings { Mode = LipSyncMode.Hybrid }, 1f, 0f, false, audioAvailable: false);
            Assert.That(e2[VrmExpressions.Aa], Is.EqualTo(0.6f), "no microphone: camera passthrough, no suppression");
        }

        // ---------------------------------------------------------------- body

        private static float[] World(float shoulderDy = 0f, float shoulderDz = 0f, float lean = 0f)
        {
            var w = new float[PoseFrameBuilder.LandmarkCount * 3];
            void Set(int i, float x, float y, float z) { w[i * 3] = x; w[i * 3 + 1] = y; w[i * 3 + 2] = z; }
            // Hips at the origin, shoulders 0.45 m up (y down => negative), 0.4 m apart.
            Set(PoseFrameBuilder.LeftHip, 0.15f, 0f, 0f);
            Set(PoseFrameBuilder.RightHip, -0.15f, 0f, 0f);
            Set(PoseFrameBuilder.LeftShoulder, 0.2f, -0.45f + shoulderDy * 0.5f, -lean + shoulderDz * 0.5f);
            Set(PoseFrameBuilder.RightShoulder, -0.2f, -0.45f - shoulderDy * 0.5f, -lean - shoulderDz * 0.5f);
            Set(PoseFrameBuilder.Nose, 0f, -0.6f, -lean);
            return w;
        }

        [Test]
        public void BodyAnglesFollowContract()
        {
            var upright = PoseFrameBuilder.Build(0, World(), null);
            Assert.That(upright.Pose, Is.Not.Null);
            PoseFrameBuilder.ComputeAngles(upright.Pose.Value, out var r, out var y, out var p);
            Assert.That(r, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(p, Is.EqualTo(0f).Within(1e-4f));

            // Left shoulder lower (user leans left) -> positive roll.
            PoseFrameBuilder.ComputeAngles(PoseFrameBuilder.Build(0, World(shoulderDy: 0.1f), null).Pose.Value, out r, out _, out _);
            Assert.That(r, Is.GreaterThan(0f));
            // Left shoulder farther from the camera (user turns left) -> positive yaw.
            PoseFrameBuilder.ComputeAngles(PoseFrameBuilder.Build(0, World(shoulderDz: 0.1f), null).Pose.Value, out _, out y, out _);
            Assert.That(y, Is.GreaterThan(0f));
            // Shoulders closer to the camera than hips (lean forward) -> positive pitch.
            PoseFrameBuilder.ComputeAngles(PoseFrameBuilder.Build(0, World(lean: 0.1f), null).Pose.Value, out _, out _, out p);
            Assert.That(p, Is.GreaterThan(0f));
        }

        [Test]
        public void PoseBuilderRejectsShortArraysAndUsesVisibility()
        {
            Assert.That(PoseFrameBuilder.Build(0, new float[10], null).PoseConfidence, Is.EqualTo(0f));
            var vis = new float[PoseFrameBuilder.LandmarkCount];
            for (var i = 0; i < vis.Length; i++) vis[i] = 1f;
            vis[PoseFrameBuilder.RightShoulder] = 0.2f;
            Assert.That(PoseFrameBuilder.Build(0, World(), vis).PoseConfidence, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(PoseFrameBuilder.NoBody(1).PoseConfidence, Is.EqualTo(0f));
        }

        [Test]
        public void PoseBuilderSwapsImageSideLabelsByDefault()
        {
            PoseFrameBuilder.SwapLeftRight = true;
            var world = World(shoulderDy: 0.1f);   // raw label "left" shoulder lower
            var vis = new float[PoseFrameBuilder.LandmarkCount];
            for (var i = 0; i < vis.Length; i++) vis[i] = 1f;
            vis[PoseFrameBuilder.LeftWrist] = 0.2f;
            var p = PoseFrameBuilder.Build(0, world, vis).Pose.Value;
            Assert.That(p.RightShoulderY, Is.GreaterThan(p.LeftShoulderY), "the raw 'left' shoulder became the real right");
            Assert.That(p.RightWristVisibility, Is.EqualTo(0.2f), "visibility follows the swap");
            PoseFrameBuilder.ComputeAngles(p, out var r, out _, out _);
            Assert.That(r, Is.LessThan(0f), "real right shoulder lower = lean to the right = negative roll");
        }

        [Test]
        public void BodyInvertSwitchesFlipEachAxis()
        {
            var settings = new BodyTrackingSettings { Smoothing = 0f, DeadZoneDeg = 0f, InvertRoll = true };
            var solver = new BodyPoseSolver(settings);
            var frame = PoseFrameBuilder.Build(0, World(shoulderDy: 0.1f, shoulderDz: 0.1f, lean: 0.1f), null);
            PoseFrameBuilder.ComputeAngles(frame.Pose.Value, out var r, out var y, out var pch);
            BodyPose pose = default;
            for (var i = 0; i < 5; i++) { solver.Submit(frame); pose = solver.Update(1f / 60f, 0.01 * i, mirrorUser: true); }
            Assert.That(pose.RollRad, Is.EqualTo(r).Within(1e-4f), "inverted roll");
            Assert.That(pose.YawRad, Is.EqualTo(y).Within(1e-4f), "yaw untouched");
            Assert.That(pose.PitchRad, Is.EqualTo(pch).Within(1e-4f));
            settings.InvertYaw = true; settings.InvertPitch = true;
            pose = solver.Update(1f / 60f, 0.06, mirrorUser: true);
            Assert.That(pose.YawRad, Is.EqualTo(-y).Within(1e-4f));
            Assert.That(pose.PitchRad, Is.EqualTo(-pch).Within(1e-4f));
        }

        [Test]
        public void BodySolverMirrorsClampsAndReturnsToNeutral()
        {
            var settings = new BodyTrackingSettings { Smoothing = 0f, DeadZoneDeg = 0f, LostTimeoutSeconds = 0.2f };
            var solver = new BodyPoseSolver(settings);
            var frame = PoseFrameBuilder.Build(0, World(shoulderDy: 0.1f, shoulderDz: 0.1f), null);
            PoseFrameBuilder.ComputeAngles(frame.Pose.Value, out var r, out var y, out _);

            BodyPose pose = default;
            for (var i = 0; i < 5; i++) { solver.Submit(frame); pose = solver.Update(1f / 60f, 0.01 * i, mirrorUser: true); }
            Assert.That(pose.HasBody, Is.True);
            Assert.That(pose.RollRad, Is.EqualTo(-r).Within(1e-4f));
            Assert.That(pose.YawRad, Is.EqualTo(y).Within(1e-4f));

            pose = solver.Update(1f / 60f, 0.05, mirrorUser: false);
            Assert.That(pose.RollRad, Is.EqualTo(r).Within(1e-4f));
            Assert.That(pose.YawRad, Is.EqualTo(-y).Within(1e-4f));

            // Clamp: a huge tilt is limited.
            var extreme = PoseFrameBuilder.Build(1, World(shoulderDy: 2f), null);
            solver.Submit(extreme);
            pose = solver.Update(1f / 60f, 1.0, false);
            Assert.That(Math.Abs(pose.RollRad * HeadPoseMath.Rad2Deg), Is.EqualTo(settings.MaxRollDeg).Within(1e-3f));

            // Lost: glides to zero.
            for (var i = 0; i < 240; i++) pose = solver.Update(1f / 60f, 2.0 + i / 60.0, false);
            Assert.That(pose.HasBody, Is.False);
            Assert.That(pose.RollRad, Is.EqualTo(0f).Within(1e-2f));
        }

        [Test]
        public void BodyCalibrationStoresMedianNeutral()
        {
            var settings = new BodyTrackingSettings { Smoothing = 0f, DeadZoneDeg = 0f };
            var solver = new BodyPoseSolver(settings);
            var tilted = PoseFrameBuilder.Build(0, World(shoulderDy: 0.1f), null);
            PoseFrameBuilder.ComputeAngles(tilted.Pose.Value, out var r, out _, out _);

            bool? ended = null;
            solver.CalibrationEnded += ok => ended = ok;
            solver.StartCalibration(0, 0.3f);
            for (var i = 0; i < 10; i++) { var f = tilted; f.Timestamp = i * 0.05; solver.Submit(f); solver.Update(0.05f, i * 0.05, true); }
            Assert.That(ended, Is.True);
            Assert.That(settings.NeutralRollRad, Is.EqualTo(r).Within(1e-5f));

            // After calibration the tilted pose reads as neutral.
            solver.Submit(tilted);
            var pose = solver.Update(1f / 60f, 1.0, true);
            Assert.That(pose.RollRad, Is.EqualTo(0f).Within(1e-4f));
        }
    }
}
