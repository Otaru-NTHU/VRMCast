using NUnit.Framework;
using VRMCast.Core.Tracking;

namespace VRMCast.Core.Tests
{
    public class FingerCurlTests
    {
        /// <summary>Synthetic left hand: fingers along +x from the wrist, palm in the xz plane; bends fold toward -y.</summary>
        private static float[] Hand(float indexBendDeg = 0f, float thumbBendDeg = 0f, float otherBendDeg = 0f)
        {
            var w = new float[HandFrameBuilder.LandmarkCount * 3];
            void Set(int i, float x, float y, float z) { w[i * 3] = x; w[i * 3 + 1] = y; w[i * 3 + 2] = z; }
            Set(HandFrameBuilder.Wrist, 0f, 0f, 0f);

            void Chain(int mcp, float baseX, float baseZ, float bendDeg, float seg)
            {
                // Three segments after the MCP, each bent by bendDeg relative to the previous one.
                var x = baseX; var y = 0f; var z = baseZ;
                Set(mcp, x, y, z);
                var angle = 0.0;
                for (var k = 1; k <= 3; k++)
                {
                    angle += bendDeg * System.Math.PI / 180.0;
                    x += (float)(seg * System.Math.Cos(angle));
                    y -= (float)(seg * System.Math.Sin(angle));
                    Set(mcp + k, x, y, z);
                }
            }
            Chain(HandFrameBuilder.IndexMcp, 0.09f, 0.03f, indexBendDeg, 0.03f);
            Chain(HandFrameBuilder.MiddleMcp, 0.09f, 0.01f, otherBendDeg, 0.032f);
            Chain(HandFrameBuilder.RingMcp, 0.085f, -0.01f, otherBendDeg, 0.03f);
            Chain(HandFrameBuilder.LittleMcp, 0.08f, -0.03f, otherBendDeg, 0.025f);
            // Thumb: CMC then two segments bent by thumbBendDeg each.
            Set(HandFrameBuilder.ThumbCmc, 0.03f, 0f, 0.04f);
            var tx = 0.03f; var ty = 0f; var tz = 0.04f;
            var ta = 0.0;
            for (var k = 1; k <= 3; k++)
            {
                if (k > 1) ta += thumbBendDeg * System.Math.PI / 180.0;
                tx += (float)(0.03 * System.Math.Cos(ta));
                ty -= (float)(0.03 * System.Math.Sin(ta));
                Set(HandFrameBuilder.ThumbCmc + k, tx, ty, tz);
            }
            return w;
        }

        [Test]
        public void StraightFingersHaveZeroCurl()
        {
            var w = Hand();
            foreach (Finger f in System.Enum.GetValues(typeof(Finger)))
            {
                Assert.That(FingerCurl.Compute(w, f), Is.LessThan(0.05f), f.ToString());
            }
        }

        [Test]
        public void BentIndexCurlsOnlyTheIndex()
        {
            var w = Hand(indexBendDeg: 70f);
            Assert.That(FingerCurl.Compute(w, Finger.Index), Is.GreaterThan(0.9f));
            Assert.That(FingerCurl.Compute(w, Finger.Middle), Is.LessThan(0.05f));
            Assert.That(FingerCurl.Compute(w, Finger.Thumb), Is.LessThan(0.05f));
        }

        [Test]
        public void HalfBendIsPartialCurl()
        {
            var w = Hand(indexBendDeg: 35f);   // 105° total
            var curl = FingerCurl.Compute(w, Finger.Index);
            Assert.That(curl, Is.GreaterThan(0.35f).And.LessThan(0.65f));
        }

        [Test]
        public void ThumbUsesItsTwoJoints()
        {
            var w = Hand(thumbBendDeg: 50f);   // 100° total across MCP + IP
            Assert.That(FingerCurl.Compute(w, Finger.Thumb), Is.GreaterThan(0.9f));
        }

        [Test]
        public void CurlIsOrientationIndependent()
        {
            var w = Hand(indexBendDeg: 60f);
            var rotated = new float[w.Length];
            // Rotate the whole hand 90° about y and mirror x: joint angles must be unchanged.
            for (var i = 0; i < HandFrameBuilder.LandmarkCount; i++)
            {
                rotated[i * 3] = -w[i * 3 + 2];
                rotated[i * 3 + 1] = w[i * 3 + 1];
                rotated[i * 3 + 2] = w[i * 3];
            }
            Assert.That(FingerCurl.Compute(rotated, Finger.Index), Is.EqualTo(FingerCurl.Compute(w, Finger.Index)).Within(1e-4f));
        }

