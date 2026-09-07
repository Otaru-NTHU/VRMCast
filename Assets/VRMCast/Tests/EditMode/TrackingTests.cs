using System;
using System.Collections.Generic;
using NUnit.Framework;
using VRMCast.Core.Tracking;

namespace VRMCast.Core.Tests
{
    public class TrackingTests
    {
        private static Dictionary<string, float> Shapes(params (string name, float value)[] values)
        {
            var d = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var (name, value) in values) d[name] = value;
            return d;
        }

        private static TrackingFrame FaceFrame(double t, float pitch = 0, float yaw = 0, float roll = 0, Dictionary<string, float> shapes = null)
        {
            var f = TrackingFrame.Empty(t);
            f.FaceConfidence = 1f;
            f.Head.PitchRad = pitch;
            f.Head.YawRad = yaw;
            f.Head.RollRad = roll;
            f.Blendshapes = shapes ?? new Dictionary<string, float>();
            f.Eyes.BlinkLeft = MediaPipeBlendshapes.Get(f.Blendshapes, MediaPipeBlendshapes.EyeBlinkLeft);
            f.Eyes.LookX = FaceFrameBuilder.ComputeLookX(f.Blendshapes);
            f.Mouth.Open = MediaPipeBlendshapes.Get(f.Blendshapes, MediaPipeBlendshapes.JawOpen);
            return f;
        }

        private static AvatarPose Settle(MotionSolver solver, in TrackingFrame frame, int frames = 240, float dt = 1f / 60f)
        {
            AvatarPose pose = null;
            var t = frame.Timestamp;
            for (var i = 0; i < frames; i++)
            {
                var f = frame;
                f.Timestamp = t;
                solver.Submit(f);
                pose = solver.Update(dt, t);
                t += dt;
            }
            return pose;
        }

        // ---------------------------------------------------------------- head pose math

