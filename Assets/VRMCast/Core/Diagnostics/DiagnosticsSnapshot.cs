using System.Globalization;
using System.Text;
using VRMCast.Core.Backgrounds;
using VRMCast.Core.Camera;
using VRMCast.Core.Rendering;

namespace VRMCast.Core.Diagnostics
{
    /// <summary>
    /// Point-in-time diagnostics (PRD 31, MVP-A subset). Contains no camera frames, audio, or file contents;
    /// only the avatar file name is included so the report is safe to paste into a bug report.
    /// </summary>
    public sealed class DiagnosticsSnapshot
    {
        public double RenderFps { get; set; }
        public double FrameTimeMs { get; set; }
        public OutputSettings Output { get; set; } = OutputSettings.Default;
        public int TargetFrameRate { get; set; }
        public string AvatarName { get; set; } = "(none)";
        public string AvatarVersion { get; set; } = "-";
        public int ExpressionCount { get; set; }
        public bool SpringBones { get; set; }
        public BackgroundMode Background { get; set; } = BackgroundModeExtensions.Default;
        public FramingPreset Framing { get; set; } = FramingPresetExtensions.Default;
        public string OutputStatus { get; set; } = "Preview only";
        public string AppVersion { get; set; } = "";
        public string Platform { get; set; } = "";

        public string TrackingStatus { get; set; } = "off";
        public string TrackingEngine { get; set; } = "(none)";
        public double TrackingFps { get; set; }
        public double InferenceMs { get; set; }
        public long TrackingDropped { get; set; }
        public float FaceConfidence { get; set; }
        public string CameraDevice { get; set; } = "(none)";
        public string CameraResolution { get; set; } = "-";

        public string ToReport()
        {
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            sb.AppendLine("VRMCast diagnostics");
            sb.AppendLine($"App: {AppVersion} ({Platform})");
            sb.AppendLine($"Render FPS: {RenderFps.ToString("0.0", ci)} ({FrameTimeMs.ToString("0.00", ci)} ms)");
            sb.AppendLine($"Target FPS: {TargetFrameRate}");
            sb.AppendLine($"Output: {Output.Width}x{Output.Height} @ {Output.Fps}");
            sb.AppendLine($"Output status: {OutputStatus}");
            sb.AppendLine($"Avatar: {AvatarName} [{AvatarVersion}] expressions={ExpressionCount} springBones={(SpringBones ? "yes" : "no")}");
            sb.AppendLine($"Background: {Background.Label()}");
            sb.AppendLine($"Framing: {Framing.Label()}");
            sb.AppendLine($"Tracking: {TrackingStatus} engine={TrackingEngine} fps={TrackingFps.ToString("0.0", ci)} inference={InferenceMs.ToString("0.0", ci)} ms dropped={TrackingDropped} confidence={FaceConfidence.ToString("0.00", ci)}");
            sb.AppendLine($"Camera: {CameraDevice} {CameraResolution}");
            return sb.ToString();
        }
    }
}
