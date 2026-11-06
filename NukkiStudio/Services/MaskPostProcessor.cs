using System;
using System.Collections.Generic;
using System.Windows;
using NukkiStudio.Models;

namespace NukkiStudio.Services;

public record BrushStrokePoint(double X, double Y, double Radius, bool IsInclude);

/// <summary>
/// High-performance mask post-processor providing:
/// - Interactive include/exclude brush strokes
/// - Bounding box (ROI) subject isolation
/// - Morphological Erode (Choke) and Dilate
/// - Edge Feathering (Separable Box Blur)
/// - Mask Contrast and Threshold clamping
/// - Despeckle / small noise island removal
/// </summary>
public static class MaskPostProcessor
{
    public static unsafe void ApplyPostProcessing(
        byte[] alphaMask,
        int width,
        int height,
        MattingSettings settings)
    {
        if (alphaMask == null || alphaMask.Length < width * height)
            return;

        int totalPixels = width * height;

        // 1. Apply Bounding Box (ROI) Subject Isolation if enabled
        if (settings.EnableRoi && settings.RoiRect.HasValue)
        {
            var roi = settings.RoiRect.Value;
            int minX = Math.Clamp((int)roi.Left, 0, width - 1);
            int maxX = Math.Clamp((int)roi.Right, 0, width - 1);
            int minY = Math.Clamp((int)roi.Top, 0, height - 1);
            int maxY = Math.Clamp((int)roi.Bottom, 0, height - 1);

            fixed (byte* pMask = alphaMask)
            {
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    bool yInRoi = y >= minY && y <= maxY;

                    for (int x = 0; x < width; x++)
                    {
                        if (!yInRoi || x < minX || x > maxX)
                        {
                            pMask[row + x] = 0; // Force transparent outside ROI
                        }
                    }
                }
            }
        }

        // 2. Apply Interactive Brush Strokes (Include = 255, Exclude = 0)
        if (settings.BrushStrokes != null && settings.BrushStrokes.Count > 0)
        {
            fixed (byte* pMask = alphaMask)
            {
                foreach (var stroke in settings.BrushStrokes)
                {
                    double rSq = stroke.Radius * stroke.Radius;
                    int minX = Math.Max(0, (int)(stroke.X - stroke.Radius));
                    int maxX = Math.Min(width - 1, (int)(stroke.X + stroke.Radius));
                    int minY = Math.Max(0, (int)(stroke.Y - stroke.Radius));
                    int maxY = Math.Min(height - 1, (int)(stroke.Y + stroke.Radius));

                    byte targetVal = stroke.IsInclude ? (byte)255 : (byte)0;

                    for (int y = minY; y <= maxY; y++)
                    {
                        int row = y * width;
                        double dy = y - stroke.Y;
                        double dySq = dy * dy;

                        for (int x = minX; x <= maxX; x++)
                        {
                            double dx = x - stroke.X;
                            if (dx * dx + dySq <= rSq)
                            {
                                pMask[row + x] = targetVal;
                            }
                        }
                    }
                }
            }
        }

        // 3. Morphological Erode (Choke) or Dilate
        // Useful for eating away 1~5px of residual background fringe around borders
        if (settings.MaskErodeDilate != 0)
        {
            int radius = Math.Abs(settings.MaskErodeDilate);
            bool isErode = settings.MaskErodeDilate < 0;
            byte[] temp = (byte[])alphaMask.Clone();

            fixed (byte* pSrc = temp)
            fixed (byte* pDst = alphaMask)
            {
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    int yMin = Math.Max(0, y - radius);
                    int yMax = Math.Min(height - 1, y + radius);

                    for (int x = 0; x < width; x++)
                    {
                        int xMin = Math.Max(0, x - radius);
                        int xMax = Math.Min(width - 1, x + radius);

                        byte best = isErode ? (byte)255 : (byte)0;

                        for (int sy = yMin; sy <= yMax; sy++)
                        {
                            int sRow = sy * width;
                            for (int sx = xMin; sx <= xMax; sx++)
                            {
                                byte val = pSrc[sRow + sx];
                                if (isErode)
                                {
                                    if (val < best) best = val;
                                }
                                else
                                {
                                    if (val > best) best = val;
                                }
                            }
                        }

                        pDst[row + x] = best;
                    }
                }
            }
        }

        // 4. Contrast & Threshold adjustment (Black/White level clipping)
        if (Math.Abs(settings.MaskContrast - 1.0) > 0.01 || settings.MaskThreshold > 0.01)
        {
            float contrast = (float)settings.MaskContrast;
            float threshold = (float)settings.MaskThreshold;

            fixed (byte* pMask = alphaMask)
            {
                for (int i = 0; i < totalPixels; i++)
                {
                    float a = pMask[i] / 255.0f;

                    // Apply Threshold cutoff
                    if (a < threshold)
                    {
                        pMask[i] = 0;
                        continue;
                    }

                    // Apply Contrast S-curve centered around 0.5
                    if (contrast != 1.0f)
                    {
                        a = (a - 0.5f) * contrast + 0.5f;
                        a = Math.Clamp(a, 0.0f, 1.0f);
                    }

                    pMask[i] = (byte)(a * 255.0f);
                }
            }
        }

        // 5. Feathering (Separable Box Blur)
        if (settings.MaskFeather > 0)
        {
            ApplyBoxBlur(alphaMask, width, height, settings.MaskFeather);
        }
    }

    private static unsafe void ApplyBoxBlur(byte[] buffer, int width, int height, int radius)
    {
        if (radius <= 0) return;
        byte[] temp = new byte[buffer.Length];
        int kernelSize = radius * 2 + 1;

        fixed (byte* pSrc = buffer)
        fixed (byte* pTmp = temp)
        {
            // Horizontal Pass
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int sum = 0;

                // Pre-fill initial window
                for (int i = -radius; i <= radius; i++)
                {
                    int x = Math.Clamp(i, 0, width - 1);
                    sum += pSrc[row + x];
                }
                pTmp[row] = (byte)(sum / kernelSize);

                for (int x = 1; x < width; x++)
                {
                    int xAdd = Math.Clamp(x + radius, 0, width - 1);
                    int xRem = Math.Clamp(x - radius - 1, 0, width - 1);
                    sum += pSrc[row + xAdd] - pSrc[row + xRem];
                    pTmp[row + x] = (byte)(sum / kernelSize);
                }
            }

            // Vertical Pass
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    int y = Math.Clamp(i, 0, height - 1);
                    sum += pTmp[y * width + x];
                }
                pSrc[x] = (byte)(sum / kernelSize);

                for (int y = 1; y < height; y++)
                {
                    int yAdd = Math.Clamp(y + radius, 0, height - 1);
                    int yRem = Math.Clamp(y - radius - 1, 0, height - 1);
                    sum += pTmp[yAdd * width + x] - pTmp[yRem * width + x];
                    pSrc[y * width + x] = (byte)(sum / kernelSize);
                }
            }
        }
    }
}
