using System;
using NUnit.Framework;
using VRMCast.Core.Camera;
using VRMCast.Core.Tracking;

namespace VRMCast.Core.Tests
{
    /// <summary>Hand-anchored arm IK, self-calibrating sides and hand side resolution.</summary>
    public class ArmIkTests
    {
        private const float Aspect = 16f / 9f;

        /// <summary>Front-facing user, unmirrored picture: the left shoulder is on the image's right (label left, u > 0.5).</summary>
        private static TrackingFrame PoseFrame(float elbowVis = 1f, float wristVis = 1f)
        {
            var w = new float[PoseFrameBuilder.LandmarkCount * 3];
            var n = new float[PoseFrameBuilder.LandmarkCount * 3];
            var vis = new float[PoseFrameBuilder.LandmarkCount];
            for (var i = 0; i < vis.Length; i++) vis[i] = 1f;
            void Set(int i, float x, float y, float z, float u, float v)
            {
                w[i * 3] = x; w[i * 3 + 1] = y; w[i * 3 + 2] = z;
                n[i * 3] = u; n[i * 3 + 1] = v;
            }
            const float sgn = 1f;
            // Shoulder width 0.38 m, 0.30 image widths apart at u = 0.5 ± 0.15.
            Set(PoseFrameBuilder.LeftShoulder, sgn * 0.19f, -0.45f, 0f, 0.5f + sgn * 0.15f, 0.40f);
            Set(PoseFrameBuilder.RightShoulder, -sgn * 0.19f, -0.45f, 0f, 0.5f - sgn * 0.15f, 0.40f);
            Set(PoseFrameBuilder.LeftHip, sgn * 0.15f, 0f, 0f, 0.5f + sgn * 0.12f, 0.75f);
            Set(PoseFrameBuilder.RightHip, -sgn * 0.15f, 0f, 0f, 0.5f - sgn * 0.12f, 0.75f);
            // Arms hanging.
            Set(PoseFrameBuilder.LeftElbow, sgn * 0.22f, -0.18f, 0f, 0.5f + sgn * 0.17f, 0.60f);
            Set(PoseFrameBuilder.LeftWrist, sgn * 0.24f, 0.06f, 0f, 0.5f + sgn * 0.19f, 0.80f);
            Set(PoseFrameBuilder.RightElbow, -sgn * 0.22f, -0.18f, 0f, 0.5f - sgn * 0.17f, 0.60f);
            Set(PoseFrameBuilder.RightWrist, -sgn * 0.24f, 0.06f, 0f, 0.5f - sgn * 0.19f, 0.80f);
            vis[PoseFrameBuilder.LeftElbow] = vis[PoseFrameBuilder.RightElbow] = elbowVis;
            vis[PoseFrameBuilder.LeftWrist] = vis[PoseFrameBuilder.RightWrist] = wristVis;
            return PoseFrameBuilder.Build(0, w, vis, n, Aspect);
        }

        /// <summary>
        /// A hand whose palm spans 0.09 m and appears `palmImage` image-widths wide, wrist at (u, v), palm facing the
        /// camera with the fingers up. Geometry follows the user's real side; the MediaPipe label defaults to the
        /// convention for an unmirrored picture (the real left hand is labelled "Right").
        /// </summary>
        private static HandTracking Hand(float u, float v, bool realLeft, float palmImage = 0.07f, float score = 0.95f, bool? label = null)
        {
            var labelLeft = label ?? !realLeft;
            var w = new float[HandFrameBuilder.LandmarkCount * 3];
            void Set(int i, float x, float y, float z) { w[i * 3] = x; w[i * 3 + 1] = y; w[i * 3 + 2] = z; }
            Set(HandFrameBuilder.Wrist, 0f, 0f, 0f);
            Set(HandFrameBuilder.MiddleMcp, 0f, -0.09f, 0f);
            // Real left hand, palm to the camera: the thumb side (index) is toward the body, i.e. image-left (-x).
            Set(HandFrameBuilder.IndexMcp, realLeft ? -0.04f : 0.04f, -0.08f, 0f);
            Set(HandFrameBuilder.LittleMcp, realLeft ? 0.04f : -0.04f, -0.08f, 0f);
            for (var i = 0; i < HandFrameBuilder.LandmarkCount; i++)
            {
                if (w[i * 3] == 0f && w[i * 3 + 1] == 0f && i != HandFrameBuilder.Wrist) Set(i, 0.01f * i, -0.1f, 0f);
            }
            return new HandTracking
            {
                Confidence = score, LandmarksXyz = w, LabelLeft = labelLeft,
                WristU = u, WristV = v,
                MiddleMcpU = u, MiddleMcpV = v - palmImage * Aspect,
                IndexMcpU = u + (realLeft ? -0.4f : 0.4f) * palmImage, IndexMcpV = v - 0.9f * palmImage * Aspect,
                LittleMcpU = u + (realLeft ? 0.4f : -0.4f) * palmImage, LittleMcpV = v - 0.9f * palmImage * Aspect,
            };
        }

