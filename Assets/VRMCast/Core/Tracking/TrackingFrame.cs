using System.Collections.Generic;

namespace VRMCast.Core.Tracking
{
    /// <summary>
    /// Common tracking data contract (PRD 8). Every tracking engine converts its native output into this
    /// structure; no provider-specific object may cross into the avatar runtime. Ranges: expression/blink/mouth
    /// 0..1, look -1..1, head rotation radians, head position normalized to the camera frame.
    /// </summary>
    public struct TrackingFrame
    {
        public double Timestamp;

        public HeadTracking Head;
        public EyeTracking Eyes;
        public MouthTracking Mouth;

        /// <summary>Raw blendshape coefficients keyed by provider-neutral names (e.g. "jawOpen"). May be null.</summary>
        public Dictionary<string, float> Blendshapes;

        public PoseTracking? Pose;
        public HandTracking? LeftHand;
        public HandTracking? RightHand;

        public float FaceConfidence;
        public float PoseConfidence;

        public static TrackingFrame Empty(double timestamp) => new TrackingFrame { Timestamp = timestamp };
    }

    public struct HeadTracking
    {
        /// <summary>Radians. Positive pitch looks down, positive yaw turns toward the avatar's left, positive roll tilts clockwise as seen by the camera.</summary>
        public float PitchRad;
        public float YawRad;
        public float RollRad;

        /// <summary>Head center in the camera frame, normalized -1..1 (x right, y up); z is relative depth.</summary>
        public float PositionX;
        public float PositionY;
        public float PositionZ;
    }

    public struct EyeTracking
    {
        public float BlinkLeft;
        public float BlinkRight;
        public float LookX;
        public float LookY;
    }

    public struct MouthTracking
    {
        public float Open;
        public float Smile;
        public float Pucker;
        public float Funnel;
    }

    /// <summary>
    /// Upper-body landmarks in world space (meters, origin between the hips, x right in the image, y down, z away from
    /// the camera), as produced by <see cref="PoseFrameBuilder"/>. Left/Right are the user's own sides.
    /// </summary>
    public struct PoseTracking
    {
        public float LeftShoulderX, LeftShoulderY, LeftShoulderZ;
        public float RightShoulderX, RightShoulderY, RightShoulderZ;
        public float LeftHipX, LeftHipY, LeftHipZ;
        public float RightHipX, RightHipY, RightHipZ;
        public float NoseX, NoseY, NoseZ;
        public float LeftElbowX, LeftElbowY, LeftElbowZ, LeftElbowVisibility;
        public float RightElbowX, RightElbowY, RightElbowZ, RightElbowVisibility;
        public float LeftWristX, LeftWristY, LeftWristZ, LeftWristVisibility;
        public float RightWristX, RightWristY, RightWristZ, RightWristVisibility;
        public float Confidence;

        /// <summary>
        /// Normalized image coordinates (u right 0..1, v down 0..1) of the same joints, when the provider supplies
        /// them; <see cref="ImageAspect"/> is width / height so distances can be measured isotropically.
        /// </summary>
        public bool HasImageCoords;
        public float ImageAspect;
        public float LeftShoulderU, LeftShoulderV, RightShoulderU, RightShoulderV;
        public float LeftElbowU, LeftElbowV, RightElbowU, RightElbowV;
        public float LeftWristU, LeftWristV, RightWristU, RightWristV;
    }

    /// <summary>
    /// One hand from the hand landmarker: 21 world landmarks (meters, origin at the hand's geometric center, x right in
    /// the image, y down, z away from the camera) in MediaPipe order, see <see cref="HandFrameBuilder"/>. Which hand it
    /// is has already been resolved to the user's own side by the provider.
    /// </summary>
    public struct HandTracking
    {
        /// <summary>Handedness score 0..1.</summary>
        public float Confidence;
        /// <summary>21 × (x, y, z) world landmarks, meters.</summary>
        public float[] LandmarksXyz;
        /// <summary>Raw MediaPipe handedness label ("Left" == true) before side resolution.</summary>
        public bool LabelLeft;
        /// <summary>Normalized image coordinates of the wrist and three palm knuckles (u right, v down).</summary>
        public float WristU, WristV;
        public float MiddleMcpU, MiddleMcpV;
        public float IndexMcpU, IndexMcpV;
        public float LittleMcpU, LittleMcpV;
    }
}
