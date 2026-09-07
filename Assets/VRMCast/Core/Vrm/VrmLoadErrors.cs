namespace VRMCast.Core.Vrm
{
    /// <summary>
    /// Keys of the user-facing, actionable load errors (PRD 5.3). The text lives in
    /// <see cref="Localization.LocalizationTable"/> so it follows the UI language; the technical detail is logged
    /// separately by the service. Never show a raw stack trace to a normal user.
    /// </summary>
    public static class VrmLoadErrors
    {
        public const string NoFile = "error.noFile";
        public const string FileMissing = "error.fileMissing";
        public const string Unreadable = "error.unreadable";
        public const string NotVrm = "error.notVrm";
        public const string UnsupportedGlbVersion = "error.unsupportedGlb";
        public const string MetadataUnreadable = "error.metadata";
        public const string NoHumanoid = "error.noHumanoid";
        public const string SpringBoneInvalid = "error.springBone";
        public const string ImporterFailed = "error.importer";
        public const string Cancelled = "error.cancelled";

        /// <summary>Format args: {0} detected version label, {1} forced version label.</summary>
        public const string VersionMismatch = "error.versionMismatch";

        public static readonly string[] All =
        {
            NoFile, FileMissing, Unreadable, NotVrm, UnsupportedGlbVersion, MetadataUnreadable,
            NoHumanoid, SpringBoneInvalid, ImporterFailed, Cancelled, VersionMismatch,
        };
    }
}
