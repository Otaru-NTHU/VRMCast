using System;

namespace VRMCast.Core.Backgrounds
{
    /// <summary>Scale of the background image plane relative to the output frame (1 = exactly the frame's size on that axis).</summary>
    public readonly struct ImageFit
    {
        public float ScaleX { get; }
        public float ScaleY { get; }

        public ImageFit(float scaleX, float scaleY)
        {
            ScaleX = scaleX;
            ScaleY = scaleY;
        }
    }

    /// <summary>Fill / Fit / Stretch math for the image background (PRD 17.3). Fill is the default.</summary>
    public static class ImageFitSolver
    {
        public static ImageFit Solve(float imageWidth, float imageHeight, float frameWidth, float frameHeight, ImageFitMode mode)
        {
            if (imageWidth <= 0 || imageHeight <= 0 || frameWidth <= 0 || frameHeight <= 0)
                return new ImageFit(1f, 1f);

            var imageAspect = imageWidth / imageHeight;
            var frameAspect = frameWidth / frameHeight;

            switch (mode)
            {
                case ImageFitMode.Stretch:
                    return new ImageFit(1f, 1f);

                case ImageFitMode.Fit:
                    // Image is relatively wider: width matches the frame, height shrinks.
                    return imageAspect >= frameAspect
                        ? new ImageFit(1f, frameAspect / imageAspect)
                        : new ImageFit(imageAspect / frameAspect, 1f);

                case ImageFitMode.Fill:
                    // Image is relatively wider: height matches the frame, width overflows.
                    return imageAspect >= frameAspect
                        ? new ImageFit(imageAspect / frameAspect, 1f)
                        : new ImageFit(1f, frameAspect / imageAspect);

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }
}
