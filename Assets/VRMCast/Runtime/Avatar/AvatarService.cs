using System;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UnityEngine;
using UniVRM10;
using VRM;
using VRMCast.Core.Localization;
using VRMCast.Core.Vrm;

namespace VRMCast.Avatar
{
    /// <summary>
    /// Runtime VRM loading (PRD 5.2). Detects the VRM version from the file header, loads through the matching
    /// UniVRM importer, and always unloads the previous avatar before showing the new one so reloading never
    /// leaks or duplicates avatars. All Unity object mutation happens on the main thread; UniVRM's
    /// <c>RuntimeOnlyAwaitCaller</c> only moves parsing/decoding work off it.
    /// </summary>
    public sealed class AvatarService : IAvatarService
    {
        private readonly Transform _avatarRoot;
        private CancellationTokenSource _loadCts;
        private int _loadSerial;

        public LoadedAvatar Current { get; private set; }
        public bool HasAvatar => Current != null && Current.IsAlive;
        public bool IsLoading { get; private set; }
        public Message LastError { get; private set; }

        public event Action<LoadedAvatar> AvatarLoaded;
        public event Action AvatarUnloaded;
        public event Action<Message> LoadFailed;
        public event Action<bool> LoadingStateChanged;

        /// <param name="avatarRoot">Scene transform that loaded avatars are parented under (identity pose expected).</param>
        public AvatarService(Transform avatarRoot)
        {
            _avatarRoot = avatarRoot ? avatarRoot : throw new ArgumentNullException(nameof(avatarRoot));
        }

        public async Task<AvatarLoadResult> LoadAsync(string path, VrmVersionOverride versionOverride = VrmVersionOverride.AutoDetect, CancellationToken cancellationToken = default)
        {
            // A newer request supersedes an in-flight one.
            _loadCts?.Cancel();
            _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ct = _loadCts.Token;
            var serial = ++_loadSerial;

            SetLoading(true);
            try
            {
                var result = await LoadInternalAsync(path, versionOverride, ct);
                if (serial != _loadSerial)
                {
                    // Superseded while loading: discard quietly.
                    result.Avatar?.Dispose();
                    return AvatarLoadResult.Fail(VrmLoadErrors.Cancelled);
                }

                if (result.Success)
                {
                    Unload();
                    Current = result.Avatar;
                    Current.Root.transform.SetParent(_avatarRoot, worldPositionStays: false);
                    Current.Root.transform.localPosition = Vector3.zero;
                    Current.Root.transform.localRotation = Quaternion.identity;
                    LastError = default;
                    AvatarLoaded?.Invoke(Current);
                }
                else
                {
                    LastError = result.Error;
                    LoadFailed?.Invoke(result.Error);
                }
                return result;
            }
            finally
            {
                if (serial == _loadSerial) SetLoading(false);
            }
        }

        private async Task<AvatarLoadResult> LoadInternalAsync(string path, VrmVersionOverride versionOverride, CancellationToken ct)
        {
            var inspection = VrmFileInspector.InspectFile(path);
            if (!inspection.IsValidGlb)
            {
                return AvatarLoadResult.Fail(inspection.Error ?? VrmLoadErrors.NotVrm);
            }

            var version = VrmFileInspector.Resolve(inspection.Version, versionOverride);
            if (version == VrmVersion.Unknown)
            {
                return AvatarLoadResult.Fail(inspection.Error ?? VrmLoadErrors.NotVrm);
            }
            if (versionOverride != VrmVersionOverride.AutoDetect && inspection.Version != VrmVersion.Unknown && inspection.Version != version)
            {
                return AvatarLoadResult.Fail(Message.Of(VrmLoadErrors.VersionMismatch,
                    Message.Of(inspection.Version.LabelKey()), Message.Of(version.LabelKey())));
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                switch (version)
                {
                    case VrmVersion.Vrm0:
                        return await LoadVrm0Async(path, inspection, ct);
                    case VrmVersion.Vrm1:
                        return await LoadVrm1Async(path, inspection, ct);
                    default:
                        return AvatarLoadResult.Fail(VrmLoadErrors.NotVrm);
                }
            }
            catch (OperationCanceledException)
            {
                return AvatarLoadResult.Fail(VrmLoadErrors.Cancelled);
            }
            catch (System.IO.FileNotFoundException)
            {
                return AvatarLoadResult.Fail(VrmLoadErrors.FileMissing);
            }
            catch (System.IO.IOException e)
            {
                Debug.LogException(e);
                return AvatarLoadResult.Fail(VrmLoadErrors.Unreadable);
            }
            catch (NotVrm0Exception e)
            {
                Debug.LogException(e);
                return AvatarLoadResult.Fail(VrmLoadErrors.NotVrm);
            }
            catch (GlbParseException e)
            {
                Debug.LogException(e);
                return AvatarLoadResult.Fail(VrmLoadErrors.NotVrm);
            }
            catch (UniGLTFException e)
            {
                Debug.LogException(e);
                return AvatarLoadResult.Fail(VrmLoadErrors.ImporterFailed);
            }
            catch (Exception e)
            {
                // The technical detail goes to the log; the user gets an actionable sentence (PRD 5.3).
                Debug.LogException(e);
                return AvatarLoadResult.Fail(VrmLoadErrors.ImporterFailed);
            }
        }

        private static async Task<AvatarLoadResult> LoadVrm0Async(string path, VrmInspection inspection, CancellationToken ct)
        {
            VRMMetaObject meta = null;
            var instance = await VrmUtility.LoadAsync(
                path,
                new RuntimeOnlyAwaitCaller(),
                metaCallback: m => meta = m);
            if (instance == null)
            {
                return AvatarLoadResult.Fail(VrmLoadErrors.ImporterFailed);
            }
            if (ct.IsCancellationRequested)
            {
                instance.Dispose();
                throw new OperationCanceledException(ct);
            }

            instance.EnableUpdateWhenOffscreen();
            instance.ShowMeshes();

            var avatar = new Vrm0Avatar(instance, path, meta, inspection.SpecVersion);
            if (!avatar.HasHumanoid)
            {
                avatar.Dispose();
                return AvatarLoadResult.Fail(VrmLoadErrors.NoHumanoid);
            }
            return AvatarLoadResult.Ok(avatar);
        }

        private static async Task<AvatarLoadResult> LoadVrm1Async(string path, VrmInspection inspection, CancellationToken ct)
        {
            var instance = await Vrm10.LoadPathAsync(
                path,
                canLoadVrm0X: false,
                showMeshes: true,
                awaitCaller: new RuntimeOnlyAwaitCaller(),
                ct: ct);
            if (instance == null)
            {
                return AvatarLoadResult.Fail(VrmLoadErrors.ImporterFailed);
            }

            var gltfInstance = instance.GetComponent<RuntimeGltfInstance>();
            if (gltfInstance != null)
            {
                gltfInstance.EnableUpdateWhenOffscreen();
            }

            var avatar = new Vrm1Avatar(instance, path, inspection.SpecVersion);
            if (!avatar.HasHumanoid)
            {
                avatar.Dispose();
                return AvatarLoadResult.Fail(VrmLoadErrors.NoHumanoid);
            }
            return AvatarLoadResult.Ok(avatar);
        }

        public void Unload()
        {
            if (Current == null) return;
            var old = Current;
            Current = null;
            old.Dispose();
            AvatarUnloaded?.Invoke();
        }

        private void SetLoading(bool loading)
        {
            if (IsLoading == loading) return;
            IsLoading = loading;
            LoadingStateChanged?.Invoke(loading);
        }

        public void Dispose()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            Unload();
        }
    }
}
