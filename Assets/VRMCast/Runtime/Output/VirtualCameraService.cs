using System;
using UnityEngine;

namespace VRMCast.Output
{
    /// <summary>
    /// Install / status page logic for the camera extension (PRD 20.3). The extension must be embedded in the app
    /// bundle (Scripts/package-macos.sh); its identifier is the app's plus ".Camera". State is polled because the
    /// native request completes asynchronously on the main queue.
    /// </summary>
    public sealed class VirtualCameraService
    {
        public const string ExtensionSuffix = ".Camera";
        private const float PollSeconds = 1.0f;

        private float _nextPoll;
        private bool _queried;

        public bool IsSupported => FrameBridgeNative.IsAvailable;
        public string UnsupportedReason => FrameBridgeNative.UnavailableReason;
        public string ExtensionIdentifier => FrameBridgeNative.HostBundleIdentifier() + ExtensionSuffix;
        public FrameBridgeNative.ExtensionState State { get; private set; } = FrameBridgeNative.ExtensionState.Unknown;
        public string Message { get; private set; } = "";
        public string InstalledVersion { get; private set; } = "";
        /// <summary>True when macOS currently lists the camera device (installed, approved and loaded).</summary>
        public bool DevicePresent { get; private set; }
        /// <summary>Set when the app bundle does not contain the extension at all (development build).</summary>
        public bool ExtensionBundled { get; private set; }

        public event Action Changed;

        public VirtualCameraService()
        {
            ExtensionBundled = CheckBundled();
        }

        private static bool CheckBundled()
        {
            try
            {
                if (Application.platform != RuntimePlatform.OSXPlayer) return false;
                // Application.dataPath = VRMCast.app/Contents/Resources/Data
                var contents = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", ".."));
                var dir = System.IO.Path.Combine(contents, "Library", "SystemExtensions");
                return System.IO.Directory.Exists(dir) && System.IO.Directory.GetDirectories(dir, "*.systemextension").Length > 0;
            }
            catch { return false; }
        }

        public void Install()
        {
            if (!IsSupported) return;
            FrameBridgeNative.ActivateExtension(ExtensionIdentifier);
            _nextPoll = 0f;
            Refresh();
        }

        public void Uninstall()
        {
            if (!IsSupported) return;
            FrameBridgeNative.DeactivateExtension(ExtensionIdentifier);
            _nextPoll = 0f;
            Refresh();
        }

        public void Query()
        {
            if (!IsSupported) return;
            FrameBridgeNative.QueryExtension(ExtensionIdentifier);
            _queried = true;
        }

        /// <summary>Call every frame; cheap polling of the native state.</summary>
        public void Tick(float time)
        {
            if (!IsSupported) return;
            if (!_queried) Query();
            if (time < _nextPoll) return;
            _nextPoll = time + PollSeconds;
            Refresh();
        }

        private void Refresh()
        {
            var state = FrameBridgeNative.State;
            var message = FrameBridgeNative.ExtensionMessage();
            var version = FrameBridgeNative.InstalledVersion();
            var present = FrameBridgeNative.DevicePresent(MacVirtualCameraOutput.DeviceName);
            if (state == State && message == Message && version == InstalledVersion && present == DevicePresent) return;
            State = state;
            Message = message;
            InstalledVersion = version;
            DevicePresent = present;
            Changed?.Invoke();
        }
    }
}
