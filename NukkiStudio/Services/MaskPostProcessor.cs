using System;
using System.Collections.Generic;
using System.Windows;
using NukkiStudio.Models;

namespace NukkiStudio.Services;

public record BrushStrokePoint(double X, double Y, double Radius, bool IsInclude);
public record MagicWandPoint(double X, double Y, double Tolerance, bool IsInclude);
public record LassoPolygon(List<Point> Points, bool IsInclude);

/// <summary>
/// High-performance mask post-processor providing:
/// - Edge-aware Magic Wand area selection/removal
/// - Freehand & Polygon Lasso isolation
/// - Interactive include/exclude brush strokes with continuous interpolation
/// - Bounding box (ROI) subject isolation
/// - Edge Defringe / Color Decontamination (cleans white/key halos on border)
/// - Morphological Erode (Choke) and Dilate
/// - Edge Feathering (Separable Box Blur)
/// - Mask Contrast and Threshold clamping
/// </summary>
public static class MaskPostProcessor
{
    public static unsafe void ApplyPostProcessing(
        byte[] alphaMask,
        int width,
        int height,
        MattingSettings settings,
        byte[]? rgbaBuffer = null)
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

        // 2. Apply Lasso Polygons (Include / Exclude)
        if (settings.LassoPolygons != null && settings.LassoPolygons.Count > 0)
        {
            fixed (byte* pMask = alphaMask)
            {
                foreach (var lasso in settings.LassoPolygons)
                {
                    if (lasso.Points.Count < 3) continue;

                    // Calculate polygon bounding box
                    double polyMinX = double.MaxValue, polyMaxX = double.MinValue;
                    double polyMinY = double.MaxValue, polyMaxY = double.MinValue;
                    foreach (var pt in lasso.Points)
                    {
                        if (pt.X < polyMinX) polyMinX = pt.X;
                        if (pt.X > polyMaxX) polyMaxX = pt.X;
                        if (pt.Y < polyMinY) polyMinY = pt.Y;
                        if (pt.Y > polyMaxY) polyMaxY = pt.Y;
                    }

                    int bMinX = Math.Clamp((int)polyMinX, 0, width - 1);
                    int bMaxX = Math.Clamp((int)polyMaxX, 0, width - 1);
                    int bMinY = Math.Clamp((int)polyMinY, 0, height - 1);
                    int bMaxY = Math.Clamp((int)polyMaxY, 0, height - 1);

                    byte targetVal = lasso.IsInclude ? (byte)255 : (byte)0;

                    for (int y = bMinY; y <= bMaxY; y++)
                    {
                        int row = y * width;
                        for (int x = bMinX; x <= bMaxX; x++)
                        {
                            if (IsPointInPolygon(x, y, lasso.Points))
                            {
                                pMask[row + x] = targetVal;
                            }
                        }
                    }
                }
            }
        }

        // 3. Apply Edge-Aware Magic Wand Points
        if (rgbaBuffer != null && settings.MagicWandPoints != null && settings.MagicWandPoints.Count > 0)
        {
            ApplyMagicWand(alphaMask, rgbaBuffer, width, height, settings.MagicWandPoints);
        }

        // 4. Apply Interactive Brush Strokes (Include = 255, Exclude = 0)
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

        // 5. Morphological Erode (Choke) or Dilate
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

        // 6. Contrast & Threshold adjustment (Black/White level clipping)
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

        // 7. Feathering (Separable Box Blur)
        if (settings.MaskFeather > 0)
        {
            ApplyBoxBlur(alphaMask, width, height, settings.MaskFeather);
        }