        [Test]
        public void HandednessLabelsAreSwappedForUnmirroredInput()
        {
            Assert.That(HandFrameBuilder.IsUserLeft("Left", imageIsMirrored: false), Is.False, "MediaPipe assumes a selfie mirror");
            Assert.That(HandFrameBuilder.IsUserLeft("Right", imageIsMirrored: false), Is.True);
            Assert.That(HandFrameBuilder.IsUserLeft("Left", imageIsMirrored: true), Is.True);
            Assert.That(HandFrameBuilder.IsUserLeft("Left", imageIsMirrored: false, swap: true), Is.True);
        }

        [Test]
        public void MirrorAssignsUserLeftHandToAvatarRightHand()
        {
            var frame = HandFrameBuilder.Build(0, Hand(indexBendDeg: 70f, otherBendDeg: 70f, thumbBendDeg: 50f), 0.9f, null, 0f);
            var solver = new FingerCurlSolver(new HandTrackingSettings { Smoothing = 0f });
            solver.Submit(frame, now: 1.0, mirrorUser: true);
            var pose = solver.Update(1f / 60f, 1.0, enabled: true);
            Assert.That(pose.Right.Tracked, Is.True);
            Assert.That(pose.Right.Index, Is.GreaterThan(0.9f));
            Assert.That(pose.Left.Tracked, Is.False);
            Assert.That(pose.Left.Index, Is.EqualTo(0.1f).Within(1e-3f), "untracked hand rests slightly curled");

            solver.Reset();
            solver.Submit(frame, now: 1.0, mirrorUser: false);
            pose = solver.Update(1f / 60f, 1.0, enabled: true);
            Assert.That(pose.Left.Tracked, Is.True);
            Assert.That(pose.Left.Index, Is.GreaterThan(0.9f));
        }

        [Test]
        public void LostHandEasesBackToRest()
        {
            var frame = HandFrameBuilder.Build(0, Hand(indexBendDeg: 70f), 0.9f, null, 0f);
            var solver = new FingerCurlSolver(new HandTrackingSettings { Smoothing = 0.5f });
            solver.Submit(frame, 0.0, true);
            var pose = solver.Update(1f / 60f, 0.0, true);
            Assert.That(pose.Right.Index, Is.GreaterThan(0.9f), "first update snaps");

            // No new frames for two seconds: the hand is lost after the timeout and relaxes.
            for (var i = 1; i <= 120; i++) pose = solver.Update(1f / 60f, i / 60.0, true);
            Assert.That(pose.Right.Tracked, Is.False);
            Assert.That(pose.Right.Index, Is.LessThan(0.15f));
        }

        [Test]
        public void LowConfidenceHandIsIgnored()
        {
            var frame = HandFrameBuilder.Build(0, Hand(indexBendDeg: 70f), 0.2f, null, 0f);
            var solver = new FingerCurlSolver(new HandTrackingSettings { Smoothing = 0f });
            solver.Submit(frame, 0.0, true);
            var pose = solver.Update(1f / 60f, 0.0, true);
            Assert.That(pose.Right.Tracked, Is.False);
        }

        [Test]
        public void DisabledKeepsHandsAtRest()
        {
            var frame = HandFrameBuilder.Build(0, Hand(indexBendDeg: 70f), 0.9f, Hand(indexBendDeg: 70f), 0.9f);
            var solver = new FingerCurlSolver(new HandTrackingSettings { Smoothing = 0f });
            solver.Submit(frame, 0.0, true);
            var pose = solver.Update(1f / 60f, 0.0, enabled: false);
            Assert.That(pose.Left.Tracked, Is.False);
            Assert.That(pose.Left.Index, Is.EqualTo(0.1f).Within(1e-3f));
        }

        [Test]
        public void FoldedFingerWithFlatDepthStillCurlsByDistance()
        {
            // Landmarks whose joint angles read small (depth collapsed) but whose tip sits back near the knuckle.
            var w = Hand();
            void Set(int i, float x, float y, float z) { w[i * 3] = x; w[i * 3 + 1] = y; w[i * 3 + 2] = z; }
            Set(HandFrameBuilder.IndexMcp, 0.09f, 0f, 0.03f);
            Set(HandFrameBuilder.IndexPip, 0.12f, 0f, 0.03f);
            Set(HandFrameBuilder.IndexDip, 0.12f, -0.03f, 0.03f);
            Set(HandFrameBuilder.IndexTip, 0.095f, -0.03f, 0.03f);
            Assert.That(FingerCurl.Compute(w, Finger.Index), Is.GreaterThan(0.6f));
        }

        [Test]
        public void BodyModeFlagsFollowTheMode()
        {
            var s = new BodyTrackingSettings { Mode = BodyTrackingMode.UpperBodyArmsFingers };
            Assert.That(s.ArmsEnabled, Is.True);
            Assert.That(s.HandsEnabled, Is.True);
            s.Mode = BodyTrackingMode.UpperBodyArms;
            Assert.That(s.HandsEnabled, Is.False);
        }
    }
}
