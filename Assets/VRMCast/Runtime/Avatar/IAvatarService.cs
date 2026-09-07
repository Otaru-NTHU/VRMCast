using System;
using System.Threading;
using System.Threading.Tasks;
using VRMCast.Core.Localization;
using VRMCast.Core.Vrm;

namespace VRMCast.Avatar
{
    public interface IAvatarService : IDisposable
    {
        LoadedAvatar Current { get; }
        bool HasAvatar { get; }
        bool IsLoading { get; }
        Message LastError { get; }

        event Action<LoadedAvatar> AvatarLoaded;
        event Action AvatarUnloaded;
        event Action<Message> LoadFailed;
        event Action<bool> LoadingStateChanged;

        Task<AvatarLoadResult> LoadAsync(string path, VrmVersionOverride versionOverride = VrmVersionOverride.AutoDetect, CancellationToken cancellationToken = default);
        void Unload();
    }
}
