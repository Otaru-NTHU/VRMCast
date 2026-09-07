using System.Collections.Generic;
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
        /// <summary>Degrees the upper arms hang down from the T-pose while idle (mirrors BodyTrackingSettings.ArmRestAngleDeg).</summary>
        public float ArmRestAngleDeg { get; set; } = 70f;

        /// <summary>Finger curl used while hands are not tracked (mirrors HandTrackingSettings.RestCurl).</summary>
        public float RestCurl { get; set; } = 0.1f;

        /// <summary>Degrees each phalanx bends at curl = 1: proximal, intermediate, distal.</summary>
        private static readonly Vector3 FingerCurlDeg = new Vector3(70f, 90f, 60f);
        private static readonly Vector3 ThumbCurlDeg = new Vector3(20f, 40f, 55f);

        private static readonly Vector3 LeftArmAxis = Vector3.left;   // normalized T-pose: the avatar's left arm points -X
        private static readonly Vector3 RightArmAxis = Vector3.right;

        private readonly IAvatarService _avatars;
        private string[] _lastExpressionNames = new string[0];
        private IReadOnlyDictionary<string, float> _overrides;
        private readonly Dictionary<string, float> _merged = new Dictionary<string, float>();

        /// <summary>Expression weights layered on top of tracking (hotkeys). Max wins per expression.</summary>
        public void SetExpressionOverrides(IReadOnlyDictionary<string, float> overrides) => _overrides = overrides;

        public AvatarDriver(IAvatarService avatars)
        {
            _avatars = avatars;
        }

        public void Apply(AvatarPose pose) => Apply(pose, default, null);

        public void Apply(AvatarPose pose, BodyPose body) => Apply(pose, body, null, null);

        public void Apply(AvatarPose pose, BodyPose body, ArmsPose? arms) => Apply(pose, body, arms, null);

        /// <param name="body">Torso rotation from pose tracking (zero when body tracking is off).</param>
        /// <param name="arms">Arm directions in avatar space, or null to use the rest pose.</param>
        /// <param name="hands">Finger curl per hand, or null for the relaxed hand.</param>
        public void Apply(AvatarPose pose, BodyPose body, ArmsPose? arms, HandsPose? hands)
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

            var chest = BodyPart(0.5f) * Part(pose.ChestRatio);
            var spine = BodyPart(0.5f) * Part(pose.SpineRatio);
            avatar.ApplyPose(head: Part(pose.HeadRatio), neck: Part(pose.NeckRatio), chest: chest, spine: spine);
            ApplyArms(avatar, spine * chest, arms);
            ApplyFingers(avatar, hands);

            avatar.SetLookAt(pose.LookYawDeg, pose.LookPitchDeg);

            ApplyExpressions(avatar, pose.Expressions);
        }

        private void ApplyExpressions(LoadedAvatar avatar, IReadOnlyDictionary<string, float> tracked)
        {
            _merged.Clear();
            foreach (var kv in tracked) _merged[kv.Key] = kv.Value;
            if (_overrides != null)
            {
                foreach (var kv in _overrides)
                {
                    _merged[kv.Key] = _merged.TryGetValue(kv.Key, out var existing) ? Mathf.Max(existing, kv.Value) : kv.Value;
                }
            }

            // Zero expressions that were driven last frame but are absent now so nothing sticks.
            foreach (var name in _lastExpressionNames)
            {
                if (!_merged.ContainsKey(name)) avatar.SetExpressionWeight(name, 0f);
            }
            foreach (var kv in _merged)
            {
                avatar.SetExpressionWeight(kv.Key, kv.Value);
            }
            if (_lastExpressionNames.Length != _merged.Count)
            {
                _lastExpressionNames = new string[_merged.Count];
            }
            _merged.Keys.CopyTo(_lastExpressionNames, 0);
        }

        /// <summary>
        /// Converts arm segment directions (avatar space) into local bone rotations. The arms hang under the
        /// spine/chest chain, so the parent rotation is removed; the forearm additionally removes the upper arm.
        /// FromToRotation picks the minimal twist, which keeps elbows natural for waving and pointing.
        /// </summary>
        private void ApplyArms(LoadedAvatar avatar, Quaternion parent, ArmsPose? arms)
        {
            var restLeft = ToVector(ArmPoseSolver.RestDirection(true, ArmRestAngleDeg));
            var restRight = ToVector(ArmPoseSolver.RestDirection(false, ArmRestAngleDeg));
            var left = arms?.Left ?? new ArmPose { UpperArm = default, Forearm = default };
            var right = arms?.Right ?? new ArmPose();
            var leftUpperDir = arms.HasValue ? ToVector(left.UpperArm) : restLeft;
            var leftForeDir = arms.HasValue ? ToVector(left.Forearm) : restLeft;
            var rightUpperDir = arms.HasValue ? ToVector(right.UpperArm) : restRight;
            var rightForeDir = arms.HasValue ? ToVector(right.Forearm) : restRight;

            var inverseParent = Quaternion.Inverse(parent);
            var leftUpperLocal = inverseParent * Quaternion.FromToRotation(LeftArmAxis, leftUpperDir);
            var leftLowerLocal = Quaternion.Inverse(parent * leftUpperLocal) * Quaternion.FromToRotation(LeftArmAxis, leftForeDir);
            var rightUpperLocal = inverseParent * Quaternion.FromToRotation(RightArmAxis, rightUpperDir);
            var rightLowerLocal = Quaternion.Inverse(parent * rightUpperLocal) * Quaternion.FromToRotation(RightArmAxis, rightForeDir);
            avatar.ApplyArms(leftUpperLocal, leftLowerLocal, rightUpperLocal, rightLowerLocal);
        }

        private static Vector3 ToVector(VRMCast.Core.Camera.Float3 v)
        {
            var vec = new Vector3(v.X, v.Y, v.Z);
            return vec.sqrMagnitude < 1e-6f ? Vector3.down : vec.normalized;
        }

        // ------------------------------------------------------------- fingers

        private sealed class FingerBone
        {
            public Transform Transform;
            public Quaternion RestLocal;
            public Vector3 CurlAxisLocal;
            public float MaxDeg;
        }

        // [hand 0 = left, 1 = right][finger][segment]
        private FingerBone[,,] _fingerRig;
        private LoadedAvatar _rigAvatar;

        private static readonly HumanBodyBones[,] LeftFingerBones =
        {
            { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal },
            { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal },
            { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
            { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal },
            { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal },
        };

        private static readonly HumanBodyBones[,] RightFingerBones =
        {
            { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal },
            { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
            { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
            { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
            { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal },
        };

        /// <summary>
        /// Captures each finger bone's rest rotation and curl axis while the avatar is still in its import T-pose
        /// (VRM normalizes to a T-pose with the palms facing down). The curl axis is the one that swings the finger
        /// from its rest direction toward the palm; storing it in bone-local space makes it independent of whatever
        /// the arm does later. Call right after a load, before any pose is applied.
        /// </summary>
        public void OnAvatarLoaded()
        {
            _fingerRig = null;
            _rigAvatar = null;
            if (!_avatars.HasAvatar) return;
            var avatar = _avatars.Current;
            var rig = new FingerBone[2, 5, 3];
            var any = false;
            for (var hand = 0; hand < 2; hand++)
            {
                var bones = hand == 0 ? LeftFingerBones : RightFingerBones;
                var handBone = hand == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                avatar.TryGetPoseBone(handBone, out var handTransform);
                avatar.TryGetPoseBone(hand == 0 ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal, out var littleProximal);
                for (var finger = 0; finger < 5; finger++)
                {
                    Transform prev = handTransform;
                    for (var seg = 0; seg < 3; seg++)
                    {
                        if (!avatar.TryGetPoseBone(bones[finger, seg], out var bone) || bone == null) { prev = null; continue; }
                        // Finger direction: toward the next segment, or continuing the previous one for the fingertip bone.
                        Vector3 dir;
                        if (seg < 2 && avatar.TryGetPoseBone(bones[finger, seg + 1], out var next) && next != null)
                            dir = next.position - bone.position;
                        else if (prev != null)
                            dir = bone.position - prev.position;
                        else
                            dir = hand == 0 ? Vector3.left : Vector3.right;
                        if (dir.sqrMagnitude < 1e-8f) dir = hand == 0 ? Vector3.left : Vector3.right;
                        dir.Normalize();

                        // Fingers fold toward the palm (down in the T-pose); the thumb folds across the palm toward the
                        // little finger as well as down.
                        var toward = Vector3.down;
                        if (finger == 0 && littleProximal != null)
                        {
                            toward = (Vector3.down + (littleProximal.position - bone.position).normalized).normalized;
                        }
                        var axis = Vector3.Cross(dir, toward);
                        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(dir, Vector3.forward);
                        axis.Normalize();

                        var maxDeg = finger == 0 ? ThumbCurlDeg[seg] : FingerCurlDeg[seg];
                        rig[hand, finger, seg] = new FingerBone
                        {
                            Transform = bone,
                            RestLocal = bone.localRotation,
                            CurlAxisLocal = Quaternion.Inverse(bone.rotation) * axis,
                            MaxDeg = maxDeg,
                        };
                        any = true;
                        prev = bone;
                    }
                }
            }
            if (!any) return;
            _fingerRig = rig;
            _rigAvatar = avatar;
        }

        private void ApplyFingers(LoadedAvatar avatar, HandsPose? hands)
        {
            if (_fingerRig == null || !ReferenceEquals(_rigAvatar, avatar)) return;
            var left = hands?.Left ?? HandPose.Uniform(RestCurl);
            var right = hands?.Right ?? HandPose.Uniform(RestCurl);
            for (var hand = 0; hand < 2; hand++)
            {
                var pose = hand == 0 ? left : right;
                for (var finger = 0; finger < 5; finger++)
                {
                    var curl = Mathf.Clamp01(pose.Curl((Finger)finger));
                    for (var seg = 0; seg < 3; seg++)
                    {
                        var b = _fingerRig[hand, finger, seg];
                        if (b == null || b.Transform == null) continue;
                        b.Transform.localRotation = b.RestLocal * Quaternion.AngleAxis(curl * b.MaxDeg, b.CurlAxisLocal);
                    }
                }
            }
        }

        /// <summary>Idle pose (arms down, relaxed fingers, neutral head) used when tracking is off.</summary>
        public void ApplyRest()
        {
            if (!_avatars.HasAvatar) return;
            var avatar = _avatars.Current;
            avatar.ApplyPose(Quaternion.identity, Quaternion.identity, Quaternion.identity, Quaternion.identity);
            ApplyArms(avatar, Quaternion.identity, null);
            ApplyFingers(avatar, null);
            avatar.SetLookAt(0f, 0f);
            // Hotkeys keep working while tracking is off.
            ApplyExpressions(avatar, EmptyExpressions);
        }

        private static readonly Dictionary<string, float> EmptyExpressions = new Dictionary<string, float>();
    }
}
