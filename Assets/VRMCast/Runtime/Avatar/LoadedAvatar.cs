using System;
using System.Collections.Generic;
using UnityEngine;
using VRMCast.Core.Avatar;
using VRMCast.Core.Camera;
using VRMCast.Core.Vrm;

namespace VRMCast.Avatar
{
    /// <summary>
    /// Version-neutral handle to a loaded avatar. Wraps either a UniVRM 0.x <c>RuntimeGltfInstance</c> or a
    /// VRM 1.0 <c>Vrm10Instance</c> so nothing above the avatar layer needs to know which one it is.
    /// Only the <see cref="AvatarService"/> creates these; the UI never touches UniVRM objects directly.
    /// </summary>
    public abstract class LoadedAvatar : IDisposable
    {
        public GameObject Root { get; }
        public AvatarInfo Info { get; protected set; }
        public VrmVersion Version => Info.Version;
        public bool IsAlive => Root != null;

        protected LoadedAvatar(GameObject root)
        {
            Root = root ? root : throw new ArgumentNullException(nameof(root));
        }

        /// <summary>Expression names understood by <see cref="SetExpressionWeight"/> (preset and custom).</summary>
        public abstract IReadOnlyList<string> ExpressionNames { get; }

        /// <summary>Sets an expression weight (0..1) by canonical VRM 1.0 preset name or custom name. Unknown names are ignored and return false.</summary>
        public abstract bool SetExpressionWeight(string name, float weight);

        /// <summary>Real skeleton bone (for measurements such as framing).</summary>
        public abstract bool TryGetBone(HumanBodyBones bone, out Transform transform);

        /// <summary>
        /// Bone to pose from tracking, in normalized space (identity rotation = T-pose). VRM 0.x skeletons are
        /// normalized at import; VRM 1.0 exposes a control rig for this purpose. Rotations written here must be set
        /// every frame before LateUpdate.
        /// </summary>
        public abstract bool TryGetPoseBone(HumanBodyBones bone, out Transform transform);

        /// <summary>Eye direction in degrees: yaw positive toward the avatar's right, pitch positive up.</summary>
        public abstract void SetLookAt(float yawDeg, float pitchDeg);

        /// <summary>Writes local rotations for the tracked chain and the resting arm pose. Called every Update by the driver.</summary>
        public void ApplyPose(Quaternion head, Quaternion neck, Quaternion chest, Quaternion spine, Quaternion leftUpperArm, Quaternion rightUpperArm)
        {
            SetPoseRotation(HumanBodyBones.Head, head);
            SetPoseRotation(HumanBodyBones.Neck, neck);
            if (!SetPoseRotation(HumanBodyBones.UpperChest, chest)) SetPoseRotation(HumanBodyBones.Chest, chest);
            SetPoseRotation(HumanBodyBones.Spine, spine);
            SetPoseRotation(HumanBodyBones.LeftUpperArm, leftUpperArm);
            SetPoseRotation(HumanBodyBones.RightUpperArm, rightUpperArm);
        }

        private bool SetPoseRotation(HumanBodyBones bone, Quaternion rotation)
        {
            if (!TryGetPoseBone(bone, out var t) || t == null) return false;
            t.localRotation = rotation;
            return true;
        }

        /// <summary>World-space bounds over all renderers; falls back to a 1.6 m box at the root when nothing renders.</summary>
        public Bounds GetWorldBounds()
        {
            var renderers = Root.GetComponentsInChildren<Renderer>(includeInactive: false);
            var found = false;
            var bounds = new Bounds(Root.transform.position, Vector3.zero);
            foreach (var r in renderers)
            {
                if (!r.enabled) continue;
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!found)
            {
                bounds = new Bounds(Root.transform.position + Vector3.up * 0.8f, new Vector3(0.5f, 1.6f, 0.3f));
            }
            return bounds;
        }

        /// <summary>Framing landmarks: humanoid bones when present, renderer bounds otherwise.</summary>
        public AvatarMetrics ComputeMetrics()
        {
            var bounds = GetWorldBounds();
            var width = Mathf.Max(bounds.size.x, 0.1f);

            if (!TryGetBone(HumanBodyBones.Hips, out var hips) || !TryGetBone(HumanBodyBones.Head, out var head))
            {
                return AvatarMetrics.FromBounds(bounds.min.y, bounds.max.y, bounds.center.x, bounds.center.z, width);
            }

            var footY = bounds.min.y;
            if (TryGetBone(HumanBodyBones.LeftFoot, out var lf) && TryGetBone(HumanBodyBones.RightFoot, out var rf))
            {
                // Foot bones sit at the ankle; keep the lower of bounds and ankle so shoes stay in frame.
                footY = Mathf.Min(footY, Mathf.Min(lf.position.y, rf.position.y));
            }

            var chest = FirstBone(HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine);
            var neck = FirstBone(HumanBodyBones.Neck, HumanBodyBones.Head);
            var chestY = chest != null ? chest.position.y : Mathf.Lerp(hips.position.y, head.position.y, 0.5f);
            var neckY = neck != null ? neck.position.y : Mathf.Lerp(chestY, head.position.y, 0.7f);
            var topY = Mathf.Max(bounds.max.y, head.position.y + 0.05f);

            return new AvatarMetrics(
                footY: footY,
                hipsY: hips.position.y,
                chestY: chestY,
                neckY: neckY,
                headY: head.position.y,
                topY: topY,
                centerX: hips.position.x,
                centerZ: hips.position.z,
                width: width);
        }

        private Transform FirstBone(params HumanBodyBones[] candidates)
        {
            foreach (var b in candidates)
            {
                if (TryGetBone(b, out var t) && t != null) return t;
            }
            return null;
        }

        public virtual void Dispose()
        {
            if (Root != null)
            {
                UnityEngine.Object.Destroy(Root);
            }
        }
    }
}
