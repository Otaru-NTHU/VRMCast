namespace VRMCast.Core.Vrm
{
    /// <summary>
    /// User-facing, actionable load error messages (PRD 5.3). Never show a raw stack trace to a normal user;
    /// the technical detail is logged separately by the service.
    /// </summary>
    public static class VrmLoadErrors
    {
        public const string NoFile = "No file was selected.";
        public const string FileMissing = "The file could not be found. It may have been moved or deleted.";
        public const string Unreadable = "The file could not be read. Check that you have permission to open it.";
        public const string NotVrm = "This file is not a valid VRM file.";
        public const string UnsupportedGlbVersion = "This file uses an unsupported glTF container version.";
        public const string MetadataUnreadable = "VRM metadata could not be read.";
        public const string NoHumanoid = "The avatar has no compatible humanoid definition.";
        public const string SpringBoneInvalid = "The model loaded, but SpringBone data is invalid. Physics has been disabled.";
        public const string ImporterFailed = "The avatar could not be loaded. The file may be damaged or use features this version does not support.";
        public const string Cancelled = "Loading was cancelled.";
    }
}
