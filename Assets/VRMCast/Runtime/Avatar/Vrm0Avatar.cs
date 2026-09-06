using System.Collections.Generic;
using UniGLTF;
using UnityEngine;
using VRM;
using VRMCast.Core.Avatar;
using VRMCast.Core.Vrm;

namespace VRMCast.Avatar
{
    /// <summary>VRM 0.x avatar loaded through UniVRM's <c>VrmUtility</c>.</summary>
    public sealed class Vrm0Avatar : LoadedAvatar
    {
        private readonly RuntimeGltfInstance _instance;
        private readonly Animator _animator;
        private readonly VRMBlendShapeProxy _blendShapes;
        private readonly Dictionary<string, BlendShapeKey> _keysByName = new Dictionary<string, BlendShapeKey>();
        private readonly List<string> _expressionNames = new List<string>();

        public Vrm0Avatar(RuntimeGltfInstance instance, string filePath, VRMMetaObject meta, string specVersion)
            : base(instance.gameObject)
        {
            _instance = instance;
            _animator = instance.GetComponent<Animator>();
            _blendShapes = instance.GetComponent<VRMBlendShapeProxy>();

            if (_blendShapes != null && _blendShapes.BlendShapeAvatar != null)
            {
                foreach (var clip in _blendShapes.BlendShapeAvatar.Clips)
                {
                    if (clip == null) continue;
                    var key = clip.Key;
                    if (_keysByName.ContainsKey(key.Name)) continue;
                    _keysByName[key.Name] = key;
                    _expressionNames.Add(key.Name);
                }
            }

            var hasSpringBones = instance.GetComponentsInChildren<VRMSpringBone>(true).Length > 0;
            Info = new AvatarInfo(
                filePath,
                meta != null ? meta.Title : null,
                meta != null ? meta.Author : null,
                VrmVersion.Vrm0,
                specVersion,
                _expressionNames.Count,
                hasSpringBones);
        }

        public bool HasHumanoid => _animator != null && _animator.avatar != null && _animator.avatar.isHuman;

        public override IReadOnlyList<string> ExpressionNames => _expressionNames;

        public override bool SetExpressionWeight(string name, float weight)
        {
            if (_blendShapes == null || !_keysByName.TryGetValue(name, out var key)) return false;
            _blendShapes.ImmediatelySetValue(key, Mathf.Clamp01(weight));
            return true;
        }

        public override bool TryGetBone(HumanBodyBones bone, out Transform transform)
        {
            transform = null;
            if (!HasHumanoid || bone == HumanBodyBones.LastBone) return false;
            transform = _animator.GetBoneTransform(bone);
            return transform != null;
        }

        public override void Dispose()
        {
            if (_instance != null)
            {
                // RuntimeGltfInstance owns meshes, materials and textures created at import and destroys them with the root.
                _instance.Dispose();
            }
            else
            {
                base.Dispose();
            }
        }
    }
}