        private static ArmPoseSolver NewSolver() => new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 0f });

        private static ArmsPose Run(ArmPoseSolver solver, TrackingFrame pose, TrackingFrame? hands, bool mirror = true, bool swap = false)
        {
            if (hands.HasValue) solver.SubmitHands(hands.Value, 1.0);
            ArmsPose result = default;
            for (var i = 0; i < 3; i++) result = solver.Update(pose, true, 1.0, 1f / 60f, mirror, swap);
            return result;
        }

        [Test]
        public void HandAboveShoulderRaisesTheArmViaIk()
        {
            var pose = PoseFrame();
            // Real left hand high above the left shoulder, far from any pose wrist: on an unmirrored picture the hand
            // landmarker labels it "Right" (it assumes a selfie mirror), which the label rule undoes.
            var hands = HandFrameBuilder.Build(0, Hand(0.66f, 0.05f, realLeft: true), null);
            HandFrameBuilder.ResolveSides(ref hands, pose.Pose, false, false);
            Assert.That(hands.LeftHand.HasValue, Is.True, "label Right on an unmirrored picture is the real left hand");

            var solver = NewSolver();
            var arms = Run(solver, pose, hands, mirror: true);
            Assert.That(arms.Right.FromHand, Is.True, "mirror: the real left hand drives the avatar's right arm");
            // 0.25 m above the shoulder is within reach, so the elbow bends; the reconstructed wrist must end up high.
            Assert.That(arms.Right.UpperArm.Y, Is.GreaterThan(0.3f), "upper arm points up toward the hand");
            var wristY = arms.Right.UpperArm.Y * solver.UpperArmLength + arms.Right.Forearm.Y * solver.ForearmLength;
            Assert.That(wristY, Is.GreaterThan(0.15f), "wrist well above the shoulder");
            Assert.That(arms.Right.UpperArm.X, Is.GreaterThan(-0.2f), "the avatar's right arm stays on its +X side");
            Assert.That(arms.Left.FromHand, Is.False);
            Assert.That(arms.Left.UpperArm.Y, Is.LessThan(-0.5f), "the other arm still hangs from the pose landmarks");
        }

        [Test]
        public void SwapSidesFlipsArms()
        {
            var pose = PoseFrame();
            var hands = HandFrameBuilder.Build(0, Hand(0.66f, 0.05f, realLeft: true), null);
            HandFrameBuilder.ResolveSides(ref hands, pose.Pose, false, false);
            var solver = NewSolver();
            var arms = Run(solver, pose, hands, mirror: true, swap: true);
            Assert.That(arms.Left.FromHand, Is.True);
            var wristY = arms.Left.UpperArm.Y * solver.UpperArmLength + arms.Left.Forearm.Y * solver.ForearmLength;
            Assert.That(wristY, Is.GreaterThan(0.15f));
        }

        [Test]
        public void HandSidesResolveByProximityToPoseWristsRegardlessOfLabel()
        {
            var pose = PoseFrame();
            // A hand on the pose's right wrist whose label ("Right" → real left by the label rule) disagrees: proximity wins.
            var p = pose.Pose.Value;
            var hands = HandFrameBuilder.Build(0, Hand(p.RightWristU, p.RightWristV, realLeft: false, label: false), null);
            HandFrameBuilder.ResolveSides(ref hands, pose.Pose, false, false);
            Assert.That(hands.RightHand.HasValue, Is.True);
            Assert.That(hands.LeftHand.HasValue, Is.False);
        }

        [Test]
        public void TwoHandsClaimingOneWristSplitBetweenSides()
        {
            var pose = PoseFrame();
            var p = pose.Pose.Value;
            var near = Hand(p.LeftWristU + 0.01f, p.LeftWristV, realLeft: true, label: true);
            var far = Hand(p.LeftWristU + 0.08f, p.LeftWristV, realLeft: true, label: true);
            var hands = HandFrameBuilder.Build(0, near, far);
            // Both were packed as "left" by label; ResolveSides must give the farther one the other side.
            HandFrameBuilder.ResolveSides(ref hands, pose.Pose, false, false);
            Assert.That(hands.LeftHand.HasValue, Is.True);
            Assert.That(hands.RightHand.HasValue, Is.True);
            Assert.That(hands.LeftHand.Value.WristU, Is.EqualTo(near.WristU).Within(1e-5f));
        }

        [Test]
        public void WithoutPoseImageCoordsLabelsDecideWithMirrorRule()
        {
            var hands = HandFrameBuilder.Build(0, Hand(0.3f, 0.3f, realLeft: false, label: true), null);
            HandFrameBuilder.ResolveSides(ref hands, null, false, false);
            Assert.That(hands.RightHand.HasValue, Is.True, "unmirrored picture: label Left is the real right hand");
            hands = HandFrameBuilder.Build(0, Hand(0.3f, 0.3f, realLeft: false, label: true), null);
            HandFrameBuilder.ResolveSides(ref hands, null, true, false);
            Assert.That(hands.LeftHand.HasValue, Is.True, "mirrored picture: labels are anatomically right");
        }

        [Test]
        public void ArmsFromHandsOffUsesPoseLandmarksOnly()
        {
            var pose = PoseFrame();
            var hands = HandFrameBuilder.Build(0, Hand(0.66f, 0.05f, realLeft: true), null);
            HandFrameBuilder.ResolveSides(ref hands, pose.Pose, false, false);
            var solver = new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 0f, ArmsFromHands = false, WristFromPalm = true });
            var arms = Run(solver, pose, hands, mirror: true);
            Assert.That(arms.Right.FromHand, Is.False);
            Assert.That(arms.Right.Tracked, Is.True, "pose elbow drives the arm");
            Assert.That(arms.Right.UpperArm.Y, Is.LessThan(-0.5f), "the pose says the arm hangs");
            Assert.That(arms.Right.HasHandOrientation, Is.True, "palm orientation is independent of the IK switch");

            solver = new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 0f, WristFromPalm = false });
            arms = Run(solver, pose, hands, mirror: true);
            Assert.That(arms.Right.FromHand, Is.True);
            Assert.That(arms.Right.HasHandOrientation, Is.False);
        }

        [Test]
        public void ResolverKeepsSideWhileHandMovesAwayFromPoseWrist()
        {
            var pose = PoseFrame();
            var p = pose.Pose.Value;
            var resolver = new HandSideResolver();
            // Frame 1: on the left pose wrist, label says the opposite → proximity decides: left.
            var f1 = HandFrameBuilder.Build(0, Hand(p.LeftWristU, p.LeftWristV, realLeft: true, label: true), null);
            resolver.Resolve(ref f1, pose.Pose, false);
            Assert.That(f1.LeftHand.HasValue, Is.True);
            // Frames 2..6: the hand drifts up, far from any pose wrist, in 0.08 steps; the side must not flip.
            for (var i = 1; i <= 5; i++)
            {
                var f = HandFrameBuilder.Build(i, Hand(p.LeftWristU, p.LeftWristV - 0.15f * i, realLeft: true, label: true), null);
                resolver.Resolve(ref f, pose.Pose, false);
                Assert.That(f.LeftHand.HasValue, Is.True, $"frame {i}");
                Assert.That(f.RightHand.HasValue, Is.False, $"frame {i}");
            }
        }

        [Test]
        public void ResolverGivesTwoHandsDifferentSides()
        {
            var pose = PoseFrame();
            var p = pose.Pose.Value;
            var resolver = new HandSideResolver();
            var near = Hand(p.LeftWristU + 0.01f, p.LeftWristV, realLeft: true, label: true);
            var far = Hand(p.LeftWristU + 0.09f, p.LeftWristV, realLeft: true, label: true);
            var f = HandFrameBuilder.Build(0, near, far);
            resolver.Resolve(ref f, pose.Pose, false);
            Assert.That(f.LeftHand.HasValue && f.RightHand.HasValue, Is.True);
            Assert.That(f.LeftHand.Value.WristU, Is.EqualTo(near.WristU).Within(1e-5f));
            // Next frame, the detector reports them in the other order: sides stay.
            f = HandFrameBuilder.Build(1, far, near);
            resolver.Resolve(ref f, pose.Pose, false);
            Assert.That(f.LeftHand.Value.WristU, Is.EqualTo(near.WristU).Within(1e-5f));
        }

        [Test]
        public void ResolverSwapFlipsFinalSides()
        {
            var pose = PoseFrame();
            var p = pose.Pose.Value;
            var resolver = new HandSideResolver();
            var f = HandFrameBuilder.Build(0, Hand(p.LeftWristU, p.LeftWristV, realLeft: true), null);
            resolver.Resolve(ref f, pose.Pose, swap: true);
            Assert.That(f.RightHand.HasValue, Is.True);
        }

        [Test]
        public void TwoBoneIkReachesTargetAndBendsTowardPole()
        {
            var solver = NewSolver();
            solver.SetAvatarArm(0.3f, 0.25f);
            // Target straight out to +X at 0.4 m (less than 0.55 reach): elbow must bend toward the pole (down).
            solver.SolveTwoBone(new Float3(0.4f, 0f, 0f), new Float3(0f, -1f, 0f), new Float3(1f, 0f, 0f), out var upper, out var fore);
            var elbow = upper * 0.3f;
            var wrist = elbow + fore * 0.25f;
            Assert.That(Float3.Distance(wrist, new Float3(0.4f, 0f, 0f)), Is.LessThan(1e-3f));
            Assert.That(elbow.Y, Is.LessThan(-0.05f), "elbow dropped toward the pole");

            // Far target: the arm straightens along the target direction.
            solver.SolveTwoBone(new Float3(0f, 2f, 0f), null, new Float3(1f, 0f, 0f), out upper, out fore);
            Assert.That(upper.Y, Is.GreaterThan(0.98f));
            Assert.That(fore.Y, Is.GreaterThan(0.98f));
        }

        [Test]
        public void HandCloserToCameraComesTowardViewer()
        {
            var pose = PoseFrame();
            // Same image position as the hanging wrist but a palm that looks twice as large: the hand is nearer.
            var p = pose.Pose.Value;
            var hands = HandFrameBuilder.Build(0, Hand(p.LeftWristU, 0.55f, realLeft: true, palmImage: 0.16f), null);
            HandFrameBuilder.ResolveSides(ref hands, pose.Pose, false, false);
            var arms = Run(NewSolver(), pose, hands, mirror: true);
            Assert.That(arms.Right.FromHand, Is.True);
            Assert.That(arms.Right.Forearm.Z, Is.GreaterThan(0.3f), "forearm reaches toward the viewer");
        }

        [Test]
        public void PalmFacingCameraGivesNormalTowardViewer()
        {
            var pose = PoseFrame();
            var hands = HandFrameBuilder.Build(0, Hand(0.66f, 0.2f, realLeft: true), null);
            HandFrameBuilder.ResolveSides(ref hands, pose.Pose, false, false);
            var arms = Run(new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 0f, WristFromPalm = true }), pose, hands, mirror: true);
            Assert.That(arms.Right.HasHandOrientation, Is.True);
            Assert.That(arms.Right.HandForward.Y, Is.GreaterThan(0.9f), "fingers point up");
            Assert.That(arms.Right.HandNormal.Z, Is.GreaterThan(0.9f), "palm faces the viewer");
        }

        [Test]
        public void HandOlderThanHoldFallsBackToPoseLandmarks()
        {
            var pose = PoseFrame();
            var hands = HandFrameBuilder.Build(0, Hand(0.66f, 0.05f, realLeft: true), null);
            HandFrameBuilder.ResolveSides(ref hands, pose.Pose, false, false);
            var solver = NewSolver();
            solver.SubmitHands(hands, 1.0);
            var arms = solver.Update(pose, true, 1.0, 1f / 60f, true, false);
            Assert.That(arms.Right.FromHand, Is.True);
            arms = solver.Update(pose, true, 2.0, 1f / 60f, true, false);
            Assert.That(arms.Right.FromHand, Is.False);
            Assert.That(arms.Right.Tracked, Is.True, "pose elbow still visible");
        }
    }
}
