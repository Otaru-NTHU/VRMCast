using System;
using NUnit.Framework;
using VRMCast.Core.Camera;
using VRMCast.Core.Tracking;

namespace VRMCast.Core.Tests
{
    public class ArmPoseTests
    {
        [SetUp]
        public void RawLabels() => PoseFrameBuilder.SwapLeftRight = false;

        [TearDown]
        public void RestoreLabels() => PoseFrameBuilder.SwapLeftRight = true;

        private static float[] World(Action<Action<int, float, float, float>> setup)
        {
            var w = new float[PoseFrameBuilder.LandmarkCount * 3];
            void Set(int i, float x, float y, float z) { w[i * 3] = x; w[i * 3 + 1] = y; w[i * 3 + 2] = z; }
            Set(PoseFrameBuilder.LeftHip, 0.15f, 0f, 0f);
            Set(PoseFrameBuilder.RightHip, -0.15f, 0f, 0f);
            Set(PoseFrameBuilder.LeftShoulder, 0.2f, -0.45f, 0f);
            Set(PoseFrameBuilder.RightShoulder, -0.2f, -0.45f, 0f);
            // Arms hanging down by default.
            Set(PoseFrameBuilder.LeftElbow, 0.22f, -0.2f, 0f);
            Set(PoseFrameBuilder.LeftWrist, 0.24f, 0.05f, 0f);
            Set(PoseFrameBuilder.RightElbow, -0.22f, -0.2f, 0f);
            Set(PoseFrameBuilder.RightWrist, -0.24f, 0.05f, 0f);
            setup?.Invoke(Set);
            return w;
        }

        private static float[] Visibility(float all = 1f)
        {
            var v = new float[PoseFrameBuilder.LandmarkCount];
            for (var i = 0; i < v.Length; i++) v[i] = all;
            return v;
        }

        private static ArmsPose Settle(ArmPoseSolver solver, TrackingFrame frame, bool mirror = true, int frames = 120)
        {
            ArmsPose pose = default;
            for (var i = 0; i < frames; i++) pose = solver.Update(frame, bodyRecent: true, 1f / 60f, mirror);
            return pose;
        }

        [Test]
        public void RestDirectionHangsBelowTPose()
        {
            var left = ArmPoseSolver.RestDirection(true, 70f);
            Assert.That(left.X, Is.LessThan(0f), "avatar's left is -X");
            Assert.That(left.Y, Is.LessThan(-0.9f));
            var right = ArmPoseSolver.RestDirection(false, 70f);
            Assert.That(right.X, Is.GreaterThan(0f));
            Assert.That(Math.Abs(left.Length - 1f), Is.LessThan(1e-4f));
        }

        [Test]
        public void RaisedLeftArmMirrorsToAvatarRightArm()
        {
            // User raises the left arm straight up (elbow and wrist above the shoulder, y down => negative).
            var world = World(set =>
            {
                set(PoseFrameBuilder.LeftElbow, 0.2f, -0.75f, 0f);
                set(PoseFrameBuilder.LeftWrist, 0.2f, -1.0f, 0f);
            });
            var frame = PoseFrameBuilder.Build(0, world, Visibility());
            var solver = new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 0f });

            var pose = Settle(solver, frame, mirror: true);
            Assert.That(pose.Right.Tracked, Is.True);
            Assert.That(pose.Right.UpperArm.Y, Is.GreaterThan(0.95f), "mirror: the reflection raises the arm on the same screen side = the avatar's right");
            Assert.That(pose.Left.UpperArm.Y, Is.LessThan(-0.9f), "the other arm hangs down");

