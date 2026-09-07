using VRMCast.Core.Localization;

namespace VRMCast.Avatar
{
    /// <summary>Outcome of <see cref="AvatarService.LoadAsync"/>. On failure <see cref="Error"/> is a localizable message.</summary>
    public sealed class AvatarLoadResult
    {
        public bool Success { get; }
        public LoadedAvatar Avatar { get; }
        public Message Error { get; }

        private AvatarLoadResult(bool success, LoadedAvatar avatar, Message error)
        {
            Success = success;
            Avatar = avatar;
            Error = error;
        }

        public static AvatarLoadResult Ok(LoadedAvatar avatar) => new AvatarLoadResult(true, avatar, default);
        public static AvatarLoadResult Fail(string errorKey) => new AvatarLoadResult(false, null, Message.Of(errorKey));
        public static AvatarLoadResult Fail(Message error) => new AvatarLoadResult(false, null, error);
    }
}
