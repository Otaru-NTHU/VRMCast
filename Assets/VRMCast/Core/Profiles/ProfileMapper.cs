using System;
using System.Collections.Generic;
using VRMCast.Core.Backgrounds;
using VRMCast.Core.Camera;
using VRMCast.Core.Hotkeys;
using VRMCast.Core.Rendering;
using VRMCast.Core.Tracking;

namespace VRMCast.Core.Profiles
{
    /// <summary>Pure conversions between <see cref="ProfileData"/> and the live settings objects. No engine types.</summary>
    public static class ProfileMapper
    {
        // ------------------------------------------------------------- tracking

        public static void ApplyTracking(ProfileData p, FaceTrackingSettings face, BodyTrackingSettings body)
        {
            face.Mode = (FaceTrackingMode)Clamp(p.faceMode, 0, 1);
            face.MirrorUser = p.tracking.mirrorUser;
            face.HeadSmoothing = p.tracking.headSmoothing;
            face.ExpressionSmoothing = p.tracking.expressionSmoothing;
            face.LookSmoothing = p.tracking.lookSmoothing;
            face.HeadGain = p.tracking.headGain;
            face.HeadDeadZoneDeg = p.tracking.headDeadZoneDeg;
            face.BlinkGain = p.tracking.blinkGain;
            face.InvertPitch = p.tracking.invertPitch;
            face.InvertYaw = p.tracking.invertYaw;
            face.InvertRoll = p.tracking.invertRoll;
            face.LookGain = p.tracking.lookGain;
            var c = p.calibration;
            face.Calibration = c.isCalibrated
                ? new CalibrationData(true, c.pitchRad, c.yawRad, c.rollRad, c.lookX, c.lookY, c.mouthOpen, c.smile)
                : CalibrationData.Identity;

            body.Mode = (BodyTrackingMode)Clamp(p.bodyMode, 0, 2);
            body.Smoothing = p.body.smoothing;
            body.Gain = p.body.gain;
            body.NeutralRollRad = c.bodyRollRad;
            body.NeutralYawRad = c.bodyYawRad;
            body.NeutralPitchRad = c.bodyPitchRad;
            face.Clamp();
            body.Clamp();
        }

        public static void CaptureTracking(ProfileData p, FaceTrackingSettings face, BodyTrackingSettings body, bool trackingEnabled)
        {
            p.faceMode = (int)face.Mode;
            p.bodyMode = (int)body.Mode;
            p.trackingEnabled = trackingEnabled;
            p.tracking = new TrackingTuningData
            {
                mirrorUser = face.MirrorUser,
                headSmoothing = face.HeadSmoothing,
                expressionSmoothing = face.ExpressionSmoothing,
                lookSmoothing = face.LookSmoothing,
                headGain = face.HeadGain,
                headDeadZoneDeg = face.HeadDeadZoneDeg,
                blinkGain = face.BlinkGain,
                invertPitch = face.InvertPitch,
                invertYaw = face.InvertYaw,
                invertRoll = face.InvertRoll,
                lookGain = face.LookGain,
            };
            var c = face.Calibration ?? CalibrationData.Identity;
            p.calibration = new FaceCalibrationData
            {
                isCalibrated = c.IsCalibrated,
                pitchRad = c.PitchRad, yawRad = c.YawRad, rollRad = c.RollRad,
                lookX = c.LookX, lookY = c.LookY, mouthOpen = c.MouthOpen, smile = c.Smile,
                bodyRollRad = body.NeutralRollRad, bodyYawRad = body.NeutralYawRad, bodyPitchRad = body.NeutralPitchRad,
            };
            p.body = new BodyTuningData { smoothing = body.Smoothing, gain = body.Gain };
        }

        // ------------------------------------------------------------- mappings

        public static List<ExpressionMapping> ToMappings(ProfileData p)
        {
            if (p.useDefaultMappings || p.mappings == null || p.mappings.Count == 0)
            {
                return ExpressionMappingDefaults.For((FaceTrackingMode)Clamp(p.faceMode, 0, 1));
            }
            var list = new List<ExpressionMapping>(p.mappings.Count);
            foreach (var m in p.mappings)
            {
                if (m == null) continue;
                list.Add(new ExpressionMapping
                {
                    Source = m.source, Destination = m.destination, Gain = m.gain, Threshold = m.threshold,
                    Min = m.min, Max = m.max, Smoothing = m.smoothing, Invert = m.invert, Enabled = m.enabled,
                });
            }
            return list;
        }

        public static void CaptureMappings(ProfileData p, IEnumerable<ExpressionMapping> mappings, bool useDefaults)
        {
            p.useDefaultMappings = useDefaults;
            p.mappings = new List<ExpressionMappingData>();
            if (mappings == null) return;
            foreach (var m in mappings)
            {
                p.mappings.Add(new ExpressionMappingData
                {
                    source = m.Source, destination = m.Destination, gain = m.Gain, threshold = m.Threshold,
                    min = m.Min, max = m.Max, smoothing = m.Smoothing, invert = m.Invert, enabled = m.Enabled,
                });
            }
        }

