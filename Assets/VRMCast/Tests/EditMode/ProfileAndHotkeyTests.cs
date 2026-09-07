using System.Collections.Generic;
using NUnit.Framework;
using VRMCast.Core.Backgrounds;
using VRMCast.Core.Camera;
using VRMCast.Core.Hotkeys;
using VRMCast.Core.Profiles;
using VRMCast.Core.Rendering;
using VRMCast.Core.Tracking;

namespace VRMCast.Core.Tests
{
    public class ProfileAndHotkeyTests
    {
        [Test]
        public void TrackingRoundTrips()
        {
            var face = new FaceTrackingSettings { Mode = FaceTrackingMode.Advanced, MirrorUser = false, HeadSmoothing = 0.2f, HeadGain = 1.5f, BlinkGain = 2.2f, InvertRoll = true };
            face.Calibration = new CalibrationData(true, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f);
            var body = new BodyTrackingSettings { Mode = BodyTrackingMode.Off, Smoothing = 0.3f, NeutralYawRad = 0.25f };

            var p = new ProfileData();
            ProfileMapper.CaptureTracking(p, face, body, trackingEnabled: true);

            var face2 = new FaceTrackingSettings();
            var body2 = new BodyTrackingSettings();
            ProfileMapper.ApplyTracking(p, face2, body2);

            Assert.That(face2.Mode, Is.EqualTo(FaceTrackingMode.Advanced));
            Assert.That(face2.MirrorUser, Is.False);
            Assert.That(face2.HeadSmoothing, Is.EqualTo(0.2f));
            Assert.That(face2.HeadGain, Is.EqualTo(1.5f));
            Assert.That(face2.BlinkGain, Is.EqualTo(2.2f));
            Assert.That(face2.InvertRoll, Is.True);
            Assert.That(face2.Calibration.IsCalibrated, Is.True);
            Assert.That(face2.Calibration.YawRad, Is.EqualTo(0.2f));
            Assert.That(face2.Calibration.Smile, Is.EqualTo(0.7f));
            Assert.That(body2.Mode, Is.EqualTo(BodyTrackingMode.Off));
            Assert.That(body2.NeutralYawRad, Is.EqualTo(0.25f));
            Assert.That(p.trackingEnabled, Is.True);
        }