        [Test]
        public void FacingCameraGivesZeroAngles()
        {
            HeadPoseMath.ExtractAngles(0, 0, -1, 0, 1, 0, out var pitch, out var yaw, out var roll);
            Assert.That(pitch, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(yaw, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(roll, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void HeadAngleSignsFollowContract()
        {
            // Nose toward image +X = user turned to their left = positive yaw.
            HeadPoseMath.ExtractAngles(0.5f, 0, -0.866f, 0, 1, 0, out _, out var yaw, out _);
            Assert.That(yaw, Is.EqualTo(30f * HeadPoseMath.Deg2Rad).Within(1e-2f));

            // Looking down: with the plugin's converted basis the forward axis gains positive y = positive pitch.
            HeadPoseMath.ExtractAngles(0, 0.5f, -0.866f, 0, 0.866f, 0.5f, out var pitch, out _, out _);
            Assert.That(pitch, Is.EqualTo(30f * HeadPoseMath.Deg2Rad).Within(1e-2f));

            // Up axis leaning toward image +X = clockwise as seen by the camera = positive roll.
            HeadPoseMath.ExtractAngles(0, 0, -1, 0.5f, 0.866f, 0, out _, out _, out var roll);
            Assert.That(roll, Is.EqualTo(30f * HeadPoseMath.Deg2Rad).Within(1e-2f));
        }

        // ---------------------------------------------------------------- frame builder

        [Test]
        public void FrameBuilderFillsEyesAndMouthFromBlendshapes()
        {
            var shapes = Shapes(
                (MediaPipeBlendshapes.EyeBlinkLeft, 0.9f), (MediaPipeBlendshapes.EyeBlinkRight, 0.1f),
                (MediaPipeBlendshapes.JawOpen, 0.6f), (MediaPipeBlendshapes.MouthSmileLeft, 0.2f), (MediaPipeBlendshapes.MouthSmileRight, 0.7f),
                (MediaPipeBlendshapes.EyeLookOutRight, 0.8f), (MediaPipeBlendshapes.EyeLookInLeft, 0.8f),
                (MediaPipeBlendshapes.EyeLookUpLeft, 0.5f), (MediaPipeBlendshapes.EyeLookUpRight, 0.5f));
            var frame = FaceFrameBuilder.Build(1.5, shapes, new[] { 0f, 0f, -1f }, new[] { 0f, 1f, 0f }, new[] { 0.1f, 0.2f, 0.6f });

            Assert.That(frame.Timestamp, Is.EqualTo(1.5));
            Assert.That(frame.FaceConfidence, Is.EqualTo(1f));
            Assert.That(frame.Eyes.BlinkLeft, Is.EqualTo(0.9f));
            Assert.That(frame.Eyes.BlinkRight, Is.EqualTo(0.1f));
            Assert.That(frame.Mouth.Open, Is.EqualTo(0.6f));
            Assert.That(frame.Mouth.Smile, Is.EqualTo(0.7f));
            Assert.That(frame.Eyes.LookX, Is.EqualTo(0.8f).Within(1e-5f), "looking right is positive");
            Assert.That(frame.Eyes.LookY, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(frame.Head.PositionZ, Is.EqualTo(0.6f));
            Assert.That(FaceFrameBuilder.NoFace(2).FaceConfidence, Is.EqualTo(0f));
        }

        // ---------------------------------------------------------------- calibration

        [Test]
        public void CalibrationTakesMedianAndRejectsLowConfidence()
        {
            var sampler = new CalibrationSampler(0.5f);
            sampler.Start();
            var t = 0.0;
            var pitches = new[] { 0.10f, 0.12f, 0.11f, 0.90f, 0.10f, 0.12f, 0.11f, 0.10f, 0.12f, 0.11f };
            var completed = false;
            for (var i = 0; i < pitches.Length; i++)
            {
                var f = FaceFrame(t, pitch: pitches[i]);
                if (i == 3) f.FaceConfidence = 0.1f; // the outlier is also low confidence
                completed = sampler.Add(f);
                t += 0.06;
            }
            Assert.That(completed, Is.True);
            Assert.That(sampler.RejectedFrames, Is.EqualTo(1));
            Assert.That(sampler.Result, Is.Not.Null);
            Assert.That(sampler.Result.IsCalibrated, Is.True);
            Assert.That(sampler.Result.PitchRad, Is.EqualTo(0.11f).Within(1e-5f));
        }

        [Test]
        public void CalibrationFailsWithTooFewFrames()
        {
            var sampler = new CalibrationSampler(0.2f);
            sampler.Start();
            sampler.Add(FaceFrame(0));
            var completed = sampler.Add(FaceFrame(0.3));
            Assert.That(completed, Is.True);
            Assert.That(sampler.Result, Is.Null);
            Assert.That(sampler.IsRunning, Is.False);
        }

        // ---------------------------------------------------------------- expression mapper

        [Test]
        public void MappingShapeAppliesThresholdGainAndInvert()
        {
            var m = new ExpressionMapping("a", "b", gain: 2f, threshold: 0.5f);
            Assert.That(m.Shape(0.4f), Is.EqualTo(0f));
            Assert.That(m.Shape(0.75f), Is.EqualTo(1f));
            Assert.That(m.Shape(1f), Is.EqualTo(1f));
            m.Gain = 1f;
            Assert.That(m.Shape(0.75f), Is.EqualTo(0.5f).Within(1e-5f));
            m.Invert = true;
            Assert.That(m.Shape(0.25f), Is.EqualTo(0.5f).Within(1e-5f));
            m.Max = 0.3f;
            Assert.That(m.Shape(0f), Is.EqualTo(0.3f).Within(1e-5f));
        }

        [Test]
        public void MapperMergesMultipleSourcesWithMax()
        {
            var mapper = new ExpressionMapper(new[]
            {
                new ExpressionMapping(MediaPipeBlendshapes.MouthSmileLeft, VrmExpressions.Happy, smoothing: 0f),
                new ExpressionMapping(MediaPipeBlendshapes.MouthSmileRight, VrmExpressions.Happy, smoothing: 0f),
                new ExpressionMapping(MediaPipeBlendshapes.JawOpen, VrmExpressions.Aa, smoothing: 0f) { Enabled = false },
            });
            var output = mapper.Evaluate(Shapes((MediaPipeBlendshapes.MouthSmileLeft, 0.2f), (MediaPipeBlendshapes.MouthSmileRight, 0.8f), (MediaPipeBlendshapes.JawOpen, 1f)), 1f / 60f);
            Assert.That(output[VrmExpressions.Happy], Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(output.ContainsKey(VrmExpressions.Aa), Is.False, "disabled rows produce nothing");
        }

        [Test]
        public void MapperSmoothsOverTime()
        {
            var mapper = new ExpressionMapper(new[] { new ExpressionMapping("x", "y", smoothing: 1f) }) { MaxSmoothingSeconds = 0.25f };
            var first = mapper.Evaluate(Shapes(("x", 1f)), 1f / 60f)["y"];
            Assert.That(first, Is.GreaterThan(0f).And.LessThan(0.2f));
            float last = first;
            for (var i = 0; i < 120; i++) last = mapper.Evaluate(Shapes(("x", 1f)), 1f / 60f)["y"];
            Assert.That(last, Is.GreaterThan(0.99f));

            for (var i = 0; i < 120; i++) last = mapper.Evaluate(null, 1f / 60f)["y"];
            Assert.That(last, Is.LessThan(0.01f), "null input decays to zero");
        }

        [Test]
        public void DefaultTablesOnlyUseKnownNames()
        {
            var sources = new HashSet<string>(MediaPipeBlendshapes.All);
            var destinations = new HashSet<string>(VrmExpressions.Presets);
            foreach (var m in ExpressionMappingDefaults.Advanced())
            {
                Assert.That(sources.Contains(m.Source), m.Source);
                Assert.That(destinations.Contains(m.Destination), m.Destination);
            }
            Assert.That(ExpressionMappingDefaults.Advanced().Count, Is.GreaterThan(ExpressionMappingDefaults.Basic().Count));
        }

        [Test]
        public void Vrm0NamesAndMirrorTable()
        {
            Assert.That(VrmExpressions.ToVrm0Preset(VrmExpressions.Happy), Is.EqualTo("Joy"));
            Assert.That(VrmExpressions.ToVrm0Preset(VrmExpressions.BlinkLeft), Is.EqualTo("Blink_L"));
            Assert.That(VrmExpressions.ToVrm0Preset(VrmExpressions.Surprised), Is.Null);
            Assert.That(VrmExpressions.Mirror(VrmExpressions.BlinkLeft), Is.EqualTo(VrmExpressions.BlinkRight));
            Assert.That(VrmExpressions.Mirror(VrmExpressions.LookRight), Is.EqualTo(VrmExpressions.LookLeft));
            Assert.That(VrmExpressions.Mirror(VrmExpressions.Aa), Is.EqualTo(VrmExpressions.Aa));
        }

        // ---------------------------------------------------------------- motion solver

        [Test]
        public void SolverMirrorsYawAndRollButNotPitch()
        {
            var settings = new FaceTrackingSettings { HeadSmoothing = 0f, HeadDeadZoneDeg = 0f, MirrorUser = true };
            var solver = new MotionSolver(settings);
            var pose = Settle(solver, FaceFrame(0, pitch: 0.2f, yaw: 0.3f, roll: 0.1f), frames: 5);
            Assert.That(pose.HasFace, Is.True);
            Assert.That(pose.HeadPitchRad, Is.EqualTo(0.2f).Within(1e-3f));
            Assert.That(pose.HeadYawRad, Is.EqualTo(0.3f).Within(1e-3f), "mirror: user's left becomes avatar's left on screen");
            Assert.That(pose.HeadRollRad, Is.EqualTo(-0.1f).Within(1e-3f));

            settings.MirrorUser = false;
            pose = Settle(solver, FaceFrame(1, pitch: 0.2f, yaw: 0.3f, roll: 0.1f), frames: 5);
            Assert.That(pose.HeadYawRad, Is.EqualTo(-0.3f).Within(1e-3f));
            Assert.That(pose.HeadRollRad, Is.EqualTo(0.1f).Within(1e-3f));
            Assert.That(pose.HeadPitchRad, Is.EqualTo(0.2f).Within(1e-3f));
        }

        [Test]
        public void SolverAppliesCalibrationDeadZoneGainAndInvert()
        {
            var settings = new FaceTrackingSettings { HeadSmoothing = 0f, HeadDeadZoneDeg = 2f, HeadGain = 2f, MirrorUser = true };
            settings.Calibration = new CalibrationData(true, 0.1f, 0f, 0f, 0f, 0f, 0f, 0f);
            var solver = new MotionSolver(settings);

            // Within the dead zone after calibration: nothing.
            var pose = Settle(solver, FaceFrame(0, pitch: 0.1f + 1f * HeadPoseMath.Deg2Rad), frames: 3);
            Assert.That(pose.HeadPitchRad, Is.EqualTo(0f).Within(1e-4f));

            // 12° above neutral: (12 - 2) * 2 = 20°.
            pose = Settle(solver, FaceFrame(1, pitch: 0.1f + 12f * HeadPoseMath.Deg2Rad), frames: 3);
            Assert.That(pose.HeadPitchRad * HeadPoseMath.Rad2Deg, Is.EqualTo(20f).Within(1e-2f));

            settings.InvertPitch = true;
            pose = Settle(solver, FaceFrame(2, pitch: 0.1f + 12f * HeadPoseMath.Deg2Rad), frames: 3);
            Assert.That(pose.HeadPitchRad * HeadPoseMath.Rad2Deg, Is.EqualTo(-20f).Within(1e-2f));
        }

        [Test]
        public void SolverClampsExtremeAngles()
        {
            var settings = new FaceTrackingSettings { HeadSmoothing = 0f, HeadDeadZoneDeg = 0f, HeadGain = 3f };
            var solver = new MotionSolver(settings);
            var pose = Settle(solver, FaceFrame(0, yaw: 1.2f), frames: 3);
            Assert.That(Math.Abs(pose.HeadYawRad * HeadPoseMath.Rad2Deg), Is.EqualTo(FaceTrackingSettings.MaxHeadAngleDeg).Within(1e-2f));
        }

        [Test]
        public void SolverReturnsToNeutralWhenFaceIsLost()
        {
            var settings = new FaceTrackingSettings { HeadSmoothing = 0f, HeadDeadZoneDeg = 0f, LostTimeoutSeconds = 0.2f, ReturnToNeutralSeconds = 0.6f };
            var solver = new MotionSolver(settings);
            var pose = Settle(solver, FaceFrame(0, yaw: 0.5f, shapes: Shapes((MediaPipeBlendshapes.JawOpen, 1f))), frames: 60);
            Assert.That(pose.HeadYawRad, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(pose.Expressions[VrmExpressions.Aa], Is.GreaterThan(0.9f));

            // No new frames: still tracking within the timeout...
            var t = 1.0;
            pose = solver.Update(1f / 60f, t + 0.1);
            Assert.That(pose.HasFace, Is.True);
            Assert.That(pose.HeadYawRad, Is.EqualTo(0.5f).Within(1e-3f));

            // ...then glides toward neutral rather than snapping.
            pose = solver.Update(1f / 60f, t + 0.3);
            Assert.That(pose.HasFace, Is.False);
            Assert.That(pose.HeadYawRad, Is.GreaterThan(0.3f), "first lost frame must not snap");
            for (var i = 0; i < 180; i++) pose = solver.Update(1f / 60f, t + 0.3 + i / 60.0);
            Assert.That(pose.HeadYawRad, Is.EqualTo(0f).Within(1e-2f));
            Assert.That(pose.Expressions[VrmExpressions.Aa], Is.LessThan(0.02f));
        }

        [Test]
        public void SolverMirrorsBlinkSidesAndLook()
        {
            var settings = new FaceTrackingSettings { HeadSmoothing = 0f, LookSmoothing = 0f, MirrorUser = true, LookGain = 1f };
            var solver = new MotionSolver(settings);
            var shapes = Shapes((MediaPipeBlendshapes.EyeBlinkLeft, 1f), (MediaPipeBlendshapes.EyeLookOutRight, 1f), (MediaPipeBlendshapes.EyeLookInLeft, 1f));
            var pose = Settle(solver, FaceFrame(0, shapes: shapes), frames: 120);
            Assert.That(pose.Expressions[VrmExpressions.BlinkRight], Is.GreaterThan(0.9f), "user's left eye drives the avatar's right eye in mirror mode");
            Assert.That(pose.Expressions.ContainsKey(VrmExpressions.BlinkLeft) ? pose.Expressions[VrmExpressions.BlinkLeft] : 0f, Is.LessThan(0.01f));
            Assert.That(pose.LookYawDeg, Is.EqualTo(-settings.LookYawRangeDeg).Within(1e-3f), "user looks right -> avatar looks to its left");

            settings.MirrorUser = false;
            pose = Settle(solver, FaceFrame(3, shapes: shapes), frames: 120);
            Assert.That(pose.Expressions[VrmExpressions.BlinkLeft], Is.GreaterThan(0.9f));
            Assert.That(pose.LookYawDeg, Is.EqualTo(settings.LookYawRangeDeg).Within(1e-3f));
        }

        [Test]
        public void SolverSubtractsCalibratedMouthBaseline()
        {
            var settings = new FaceTrackingSettings { ExpressionSmoothing = 0f };
            settings.Calibration = new CalibrationData(true, 0, 0, 0, 0, 0, mouthOpen: 0.2f, smile: 0f);
            var solver = new MotionSolver(settings, new ExpressionMapper(new[] { new ExpressionMapping(MediaPipeBlendshapes.JawOpen, VrmExpressions.Aa, smoothing: 0f) }));
            var resting = Settle(solver, FaceFrame(0, shapes: Shapes((MediaPipeBlendshapes.JawOpen, 0.2f))), frames: 3);
            Assert.That(resting.Expressions[VrmExpressions.Aa], Is.EqualTo(0f).Within(1e-5f), "mouth closes at rest after calibration (PRD 37.3)");
            var open = Settle(solver, FaceFrame(1, shapes: Shapes((MediaPipeBlendshapes.JawOpen, 1f))), frames: 3);
            Assert.That(open.Expressions[VrmExpressions.Aa], Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void TrackingStatsCountAndAverage()
        {
            var stats = new TrackingStats();
            stats.OnSubmitted(); stats.OnSubmitted(); stats.OnSubmitted();
            stats.OnResult(1.0, 0.010, true);
            stats.OnResult(1.05, 0.030, false);
            Assert.That(stats.Submitted, Is.EqualTo(3));
            Assert.That(stats.Results, Is.EqualTo(2));
            Assert.That(stats.Dropped, Is.EqualTo(1));
            Assert.That(stats.FacesSeen, Is.EqualTo(1));
            Assert.That(stats.InferenceMs, Is.EqualTo(20.0).Within(1e-6));
            stats.Reset();
            Assert.That(stats.Submitted, Is.EqualTo(0));
        }

        [Test]
        public void SmoothingHelpers()
        {
            Assert.That(SmoothingMath.DeadZone(0.5f, 1f), Is.EqualTo(0f));
            Assert.That(SmoothingMath.DeadZone(3f, 1f), Is.EqualTo(2f));
            Assert.That(SmoothingMath.DeadZone(-3f, 1f), Is.EqualTo(-2f));
            Assert.That(SmoothingMath.Tau(0.5f, 0.25f), Is.EqualTo(0.125f).Within(1e-6f));
            var s = new ExponentialSmoother();
            Assert.That(s.Update(1f, 1f / 60f, 0f), Is.EqualTo(1f), "tau 0 snaps");
            s.Reset(0f);
            var v = s.Update(1f, 0.1f, 0.1f);
            Assert.That(v, Is.EqualTo(1f - (float)Math.Exp(-1)).Within(1e-5f));
        }
    }
}
