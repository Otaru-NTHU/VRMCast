using UnityEngine;
using VRMCast.Avatar;
using VRMCast.Core.Tracking;

namespace VRMCast.Tracking
{
    /// <summary>
    /// Applies an <see cref="AvatarPose"/> to the loaded avatar every Update: head chain rotations in normalized
    /// bone space, LookAt, expressions, and the resting arm pose that replaces the import T-pose. Version
    /// differences are hidden behind <see cref="LoadedAvatar"/>.
    /// </summary>
    public sealed class AvatarDriver
    {
        /// <summary>Degrees the upper arms hang down from the T-pose while idle.</summary>
        public float ArmRestAngleDeg { get; set; } = 70f;

        private readonly IAvatarService _avatars;
        private string[] _lastExpressionNames = new string[0];

        public AvatarDriver(IAvatarService avatars)
        {
            _avatars = avatars;
        }

        public void Apply(AvatarPose pose) => Apply(pose, default);

        /// <param name="body">Torso rotation from pose tracking (zero when body tracking is off).</param>
        public void Apply(AvatarPose pose, BodyPose body)
        {
            if (!_avatars.HasAvatar) return;
            var avatar = _avatars.Current;

            var bodyPitch = body.PitchRad * Mathf.Rad2Deg;
            var bodyYaw = body.YawRad * Mathf.Rad2Deg;
            var bodyRoll = body.RollRad * Mathf.Rad2Deg;

            // Head angles are camera-relative, so the torso's share is removed from the head chain to avoid double
            // rotation when the whole upper body turns.
            var pitch = pose.HeadPitchRad * Mathf.Rad2Deg - bodyPitch;
            var yaw = pose.HeadYawRad * Mathf.Rad2Deg - bodyYaw;
            var roll = pose.HeadRollRad * Mathf.Rad2Deg - bodyRoll;

            // Normalized space: identity is the T-pose facing +Z, so Euler angles are world-aligned.
            // Positive X pitches the face down, positive Y turns it toward +X (viewer's left), positive Z rolls the top toward -X.
            Quaternion Part(float ratio) => Quaternion.Euler(pitch * ratio, yaw * ratio, roll * ratio);
            Quaternion BodyPart(float ratio) => Quaternion.Euler(bodyPitch * ratio, bodyYaw * ratio, bodyRoll * ratio);

            avatar.ApplyPose(
                head: Part(pose.HeadRatio),
                neck: Part(pose.NeckRatio),
                chest: BodyPart(0.5f) * Part(pose.ChestRatio),
                spine: BodyPart(0.5f) * Part(pose.SpineRatio),
                leftUpperArm: Quaternion.Euler(0f, 0f, ArmRestAngleDeg),
                rightUpperArm: Quaternion.Euler(0f, 0f, -ArmRestAngleDeg));

            avatar.SetLookAt(pose.LookYawDeg, pose.LookPitchDeg);

            // Zero expressions that were driven last frame but are absent now so nothing sticks.
            foreach (var name in _lastExpressionNames)
            {
                if (!pose.Expressions.ContainsKey(name)) avatar.SetExpressionWeight(name, 0f);
            }
            foreach (var kv in pose.Expressions)
            {
                avatar.SetExpressionWeight(kv.Key, kv.Value);
            }
            if (_lastExpressionNames.Length != pose.Expressions.Count)
            {
                _lastExpressionNames = new string[pose.Expressions.Count];
            }
            pose.Expressions.Keys.CopyTo(_lastExpressionNames, 0);
        }

        /// <summary>Idle pose (arms down, neutral head) used when tracking is off.</summary>
        public void ApplyRest()
        {
            if (!_avatars.HasAvatar) return;
            var avatar = _avatars.Current;
            avatar.ApplyPose(Quaternion.identity, Quaternion.identity, Quaternion.identity, Quaternion.identity,
                Quaternion.Euler(0f, 0f, ArmRestAngleDeg), Quaternion.Euler(0f, 0f, -ArmRestAngleDeg));
            avatar.SetLookAt(0f, 0f);
            foreach (var name in _lastExpressionNames) avatar.SetExpressionWeight(name, 0f);
            _lastExpressionNames = new string[0];
        }
    }
}
