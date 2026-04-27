using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OdevTakip2.Services
{
    public static class ImageAnalyzer
    {
        private const int SampleSize     = 64;
        private const double Threshold   = 0.40;
        private const int MinDimension   = 150;

        /// <summary>
        /// Returns true when the image looks like a real-world photo
        /// (whiteboard, handwritten paper, printed sheet) rather than
        /// a digital screenshot or decorative graphic.
        /// </summary>
        public static bool IsLikelyHomeworkPhoto(string imagePath)
        {
            try
            {
                using var image = Image.Load<Rgba32>(imagePath);

                if (image.Width < MinDimension || image.Height < MinDimension)
                    return false;

                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(SampleSize, SampleSize),
                    Mode = ResizeMode.Stretch
                }));

                var uniqueColors = new HashSet<int>(SampleSize * SampleSize);
                long sameNeighborCount = 0;
                int  comparisons       = 0;

                for (int y = 0; y < SampleSize; y++)
                {
                    for (int x = 0; x < SampleSize; x++)
                    {
                        var px = image[x, y];
                        // 5-bit quantise per channel to reduce noise
                        int key = (px.R >> 3) << 10 | (px.G >> 3) << 5 | (px.B >> 3);
                        uniqueColors.Add(key);

                        if (x + 1 < SampleSize)
                        {
                            var right = image[x + 1, y];
                            int diff  = Math.Abs(px.R - right.R) + Math.Abs(px.G - right.G) + Math.Abs(px.B - right.B);
                            if (diff < 15) sameNeighborCount++;
                            comparisons++;
                        }
                        if (y + 1 < SampleSize)
                        {
                            var below = image[x, y + 1];
                            int diff  = Math.Abs(px.R - below.R) + Math.Abs(px.G - below.G) + Math.Abs(px.B - below.B);
                            if (diff < 15) sameNeighborCount++;
                            comparisons++;
                        }
                    }
                }

                double uniqueRatio  = uniqueColors.Count / (double)(SampleSize * SampleSize);
                double uniformRatio = comparisons > 0 ? sameNeighborCount / (double)comparisons : 0.0;
                double score        = uniqueRatio * 0.6 + (1.0 - uniformRatio) * 0.4;

                return score >= Threshold;
            }
            catch
            {
                return true; // keep on error — don't silently delete
            }
        }
    }
}
