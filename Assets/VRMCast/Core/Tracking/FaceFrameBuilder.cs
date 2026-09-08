using System;
using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// Converts MediaPipe Face Landmarker output (blendshape coefficients plus the facial transformation basis) into
    /// the provider-neutral <see cref="TrackingFrame"/> (PRD 8). Pure C# so it runs on the inference callback thread
    /// and can be unit tested.
    /// </summary>
    public static class FaceFrameBuilder
    {
        /// <summary>
        /// When true (default) MediaPipe's Left/Right blendshape names are treated as image sides and swapped into the
        /// user's own sides. Mirrored camera feeds set it to false. Read on the inference thread; written by settings.
        /// </summary>
        public static volatile bool SwapLeftRight = true;

        /// <summary>Builds a frame for a detected face.</summary>
        /// <param name="blendshapes">Coefficient by MediaPipe name; the dictionary is stored, not copied.</param>
        /// <param name="forward">Face forward axis in camera space (toward the camera when facing it). Null when no matrix was output.</param>
        /// <param name="up">Face up axis in camera space.</param>
        /// <param name="position">Head position in camera space, meters (x right, y up, z away from the camera).</param>
        public static TrackingFrame Build(double timestamp, Dictionary<string, float> blendshapes,
            float[] forward, float[] up, float[] position)
        {
            var frame = new TrackingFrame
            {
                Timestamp = timestamp,
                Blendshapes = blendshapes,
                FaceConfidence = 1f,
            };

            if (forward != null && up != null && forward.Length >= 3 && up.Length >= 3)
            {
                HeadPoseMath.ExtractAngles(forward[0], forward[1], forward[2], up[0], up[1], up[2],
                    out frame.Head.PitchRad, out frame.Head.YawRad, out frame.Head.RollRad);
            }
            if (position != null && position.Length >= 3)
            {
                frame.Head.PositionX = position[0];
                frame.Head.PositionY = position[1];
                frame.Head.PositionZ = position[2];
            }

            if (SwapLeftRight) MediaPipeBlendshapes.SwapSides(blendshapes);
            var s = blendshapes;
            frame.Eyes.BlinkLeft = MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeBlinkLeft);
            frame.Eyes.BlinkRight = MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeBlinkRight);
            frame.Eyes.LookX = ComputeLookX(s);
            frame.Eyes.LookY = ComputeLookY(s);

            frame.Mouth.Open = MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.JawOpen);
            frame.Mouth.Smile = Math.Max(
                MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.MouthSmileLeft),
                MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.MouthSmileRight));
            frame.Mouth.Pucker = MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.MouthPucker);
            frame.Mouth.Funnel = MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.MouthFunnel);
            return frame;
        }

        /// <summary>Frame reported when no face is detected.</summary>
        public static TrackingFrame NoFace(double timestamp) => new TrackingFrame { Timestamp = timestamp, FaceConfidence = 0f };

        /// <summary>-1..1, positive when the user looks toward their own right.</summary>
        public static float ComputeLookX(IReadOnlyDictionary<string, float> s)
        {
            var right = MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeLookOutRight) + MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeLookInLeft);
            var left = MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeLookOutLeft) + MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeLookInRight);
            return SmoothingMath.Clamp((right - left) * 0.5f, -1f, 1f);
        }

        /// <summary>-1..1, positive when the user looks up.</summary>
        public static float ComputeLookY(IReadOnlyDictionary<string, float> s)
        {
            var upv = MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeLookUpLeft) + MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeLookUpRight);
            var down = MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeLookDownLeft) + MediaPipeBlendshapes.Get(s, MediaPipeBlendshapes.EyeLookDownRight);
            return SmoothingMath.Clamp((upv - down) * 0.5f, -1f, 1f);
        }
    }
}