        [Test]
        public void MappingsLipSyncCameraBackgroundOutputRoundTrip()
        {
            var p = new ProfileData();

            var mappings = new List<ExpressionMapping> { new ExpressionMapping("jawOpen", "aa", gain: 2f, threshold: 0.1f) { Invert = true, Enabled = false } };
            ProfileMapper.CaptureMappings(p, mappings, useDefaults: false);
            var back = ProfileMapper.ToMappings(p);
            Assert.That(back.Count, Is.EqualTo(1));
            Assert.That(back[0].Gain, Is.EqualTo(2f));
            Assert.That(back[0].Invert, Is.True);
            Assert.That(back[0].Enabled, Is.False);
            p.useDefaultMappings = true;
            Assert.That(ProfileMapper.ToMappings(p).Count, Is.EqualTo(ExpressionMappingDefaults.Basic().Count));

            var lip = new LipSyncSettings { Mode = LipSyncMode.Microphone, CameraWeight = 0.3f };
            lip.Audio.GateDb = -30f;
            ProfileMapper.CaptureLipSync(p, lip);
            var lip2 = new LipSyncSettings();
            ProfileMapper.ApplyLipSync(p, lip2);
            Assert.That(lip2.Mode, Is.EqualTo(LipSyncMode.Microphone));
            Assert.That(lip2.CameraWeight, Is.EqualTo(0.3f));
            Assert.That(lip2.Audio.GateDb, Is.EqualTo(-30f));

            var cam = new AvatarCameraState { Preset = FramingPreset.FullBody, Zoom = 2f, PanX = 0.1f, OrbitYawDeg = 15f, FovDeg = 45f };
            ProfileMapper.CaptureCamera(p, cam);
            var cam2 = new AvatarCameraState();
            ProfileMapper.ApplyCamera(p, cam2);
            Assert.That(cam2.Preset, Is.EqualTo(FramingPreset.FullBody));
            Assert.That(cam2.Zoom, Is.EqualTo(2f));
            Assert.That(cam2.OrbitYawDeg, Is.EqualTo(15f));
            Assert.That(cam2.FovDeg, Is.EqualTo(45f));

            var bg = new BackgroundSettings { Mode = BackgroundMode.Image, ImagePath = "/tmp/x.png", ImageFit = ImageFitMode.Fit, ChromaColor = RgbaColor.ParseHex("#112233") };
            ProfileMapper.CaptureBackground(p, bg);
            var bg2 = new BackgroundSettings();
            ProfileMapper.ApplyBackground(p, bg2);
            Assert.That(bg2.Mode, Is.EqualTo(BackgroundMode.Image));
            Assert.That(bg2.ImagePath, Is.EqualTo("/tmp/x.png"));
            Assert.That(bg2.ImageFit, Is.EqualTo(ImageFitMode.Fit));
            Assert.That(bg2.ChromaColor.ToHex(), Is.EqualTo("#112233"));

            ProfileMapper.CaptureOutput(p, new OutputSettings(1280, 720, 60));
            Assert.That(ProfileMapper.ToOutput(p), Is.EqualTo(new OutputSettings(1280, 720, 60)));
            p.output.width = 0;
            Assert.That(ProfileMapper.ToOutput(p), Is.EqualTo(OutputSettings.Default), "bad values fall back to the default");
        }

        [Test]
        public void HotkeysRoundTripAndDefaults()
        {
            var p = new ProfileData();
            Assert.That(ProfileMapper.ToHotkeys(p).Count, Is.EqualTo(HotkeyBinding.Defaults().Count), "empty list uses defaults");
            ProfileMapper.CaptureHotkeys(p, new[] { new HotkeyBinding { Key = "F1", Expression = "happy", Intensity = 0.5f, Mode = HotkeyTriggerMode.OneShot } });
            var back = ProfileMapper.ToHotkeys(p);
            Assert.That(back.Count, Is.EqualTo(1));
            Assert.That(back[0].Key, Is.EqualTo("F1"));
            Assert.That(back[0].Intensity, Is.EqualTo(0.5f));
            Assert.That(back[0].Mode, Is.EqualTo(HotkeyTriggerMode.OneShot));
        }

        [Test]
        public void OutOfRangeEnumsAreClamped()
        {
            var p = new ProfileData { faceMode = 9, bodyMode = -3 };
            p.background.mode = 42;
            var face = new FaceTrackingSettings();
            var body = new BodyTrackingSettings();
            ProfileMapper.ApplyTracking(p, face, body);
            Assert.That(face.Mode, Is.EqualTo(FaceTrackingMode.Advanced));
            Assert.That(body.Mode, Is.EqualTo(BodyTrackingMode.Off));
            var bg = new BackgroundSettings();
            ProfileMapper.ApplyBackground(p, bg);
            Assert.That(bg.Mode, Is.EqualTo(BackgroundMode.Transparent));
        }

        [Test]
        public void SanitizeNameStripsPathCharacters()
        {
            Assert.That(ProfileMapper.SanitizeName("  my/profile:1  "), Is.EqualTo("my_profile_1"));
            Assert.That(ProfileMapper.SanitizeName(""), Is.EqualTo(ProfileData.DefaultName));
            Assert.That(ProfileMapper.SanitizeName(new string('a', 100)).Length, Is.EqualTo(60));
        }

        // ---------------------------------------------------------------- hotkey state

        private static float Settle(HotkeyState state, float seconds = 1f)
        {
            IReadOnlyDictionary<string, float> w = null;
            for (var i = 0; i < seconds * 60; i++) w = state.Update(1f / 60f);
            return w != null && w.TryGetValue("happy", out var v) ? v : 0f;
        }

