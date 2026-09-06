using VRMCast.Core.Vrm;

namespace VRMCast.Core.Avatar
{
    /// <summary>
    /// Engine-agnostic description of the currently loaded avatar, for UI, diagnostics and profiles.
    /// </summary>
    public sealed class AvatarInfo
    {
        public string FilePath { get; }
        public string FileName { get; }
        public string Title { get; }
        public string Author { get; }
        public VrmVersion Version { get; }
        public string SpecVersion { get; }
        public int ExpressionCount { get; }
        public bool HasSpringBones { get; }

        public AvatarInfo(string filePath, string title, string author, VrmVersion version, string specVersion, int expressionCount, bool hasSpringBones)
        {
            FilePath = filePath;
            FileName = string.IsNullOrEmpty(filePath) ? string.Empty : System.IO.Path.GetFileName(filePath);
            Title = string.IsNullOrWhiteSpace(title) ? FileName : title;
            Author = author ?? string.Empty;
            Version = version;
            SpecVersion = specVersion ?? string.Empty;
            ExpressionCount = expressionCount;
            HasSpringBones = hasSpringBones;
        }

        public string VersionLabel
        {
            get
            {
                switch (Version)
                {
                    case VrmVersion.Vrm0: return "VRM 0.x";
                    case VrmVersion.Vrm1: return "VRM 1.0";
                    default: return "Unknown";
                }
            }
        }
    }
}