            pose = Settle(solver, frame, mirror: false);
            Assert.That(pose.Left.UpperArm.Y, Is.GreaterThan(0.95f), "non-mirror: the avatar copies with its own left arm");
        }

        [Test]
        public void SidewaysArmMapsToScreenSide()
        {
            // User's left arm straight out to their left = image right (+x).
            var world = World(set =>
            {
                set(PoseFrameBuilder.LeftElbow, 0.45f, -0.45f, 0f);
                set(PoseFrameBuilder.LeftWrist, 0.7f, -0.45f, 0f);
            });
            var frame = PoseFrameBuilder.Build(0, world, Visibility());
            var pose = Settle(new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 0f }), frame, mirror: true);
            // Mirror: the user's left hand shows on screen-left, which is the avatar's right arm pointing +X.
            Assert.That(pose.Right.UpperArm.X, Is.GreaterThan(0.95f));
            Assert.That(Math.Abs(pose.Right.UpperArm.Y), Is.LessThan(0.05f));

            pose = Settle(new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 0f }), frame, mirror: false);
            // Copy: the avatar's own left arm (-X) points out to its left.
            Assert.That(pose.Left.UpperArm.X, Is.LessThan(-0.95f));
        }

        [Test]
        public void ArmTowardCameraPointsTowardViewer()
        {
            var world = World(set =>
            {
                set(PoseFrameBuilder.RightElbow, -0.2f, -0.45f, -0.25f);
                set(PoseFrameBuilder.RightWrist, -0.2f, -0.45f, -0.5f);
            });
            var frame = PoseFrameBuilder.Build(0, world, Visibility());
            var pose = Settle(new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 0f }), frame, mirror: true);
            // User's right arm → avatar's left arm in mirror mode; depth is never mirrored.
            Assert.That(pose.Left.UpperArm.Z, Is.GreaterThan(0.95f), "toward the camera = toward the viewer = +Z");
            Assert.That(pose.Left.Forearm.Z, Is.GreaterThan(0.95f));
        }

        [Test]
        public void HiddenWristKeepsArmStraightAndHiddenElbowReturnsToRest()
        {
            var world = World(set =>
            {
                set(PoseFrameBuilder.LeftElbow, 0.2f, -0.75f, 0f);
                set(PoseFrameBuilder.LeftWrist, 0.5f, -0.75f, 0f);
            });
            var vis = Visibility();
            vis[PoseFrameBuilder.LeftWrist] = 0.1f;
            var frame = PoseFrameBuilder.Build(0, world, vis);
            var solver = new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 0f });
            var pose = Settle(solver, frame);   // mirror: the user's left arm is the avatar's right arm
            Assert.That(pose.Right.Tracked, Is.True);
            Assert.That(pose.Right.Forearm, Is.EqualTo(pose.Right.UpperArm));

            vis[PoseFrameBuilder.LeftElbow] = 0.1f;
            frame = PoseFrameBuilder.Build(1, world, vis);
            pose = Settle(solver, frame);
            Assert.That(pose.Right.Tracked, Is.False);
            var rest = ArmPoseSolver.RestDirection(false, 70f);
            Assert.That(Float3.Distance(pose.Right.UpperArm, rest), Is.LessThan(0.02f));
        }

        [Test]
        public void ArmsOffModeAlwaysRests()
        {
            var world = World(set => set(PoseFrameBuilder.LeftElbow, 0.2f, -0.75f, 0f));
            var frame = PoseFrameBuilder.Build(0, world, Visibility());
            var pose = Settle(new ArmPoseSolver(new BodyTrackingSettings { Mode = BodyTrackingMode.UpperBody, ArmSmoothing = 0f }), frame);
            Assert.That(pose.Right.Tracked, Is.False);
            Assert.That(pose.Right.UpperArm.Y, Is.LessThan(-0.9f));
        }

        [Test]
        public void SmoothingEasesTowardTarget()
        {
            var world = World(set =>
            {
                set(PoseFrameBuilder.LeftElbow, 0.2f, -0.75f, 0f);
                set(PoseFrameBuilder.LeftWrist, 0.2f, -1.0f, 0f);
            });
            var frame = PoseFrameBuilder.Build(0, world, Visibility());
            var solver = new ArmPoseSolver(new BodyTrackingSettings { ArmSmoothing = 1f });
            solver.Update(frame, true, 1f / 60f, true); // first frame initializes at rest? no: first Update snaps
            var second = solver.Update(frame, true, 1f / 60f, true);
            Assert.That(second.Right.UpperArm.Y, Is.GreaterThan(0.9f), "first update snaps so start-up does not animate from rest");
        }
    }
}