        // 8. Color Decontamination / Defringe (Removes background white/key bleed on edges)
        if (rgbaBuffer != null && settings.EnableDefringe && settings.DefringeRadius > 0)
        {
            ApplyDefringe(rgbaBuffer, alphaMask, width, height, settings.DefringeRadius);
        }
    }

    private static bool IsPointInPolygon(double x, double y, List<Point> poly)
    {
        bool inside = false;
        int j = poly.Count - 1;
        for (int i = 0; i < poly.Count; i++)
        {
            if ((poly[i].Y < y && poly[j].Y >= y || poly[j].Y < y && poly[i].Y >= y) &&
                (poly[i].X <= x || poly[j].X <= x))
            {
                if (poly[i].X + (y - poly[i].Y) / (poly[j].Y - poly[i].Y) * (poly[j].X - poly[i].X) < x)
                {
                    inside = !inside;
                }
            }
            j = i;
        }
        return inside;
    }

    private static unsafe void ApplyMagicWand(
        byte[] alphaMask,
        byte[] rgbaBuffer,
        int width,
        int height,
        List<MagicWandPoint> wandPoints)
    {
        int totalPixels = width * height;
        int[] queue = new int[totalPixels];
        bool[] visited = new bool[totalPixels];

        fixed (byte* pMask = alphaMask)
        fixed (byte* pRgba = rgbaBuffer)
        {
            foreach (var wand in wandPoints)
            {
                int sx = Math.Clamp((int)wand.X, 0, width - 1);
                int sy = Math.Clamp((int)wand.Y, 0, height - 1);
                int seedIdx = sy * width + sx;

                byte seedR = pRgba[seedIdx * 4];
                byte seedG = pRgba[seedIdx * 4 + 1];
                byte seedB = pRgba[seedIdx * 4 + 2];

                byte targetVal = wand.IsInclude ? (byte)255 : (byte)0;
                double maxDist = wand.Tolerance * Math.Sqrt(3) * 255.0;

                Array.Clear(visited, 0, visited.Length);
                int head = 0, tail = 0;

                visited[seedIdx] = true;
                queue[tail++] = seedIdx;

                while (head < tail)
                {
                    int curr = queue[head++];
                    pMask[curr] = targetVal;

                    int cx = curr % width;
                    int cy = curr / width;

                    int cr = pRgba[curr * 4];
                    int cg = pRgba[curr * 4 + 1];
                    int cb = pRgba[curr * 4 + 2];

                    for (int n = 0; n < 4; n++)
                    {
                        if (n == 0 && cy == 0) continue;
                        if (n == 1 && cy == height - 1) continue;
                        if (n == 2 && cx == 0) continue;
                        if (n == 3 && cx == width - 1) continue;

                        int nIdx = n switch
                        {
                            0 => curr - width,
                            1 => curr + width,
                            2 => curr - 1,
                            _ => curr + 1
                        };

                        if (visited[nIdx]) continue;
                        visited[nIdx] = true;

                        int nr = pRgba[nIdx * 4];
                        int ng = pRgba[nIdx * 4 + 1];
                        int nb = pRgba[nIdx * 4 + 2];

                        int dr = nr - seedR;
                        int dg = ng - seedG;
                        int db = nb - seedB;
                        double dist = Math.Sqrt(dr * dr + dg * dg + db * db);

                        int stepDiff = Math.Abs(nr - cr) + Math.Abs(ng - cg) + Math.Abs(nb - cb);

                        if (dist <= maxDist && stepDiff < 50)
                        {
                            queue[tail++] = nIdx;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Edge Defringe / Color Decontamination:
    /// Replaces washed-out background halo colors on semi-transparent boundary pixels
    /// with true solid foreground colors sampled from neighboring interior pixels.
    /// </summary>
    public static unsafe void ApplyDefringe(
        byte[] rgbaBuffer,
        byte[] alphaMask,
        int width,
        int height,
        int radius)
    {
        if (radius <= 0) return;

        fixed (byte* pRgba = rgbaBuffer)
        fixed (byte* pMask = alphaMask)
        {
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = row + x;
                    byte a = pMask[idx];

                    // Only decontaminate transition edge pixels
                    if (a > 10 && a < 240)
                    {
                        // Look for the closest solid foreground pixel (a >= 240)
                        int bestDistSq = int.MaxValue;
                        byte bestR = 0, bestG = 0, bestB = 0;
                        bool found = false;

                        int yMin = Math.Max(0, y - radius);
                        int yMax = Math.Min(height - 1, y + radius);
                        int xMin = Math.Max(0, x - radius);
                        int xMax = Math.Min(width - 1, x + radius);

                        for (int sy = yMin; sy <= yMax; sy++)
                        {
                            int sRow = sy * width;
                            int dy = sy - y;
                            for (int sx = xMin; sx <= xMax; sx++)
                            {
                                int sIdx = sRow + sx;
                                if (pMask[sIdx] >= 240)
                                {
                                    int dx = sx - x;
                                    int dSq = dx * dx + dy * dy;
                                    if (dSq < bestDistSq)
                                    {
                                        bestDistSq = dSq;
                                        bestR = pRgba[sIdx * 4];
                                        bestG = pRgba[sIdx * 4 + 1];
                                        bestB = pRgba[sIdx * 4 + 2];
                                        found = true;
                                    }
                                }
                            }
                        }

                        if (found)
                        {
                            // Replace contaminated fringe RGB with pure interior foreground color.
                            // The alpha channel already preserves the smooth anti-aliased edge.
                            pRgba[idx * 4] = bestR;
                            pRgba[idx * 4 + 1] = bestG;
                            pRgba[idx * 4 + 2] = bestB;
                        }
                    }
                }
            }
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
