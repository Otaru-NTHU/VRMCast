using System;
using System.Collections.Generic;

namespace VRMCast.Core.Profiles
{
    /// <summary>
    /// On-disk profile schema (PRD 23). Plain serializable fields only, so the runtime can persist it with Unity's
    /// JsonUtility; conversions to the live settings objects live in <see cref="ProfileMapper"/>.
    /// Paths are stored as given; a missing file is reported, never deleted (PRD 23.2).
    /// </summary>
    [Serializable]
    public sealed class ProfileData
    {
        public const int CurrentSchemaVersion = 1;
        public const string DefaultName = "Default";

        public int schemaVersion = CurrentSchemaVersion;
        public string name = DefaultName;
        public string createdUtc = "";
        public string modifiedUtc = "";
        public bool autoSave = true;

        public string vrmPath = "";
        public int vrmVersion;              // VrmVersion enum value as detected at save time

        public string cameraDevice = "";
        public string microphoneDevice = "";

        public int faceMode;                // FaceTrackingMode
        public int bodyMode = 3;            // BodyTrackingMode (UpperBodyArmsFingers)
        public bool trackingEnabled;

        public FaceCalibrationData calibration = new FaceCalibrationData();
        public TrackingTuningData tracking = new TrackingTuningData();
        public BodyTuningData body = new BodyTuningData();
        public HandTuningData hands = new HandTuningData();
        public List<ExpressionMappingData> mappings = new List<ExpressionMappingData>();
        public bool useDefaultMappings = true;
        public LipSyncData lipSync = new LipSyncData();
        public AvatarCameraData avatarCamera = new AvatarCameraData();
        public BackgroundData background = new BackgroundData();
        public OutputData output = new OutputData();
        public List<HotkeyData> hotkeys = new List<HotkeyData>();
        public UiData ui = new UiData();
    }

    [Serializable]
    public sealed class FaceCalibrationData
    {
        public bool isCalibrated;
        public float pitchRad, yawRad, rollRad, lookX, lookY, mouthOpen, smile;
        public float bodyRollRad, bodyYawRad, bodyPitchRad;
    }

    [Serializable]
    public sealed class TrackingTuningData
    {
        public bool mirrorUser = true;
        public float headSmoothing = 0.5f;
        public float expressionSmoothing = 0.35f;
        public float lookSmoothing = 0.4f;
        public float headGain = 1f;
        public float headDeadZoneDeg = 0.8f;
        public float blinkGain = 1.8f;
        public bool invertPitch, invertYaw, invertRoll;
        public float lookGain = 1.2f;
    }

    [Serializable]
    public sealed class BodyTuningData
    {
        public float smoothing = 0.6f;
        public float gain = 1f;
        public bool invertRoll, invertYaw, invertPitch;
    }

    [Serializable]
    public sealed class HandTuningData
    {
        public float smoothing = 0.35f;
        public float curlGain = 1f;
        public bool swapHands;
    }

    [Serializable]
    public sealed class ExpressionMappingData
    {
        public string source = "";
        public string destination = "";
        public float gain = 1f;
        public float threshold;
        public float min;
        public float max = 1f;
        public float smoothing = 0.35f;
        public bool invert;
        public bool enabled = true;
    }

    [Serializable]
    public sealed class LipSyncData
    {
        public int mode = 2;                // LipSyncMode.Hybrid
        public float cameraWeight = 0.45f;
        public float audioWeight = 0.55f;
        public float sensitivity = 1f;
        public float gateDb = -45f;
        public float attackSeconds = 0.03f;
        public float releaseSeconds = 0.12f;
    }

    [Serializable]
    public sealed class AvatarCameraData
    {
        public int preset = 1;              // FramingPreset.Bust
        public float zoom = 1f;
        public float panX, panY;
        public float orbitYawDeg, orbitPitchDeg;
        public float fovDeg = 30f;
    }

    [Serializable]
    public sealed class BackgroundData
    {
        public int mode = 2;                // BackgroundMode.ChromaKey
        public string solidHex = "#202020";
        public string chromaHex = "#00FF00";
        public string imagePath = "";
        public int imageFit;                // ImageFitMode.Fill
    }

    [Serializable]
    public sealed class OutputData
    {
        public int width = 1920;
        public int height = 1080;
        public int fps = 30;
    }

    [Serializable]
    public sealed class HotkeyData
    {
        public string key = "";             // UnityEngine.KeyCode name, e.g. "Alpha1"
        public string expression = "";      // canonical VRM expression or custom name
        public float intensity = 1f;
        public int mode;                    // HotkeyTriggerMode
    }

    [Serializable]
    public sealed class UiData
    {
        public bool showStatusOverlay = true;
    }
}
