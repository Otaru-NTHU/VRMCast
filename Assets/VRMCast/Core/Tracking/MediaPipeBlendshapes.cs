using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// The 52 blendshape coefficient names produced by MediaPipe Face Landmarker (ARKit-compatible). "Left"/"Right"
    /// refer to the user's own sides. Names are used verbatim as expression-mapping sources.
    /// </summary>
    public static class MediaPipeBlendshapes
    {
        public const string Neutral = "_neutral";
        public const string BrowDownLeft = "browDownLeft";
        public const string BrowDownRight = "browDownRight";
        public const string BrowInnerUp = "browInnerUp";
        public const string BrowOuterUpLeft = "browOuterUpLeft";
        public const string BrowOuterUpRight = "browOuterUpRight";
        public const string CheekPuff = "cheekPuff";
        public const string CheekSquintLeft = "cheekSquintLeft";
        public const string CheekSquintRight = "cheekSquintRight";
        public const string EyeBlinkLeft = "eyeBlinkLeft";
        public const string EyeBlinkRight = "eyeBlinkRight";
        public const string EyeLookDownLeft = "eyeLookDownLeft";
        public const string EyeLookDownRight = "eyeLookDownRight";
        public const string EyeLookInLeft = "eyeLookInLeft";
        public const string EyeLookInRight = "eyeLookInRight";
        public const string EyeLookOutLeft = "eyeLookOutLeft";
        public const string EyeLookOutRight = "eyeLookOutRight";
        public const string EyeLookUpLeft = "eyeLookUpLeft";
        public const string EyeLookUpRight = "eyeLookUpRight";
        public const string EyeSquintLeft = "eyeSquintLeft";
        public const string EyeSquintRight = "eyeSquintRight";
        public const string EyeWideLeft = "eyeWideLeft";
        public const string EyeWideRight = "eyeWideRight";
        public const string JawForward = "jawForward";
        public const string JawLeft = "jawLeft";
        public const string JawOpen = "jawOpen";
        public const string JawRight = "jawRight";
        public const string MouthClose = "mouthClose";
        public const string MouthDimpleLeft = "mouthDimpleLeft";
        public const string MouthDimpleRight = "mouthDimpleRight";
        public const string MouthFrownLeft = "mouthFrownLeft";
        public const string MouthFrownRight = "mouthFrownRight";
        public const string MouthFunnel = "mouthFunnel";
        public const string MouthLeft = "mouthLeft";
        public const string MouthLowerDownLeft = "mouthLowerDownLeft";
        public const string MouthLowerDownRight = "mouthLowerDownRight";
        public const string MouthPressLeft = "mouthPressLeft";
        public const string MouthPressRight = "mouthPressRight";
        public const string MouthPucker = "mouthPucker";
        public const string MouthRight = "mouthRight";
        public const string MouthRollLower = "mouthRollLower";
        public const string MouthRollUpper = "mouthRollUpper";
        public const string MouthShrugLower = "mouthShrugLower";
        public const string MouthShrugUpper = "mouthShrugUpper";
        public const string MouthSmileLeft = "mouthSmileLeft";
        public const string MouthSmileRight = "mouthSmileRight";
        public const string MouthStretchLeft = "mouthStretchLeft";
        public const string MouthStretchRight = "mouthStretchRight";
        public const string MouthUpperUpLeft = "mouthUpperUpLeft";
        public const string MouthUpperUpRight = "mouthUpperUpRight";
        public const string NoseSneerLeft = "noseSneerLeft";
        public const string NoseSneerRight = "noseSneerRight";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Neutral, BrowDownLeft, BrowDownRight, BrowInnerUp, BrowOuterUpLeft, BrowOuterUpRight, CheekPuff,
            CheekSquintLeft, CheekSquintRight, EyeBlinkLeft, EyeBlinkRight, EyeLookDownLeft, EyeLookDownRight,
            EyeLookInLeft, EyeLookInRight, EyeLookOutLeft, EyeLookOutRight, EyeLookUpLeft, EyeLookUpRight,
            EyeSquintLeft, EyeSquintRight, EyeWideLeft, EyeWideRight, JawForward, JawLeft, JawOpen, JawRight,
            MouthClose, MouthDimpleLeft, MouthDimpleRight, MouthFrownLeft, MouthFrownRight, MouthFunnel, MouthLeft,
            MouthLowerDownLeft, MouthLowerDownRight, MouthPressLeft, MouthPressRight, MouthPucker, MouthRight,
            MouthRollLower, MouthRollUpper, MouthShrugLower, MouthShrugUpper, MouthSmileLeft, MouthSmileRight,
            MouthStretchLeft, MouthStretchRight, MouthUpperUpLeft, MouthUpperUpRight, NoseSneerLeft, NoseSneerRight,
        };

        public static float Get(IReadOnlyDictionary<string, float> shapes, string name)
        {
            return shapes != null && shapes.TryGetValue(name, out var v) ? v : 0f;
        }
    }
}