        // ------------------------------------------------------------- lip sync

        public static void ApplyLipSync(ProfileData p, LipSyncSettings s)
        {
            s.Mode = (LipSyncMode)Clamp(p.lipSync.mode, 0, 2);
            s.CameraWeight = p.lipSync.cameraWeight;
            s.AudioWeight = p.lipSync.audioWeight;
            s.Audio.Sensitivity = p.lipSync.sensitivity;
            s.Audio.GateDb = p.lipSync.gateDb;
            s.Audio.AttackSeconds = p.lipSync.attackSeconds;
            s.Audio.ReleaseSeconds = p.lipSync.releaseSeconds;
            s.Clamp();
        }

        public static void CaptureLipSync(ProfileData p, LipSyncSettings s)
        {
            p.lipSync = new LipSyncData
            {
                mode = (int)s.Mode, cameraWeight = s.CameraWeight, audioWeight = s.AudioWeight,
                sensitivity = s.Audio.Sensitivity, gateDb = s.Audio.GateDb,
                attackSeconds = s.Audio.AttackSeconds, releaseSeconds = s.Audio.ReleaseSeconds,
            };
        }

        // ------------------------------------------------------------- camera / background / output

        public static void ApplyCamera(ProfileData p, AvatarCameraState s)
        {
            s.Preset = (FramingPreset)Clamp(p.avatarCamera.preset, 0, 3);
            s.Zoom = p.avatarCamera.zoom;
            s.PanX = p.avatarCamera.panX;
            s.PanY = p.avatarCamera.panY;
            s.OrbitYawDeg = p.avatarCamera.orbitYawDeg;
            s.OrbitPitchDeg = p.avatarCamera.orbitPitchDeg;
            s.FovDeg = p.avatarCamera.fovDeg;
            s.Clamp();
        }

        public static void CaptureCamera(ProfileData p, AvatarCameraState s)
        {
            p.avatarCamera = new AvatarCameraData
            {
                preset = (int)s.Preset, zoom = s.Zoom, panX = s.PanX, panY = s.PanY,
                orbitYawDeg = s.OrbitYawDeg, orbitPitchDeg = s.OrbitPitchDeg, fovDeg = s.FovDeg,
            };
        }

        public static void ApplyBackground(ProfileData p, BackgroundSettings s)
        {
            s.Mode = (BackgroundMode)Clamp(p.background.mode, 0, 3);
            if (RgbaColor.TryParseHex(p.background.solidHex, out var solid)) s.SolidColor = solid;
            if (RgbaColor.TryParseHex(p.background.chromaHex, out var chroma)) s.ChromaColor = chroma;
            s.ImagePath = string.IsNullOrEmpty(p.background.imagePath) ? null : p.background.imagePath;
            s.ImageFit = (ImageFitMode)Clamp(p.background.imageFit, 0, 2);
        }

        public static void CaptureBackground(ProfileData p, BackgroundSettings s)
        {
            p.background = new BackgroundData
            {
                mode = (int)s.Mode, solidHex = s.SolidColor.ToHex(), chromaHex = s.ChromaColor.ToHex(),
                imagePath = s.ImagePath ?? "", imageFit = (int)s.ImageFit,
            };
        }

        public static OutputSettings ToOutput(ProfileData p)
        {
            try { return new OutputSettings(p.output.width, p.output.height, p.output.fps); }
            catch (ArgumentOutOfRangeException) { return OutputSettings.Default; }
        }

        public static void CaptureOutput(ProfileData p, OutputSettings s)
        {
            p.output = new OutputData { width = s.Width, height = s.Height, fps = s.Fps };
        }

        // ------------------------------------------------------------- hotkeys

        public static List<HotkeyBinding> ToHotkeys(ProfileData p)
        {
            if (p.hotkeys == null || p.hotkeys.Count == 0) return HotkeyBinding.Defaults();
            var list = new List<HotkeyBinding>();
            foreach (var h in p.hotkeys)
            {
                if (h == null) continue;
                list.Add(new HotkeyBinding
                {
                    Key = h.key ?? "", Expression = h.expression ?? "", Intensity = h.intensity,
                    Mode = (HotkeyTriggerMode)Clamp(h.mode, 0, 2),
                });
            }
            return list;
        }

        public static void CaptureHotkeys(ProfileData p, IEnumerable<HotkeyBinding> bindings)
        {
            p.hotkeys = new List<HotkeyData>();
            foreach (var b in bindings)
            {
                p.hotkeys.Add(new HotkeyData { key = b.Key ?? "", expression = b.Expression ?? "", intensity = b.Intensity, mode = (int)b.Mode });
            }
        }

        /// <summary>Profile names become file names; keep them portable.</summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ProfileData.DefaultName;
            var chars = name.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|' || char.IsControl(c)) chars[i] = '_';
            }
            var result = new string(chars);
            return result.Length > 60 ? result.Substring(0, 60) : result;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
