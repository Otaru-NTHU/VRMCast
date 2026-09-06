using System;

namespace VRMCast.Core.Camera
{
    /// <summary>
    /// World-space landmarks of the loaded avatar used by the framing solver. Values are heights (Y) in
    /// meters plus the horizontal center and width. Built from humanoid bones when available, otherwise
    /// from renderer bounds with typical human proportions.
    /// </summary>
    public sealed class AvatarMetrics
    {
        public float FootY { get; }
        public float HipsY { get; }
        public float ChestY { get; }
        public float NeckY { get; }
        public float HeadY { get; }
        public float TopY { get; }
        public float CenterX { get; }
        public float CenterZ { get; }
        public float Width { get; }

        public float Height => Math.Max(TopY - FootY, 0.01f);

        public AvatarMetrics(float footY, float hipsY, float chestY, float neckY, float headY, float topY,
            float centerX, float centerZ, float width)
        {
            FootY = footY;
            HipsY = hipsY;
            ChestY = chestY;
            NeckY = neckY;
            HeadY = headY;
            TopY = topY;
            CenterX = centerX;
            CenterZ = centerZ;
            Width = Math.Max(width, 0.01f);
        }

        /// <summary>
        /// Fallback metrics from a bounding box (min/max Y, horizontal center, width) using average
        /// human proportions. Used when an avatar exposes no humanoid bones.
        /// </summary>
        public static AvatarMetrics FromBounds(float minY, float maxY, float centerX, float centerZ, float width)
        {
            var h = Math.Max(maxY - minY, 0.01f);
            return new AvatarMetrics(
                footY: minY,
                hipsY: minY + h * 0.52f,
                chestY: minY + h * 0.72f,
                neckY: minY + h * 0.85f,
                headY: minY + h * 0.90f,
                topY: maxY,
                centerX: centerX,
                centerZ: centerZ,
                width: width);
        }

        /// <summary>A 1.6 m placeholder used when no avatar is loaded so the camera still has a sane pose.</summary>
        public static AvatarMetrics Placeholder => FromBounds(0f, 1.6f, 0f, 0f, 0.5f);
    }
}