        [Test]
        public void HoldIsActiveOnlyWhilePressed()
        {
            var state = new HotkeyState(new[] { new HotkeyBinding { Key = "A", Expression = "happy", Mode = HotkeyTriggerMode.Hold } });
            state.KeyDown("A");
            Assert.That(Settle(state), Is.GreaterThan(0.99f));
            state.KeyUp("A");
            Assert.That(Settle(state), Is.EqualTo(0f));
        }

        [Test]
        public void ToggleFlipsOnEachPress()
        {
            var state = new HotkeyState(new[] { new HotkeyBinding { Key = "A", Expression = "happy", Mode = HotkeyTriggerMode.Toggle, Intensity = 0.6f } });
            state.KeyDown("A"); state.KeyUp("A");
            Assert.That(Settle(state), Is.EqualTo(0.6f).Within(1e-3f));
            state.KeyDown("A"); state.KeyUp("A");
            Assert.That(Settle(state), Is.EqualTo(0f));
        }

        [Test]
        public void OneShotReleasesByItselfAndEasesIn()
        {
            var state = new HotkeyState(new[] { new HotkeyBinding { Key = "A", Expression = "happy", Mode = HotkeyTriggerMode.OneShot } });
            state.KeyDown("A"); state.KeyUp("A");
            var first = state.Update(1f / 60f)["happy"];
            Assert.That(first, Is.GreaterThan(0f).And.LessThan(0.5f), "eases in rather than snapping");
            Assert.That(Settle(state, 0.5f), Is.GreaterThan(0.95f));
            Assert.That(Settle(state, 2f), Is.EqualTo(0f));
        }

        [Test]
        public void SameExpressionFromTwoKeysUsesMaxAndReleaseAllClears()
        {
            var state = new HotkeyState(new[]
            {
                new HotkeyBinding { Key = "A", Expression = "happy", Mode = HotkeyTriggerMode.Toggle, Intensity = 0.4f },
                new HotkeyBinding { Key = "B", Expression = "happy", Mode = HotkeyTriggerMode.Toggle, Intensity = 0.9f },
                new HotkeyBinding { Key = "", Expression = "sad", Mode = HotkeyTriggerMode.Hold },
            });
            state.KeyDown("A"); state.KeyDown("B");
            Assert.That(Settle(state), Is.EqualTo(0.9f).Within(1e-3f));
            Assert.That(state.AnyActive, Is.True);
            state.ReleaseAll();
            Assert.That(Settle(state), Is.EqualTo(0f));
            Assert.That(state.AnyActive, Is.False);
        }

        [Test]
        public void BlinkGainBoostsBlinkOnly()
        {
            var settings = new FaceTrackingSettings { ExpressionSmoothing = 0f, BlinkGain = 2f, MirrorUser = false };
            var solver = new MotionSolver(settings, new ExpressionMapper(new[]
            {
                new ExpressionMapping(MediaPipeBlendshapes.EyeBlinkLeft, VrmExpressions.BlinkLeft, smoothing: 0f),
                new ExpressionMapping(MediaPipeBlendshapes.JawOpen, VrmExpressions.Aa, smoothing: 0f),
            }));
            var frame = TrackingFrame.Empty(0);
            frame.FaceConfidence = 1f;
            frame.Blendshapes = new Dictionary<string, float> { [MediaPipeBlendshapes.EyeBlinkLeft] = 0.4f, [MediaPipeBlendshapes.JawOpen] = 0.4f };
            solver.Submit(frame);
            var pose = solver.Update(1f / 60f, 0);
            Assert.That(pose.Expressions[VrmExpressions.BlinkLeft], Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(pose.Expressions[VrmExpressions.Aa], Is.EqualTo(0.4f).Within(1e-4f));
        }
    }
}
