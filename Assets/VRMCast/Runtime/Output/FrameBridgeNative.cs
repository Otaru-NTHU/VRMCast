using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace VRMCast.Output
{
    /// <summary>
    /// P/Invoke surface of the macOS frame bridge plugin (Native/macOS/FrameBridge, built by
    /// Scripts/build-frame-bridge.sh into Assets/Plugins/macOS/VRMCastFrameBridge.bundle). Every call is guarded so a
    /// missing plugin or another platform degrades to "not available" instead of an exception.
    /// </summary>
    public static class FrameBridgeNative
    {
        private const string Lib = "VRMCastFrameBridge";

        public const int VcamOk = 0;
        public enum ExtensionState { Unknown = 0, NotInstalled = 1, Requesting = 2, AwaitingApproval = 3, Enabled = 4, NeedsReboot = 5, Failed = 6, Uninstalling = 7 }

        [DllImport(Lib)] private static extern int vrmcast_vcam_open(string deviceName, string sinkStreamName);
        [DllImport(Lib)] private static extern void vrmcast_vcam_close();
        [DllImport(Lib)] private static extern int vrmcast_vcam_is_open();
        [DllImport(Lib)] private static extern int vrmcast_vcam_send(IntPtr bgra, int width, int height, int bytesPerRow, int flipVertically, double timestampSeconds);
        [DllImport(Lib)] private static extern long vrmcast_vcam_frames_sent();
        [DllImport(Lib)] private static extern long vrmcast_vcam_frames_dropped();
        [DllImport(Lib)] private static extern int vrmcast_vcam_queue_count();
        [DllImport(Lib)] private static extern int vrmcast_vcam_device_present(string deviceName);
        [DllImport(Lib)] private static extern int vrmcast_vcam_last_error(StringBuilder buffer, int capacity);
        [DllImport(Lib)] private static extern void vrmcast_ext_activate(string bundleIdentifier);
        [DllImport(Lib)] private static extern void vrmcast_ext_deactivate(string bundleIdentifier);
        [DllImport(Lib)] private static extern void vrmcast_ext_query(string bundleIdentifier);
        [DllImport(Lib)] private static extern int vrmcast_ext_state();
        [DllImport(Lib)] private static extern int vrmcast_ext_message(StringBuilder buffer, int capacity);
        [DllImport(Lib)] private static extern int vrmcast_ext_installed_version(StringBuilder buffer, int capacity);
        [DllImport(Lib)] private static extern int vrmcast_host_bundle_identifier(StringBuilder buffer, int capacity);

        private static bool? _available;
        public static string UnavailableReason { get; private set; } = "";

        /// <summary>True when running on macOS and the plugin loads. Cached after the first probe.</summary>
        public static bool IsAvailable
        {
            get
            {
                if (_available.HasValue) return _available.Value;
                if (Application.platform != RuntimePlatform.OSXPlayer && Application.platform != RuntimePlatform.OSXEditor)
                {
                    UnavailableReason = "macOS only";
                    _available = false;
                    return false;
                }
                try
                {
                    vrmcast_ext_state();
                    _available = true;
                }
                catch (DllNotFoundException)
                {
                    UnavailableReason = "VRMCastFrameBridge.bundle missing (run Scripts/build-frame-bridge.sh)";
                    _available = false;
                }
                catch (EntryPointNotFoundException e)
                {
                    UnavailableReason = "VRMCastFrameBridge.bundle is out of date: " + e.Message;
                    _available = false;
                }
                catch (Exception e)
                {
                    UnavailableReason = e.Message;
                    _available = false;
                }
                return _available.Value;
            }
        }

        public static int Open(string deviceName, string sinkStreamName) => IsAvailable ? vrmcast_vcam_open(deviceName, sinkStreamName) : -1;
        public static void Close() { if (IsAvailable) vrmcast_vcam_close(); }
        public static bool IsOpen => IsAvailable && vrmcast_vcam_is_open() != 0;
        public static int Send(IntPtr bgra, int width, int height, int bytesPerRow, bool flipVertically, double timestampSeconds)
            => IsAvailable ? vrmcast_vcam_send(bgra, width, height, bytesPerRow, flipVertically ? 1 : 0, timestampSeconds) : -1;
        public static long FramesSent => IsAvailable ? vrmcast_vcam_frames_sent() : 0;
        public static long FramesDropped => IsAvailable ? vrmcast_vcam_frames_dropped() : 0;
        public static int QueueCount => IsAvailable ? vrmcast_vcam_queue_count() : 0;
        public static bool DevicePresent(string deviceName) => IsAvailable && vrmcast_vcam_device_present(deviceName) != 0;

        public static string LastError()
        {
            if (!IsAvailable) return UnavailableReason;
            var sb = new StringBuilder(512);
            vrmcast_vcam_last_error(sb, sb.Capacity);
            return sb.ToString();
        }

        public static void ActivateExtension(string bundleIdentifier) { if (IsAvailable) vrmcast_ext_activate(bundleIdentifier); }
        public static void DeactivateExtension(string bundleIdentifier) { if (IsAvailable) vrmcast_ext_deactivate(bundleIdentifier); }
        public static void QueryExtension(string bundleIdentifier) { if (IsAvailable) vrmcast_ext_query(bundleIdentifier); }
        public static ExtensionState State => IsAvailable ? (ExtensionState)vrmcast_ext_state() : ExtensionState.Unknown;

        public static string ExtensionMessage()
        {
            if (!IsAvailable) return "";
            var sb = new StringBuilder(512);
            vrmcast_ext_message(sb, sb.Capacity);
            return sb.ToString();
        }

        public static string InstalledVersion()
        {
            if (!IsAvailable) return "";
            var sb = new StringBuilder(64);
            vrmcast_ext_installed_version(sb, sb.Capacity);
            return sb.ToString();
        }

        public static string HostBundleIdentifier()
        {
            if (!IsAvailable) return Application.identifier;
            var sb = new StringBuilder(256);
            vrmcast_host_bundle_identifier(sb, sb.Capacity);
            return sb.Length > 0 ? sb.ToString() : Application.identifier;
        }
    }
}
