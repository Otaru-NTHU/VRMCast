namespace VRMCast.Avatar
{
    /// <summary>Outcome of <see cref="AvatarService.LoadAsync"/>. On failure <see cref="Error"/> is a user-facing sentence.</summary>
    public sealed class AvatarLoadResult
    {
        public bool Success { get; }
        public LoadedAvatar Avatar { get; }
        public string Error { get; }
        public string Warning { get; }

        private AvatarLoadResult(bool success, LoadedAvatar avatar, string error, string warning)
        {
            Success = success;
            Avatar = avatar;
            Error = error;
            Warning = warning;
        }

        public static AvatarLoadResult Ok(LoadedAvatar avatar, string warning = null) => new AvatarLoadResult(true, avatar, null, warning);
        public static AvatarLoadResult Fail(string error) => new AvatarLoadResult(false, null, error, null);
    }
}
