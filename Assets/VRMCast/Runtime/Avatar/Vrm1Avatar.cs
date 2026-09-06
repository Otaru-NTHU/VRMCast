using System.Collections.Generic;
using UniGLTF;
using UnityEngine;
using UniVRM10;
using VRMCast.Core.Avatar;
using VRMCast.Core.Vrm;

namespace VRMCast.Avatar
{
    /// <summary>VRM 1.0 avatar loaded through UniVRM's <c>Vrm10</c> loader.</summary>
    public sealed class Vrm1Avatar : LoadedAvatar
    {
        private readonly Vrm10Instance _instance;
        private readonly RuntimeGltfInstance _gltfInstance;
        private readonly Dictionary<string, ExpressionKey> _keysByName = new Dictionary<string, ExpressionKey>();
        private readonly List<string> _expressionNames = new List<string>();

        public Vrm1Avatar(Vrm10Instance instance, string filePath, string specVersion)
            : base(instance.gameObject)
        {
            _instance = instance;
            _gltfInstance = instance.GetComponent<RuntimeGltfInstance>();

            foreach (var key in instance.Runtime.Expression.ExpressionKeys)
            {
                var name = key.Name;
                if (string.IsNullOrEmpty(name) || _keysByName.ContainsKey(name)) continue;
                _keysByName[name] = key;
                _expressionNames.Add(name);
            }

            var meta = instance.Vrm != null ? instance.Vrm.Meta : null;
            var hasSpringBones = instance.SpringBone != null && instance.SpringBone.Springs != null && instance.SpringBone.Springs.Count > 0;
            Info = new AvatarInfo(
                filePath,
                meta != null ? meta.Name : null,
                meta != null && meta.Authors != null && meta.Authors.Count > 0 ? meta.Authors[0] : null,
                VrmVersion.Vrm1,
                specVersion,
                _expressionNames.Count,
                hasSpringBones);
        }

        public bool HasHumanoid => _instance != null && _instance.Humanoid != null;

        public override IReadOnlyList<string> ExpressionNames => _expressionNames;

        public override bool SetExpressionWeight(string name, float weight)
        {
            if (_instance == null || !_keysByName.TryGetValue(name, out var key)) return false;
            _instance.Runtime.Expression.SetWeight(key, Mathf.Clamp01(weight));
            return true;
        }

        public override bool TryGetBone(HumanBodyBones bone, out Transform transform)
        {
            transform = null;
            if (_instance == null || bone == HumanBodyBones.LastBone) return false;
            return _instance.TryGetBoneTransform(bone, out transform) && transform != null;
        }

        public override void Dispose()
        {
            if (_gltfInstance != null)
            {
                _gltfInstance.Dispose();
            }
            else
            {
                base.Dispose();
            }
        }
    }
}
