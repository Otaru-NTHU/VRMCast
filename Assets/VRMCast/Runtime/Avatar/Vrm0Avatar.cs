using System.Collections.Generic;
using UniGLTF;
using UnityEngine;
using VRM;
using VRMCast.Core.Avatar;
using VRMCast.Core.Tracking;
using VRMCast.Core.Vrm;

namespace VRMCast.Avatar
{
    /// <summary>VRM 0.x avatar loaded through UniVRM's <c>VrmUtility</c>.</summary>
    public sealed class Vrm0Avatar : LoadedAvatar
    {
        private readonly RuntimeGltfInstance _instance;
        private readonly Animator _animator;
        private readonly VRMBlendShapeProxy _blendShapes;
        private readonly VRMLookAtHead _lookAt;
        private readonly Dictionary<string, BlendShapeKey> _canonicalKeys = new Dictionary<string, BlendShapeKey>();
        private readonly Dictionary<string, BlendShapeKey> _keysByName = new Dictionary<string, BlendShapeKey>();
        private readonly List<string> _expressionNames = new List<string>();

        public Vrm0Avatar(RuntimeGltfInstance instance, string filePath, VRMMetaObject meta, string specVersion)
            : base(instance.gameObject)
        {
            _instance = instance;
            _animator = instance.GetComponent<Animator>();
            _blendShapes = instance.GetComponent<VRMBlendShapeProxy>();
            _lookAt = instance.GetComponent<VRMLookAtHead>();
            if (_lookAt != null) _lookAt.Target = null; // driven manually through RaiseYawPitchChanged

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
            if (_blendShapes == null || !TryResolveKey(name, out var key)) return false;
            _blendShapes.ImmediatelySetValue(key, Mathf.Clamp01(weight));
            return true;
        }

        /// <summary>Accepts canonical names (happy, blinkLeft, aa …), 0.x preset names (Joy, Blink_L, A …) and custom clip names.</summary>
        private bool TryResolveKey(string name, out BlendShapeKey key)
        {
            if (_canonicalKeys.TryGetValue(name, out key)) return true;
            if (_keysByName.TryGetValue(name, out key)) { _canonicalKeys[name] = key; return true; }
            var preset = VrmExpressions.ToVrm0Preset(name);
            if (preset != null && _keysByName.TryGetValue(preset, out key)) { _canonicalKeys[name] = key; return true; }
            return false;
        }

        public override bool TryGetPoseBone(HumanBodyBones bone, out Transform transform) => TryGetBone(bone, out transform);

        public override void SetLookAt(float yawDeg, float pitchDeg)
        {
            if (_lookAt == null) return;
            _lookAt.RaiseYawPitchChanged(yawDeg, pitchDeg);
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
