using System;
using System.Collections.Generic;
using NukkiStudio.Models;

namespace NukkiStudio.Services;

/// <summary>
/// High-performance ChromaKey matting engine using direct memory operations.
/// Supports arbitrary key colors, tolerance thresholding, smoothstep transitions,
/// spill suppression, exterior contiguous flood-fill (to prevent internal hole punching),
/// and foreground protection seeds.
/// </summary>
public class ChromaKeyMattingEngine : IMattingEngine
{
    public string Name => "Chroma Key (Color Screen)";
    public bool IsAvailable => true;

    private const float InvSqrt3 = 0.577350269f; // 1.0f / sqrt(3.0f)

    public unsafe void ProcessRgbaBuffer(
        byte[] buffer,
        int width,
        int height,
        MattingSettings settings,
        byte[]? backgroundBuffer = null)
    {
        if (buffer == null || buffer.Length < width * height * 4)
            return;

        int totalPixels = width * height;
        byte[] mask = ComputeAlphaMask(buffer, width, height, settings);

        float keyR = settings.KeyColor.R / 255.0f;
        float keyG = settings.KeyColor.G / 255.0f;
        float keyB = settings.KeyColor.B / 255.0f;
        float spill = Math.Clamp((float)settings.SpillSuppression, 0.0f, 1.0f);
        bool isGreenDominant = keyG > keyR && keyG > keyB;
        bool isBlueDominant = keyB > keyR && keyB > keyG;

        bool hasCustomBg = settings.BgType switch
        {
            BackgroundType.GreenScreen => true,
            BackgroundType.CustomColor => true,
            BackgroundType.Gradient when backgroundBuffer != null && backgroundBuffer.Length >= totalPixels * 4 => true,
            BackgroundType.BlurOriginal when backgroundBuffer != null && backgroundBuffer.Length >= totalPixels * 4 => true,
            BackgroundType.CustomImage when backgroundBuffer != null && backgroundBuffer.Length >= totalPixels * 4 => true,
            _ => false
        };

        byte bgR = settings.BgType switch
        {
            BackgroundType.GreenScreen => 0,
            BackgroundType.CustomColor => settings.CustomBgColor.R,
            _ => 0
        };
        byte bgG = settings.BgType switch
        {
            BackgroundType.GreenScreen => 255,
            BackgroundType.CustomColor => settings.CustomBgColor.G,
            _ => 0
        };
        byte bgB = settings.BgType switch
        {
            BackgroundType.GreenScreen => 0,
            BackgroundType.CustomColor => settings.CustomBgColor.B,
            _ => 0
        };

        fixed (byte* pBuf = buffer)
        fixed (byte* pMask = mask)
        fixed (byte* pCustomBg = backgroundBuffer)
        {
            byte* ptr = pBuf;
            byte* mPtr = pMask;
            byte* bgPtr = pCustomBg;

            for (int i = 0; i < totalPixels; i++, ptr += 4, mPtr++)
            {
                byte alpha = *mPtr;
                byte r = ptr[0];
                byte g = ptr[1];
                byte b = ptr[2];

                // Spill Suppression on foreground
                if (alpha > 0 && spill > 0.0f)
                {
                    float rf = r / 255.0f;
                    float gf = g / 255.0f;
                    float bf = b / 255.0f;

                    if (isGreenDominant)
                    {
                        float avgRB = (rf + bf) * 0.5f;
                        if (gf > avgRB)
                        {
                            gf -= (gf - avgRB) * spill;
                            g = (byte)Math.Clamp((int)(gf * 255.0f), 0, 255);
                        }
                    }
                    else if (isBlueDominant)
                    {
                        float avgRG = (rf + gf) * 0.5f;
                        if (bf > avgRG)
                        {
                            bf -= (bf - avgRG) * spill;
                            b = (byte)Math.Clamp((int)(bf * 255.0f), 0, 255);
                        }
                    }
                }

                if (!hasCustomBg)
                {
                    // Transparent output
                    ptr[0] = r;
                    ptr[1] = g;
                    ptr[2] = b;
                    ptr[3] = alpha;
                }
                else
                {
                    // Composite with background
                    byte targetBgR = bgR;
                    byte targetBgG = bgG;
                    byte targetBgB = bgB;

                    if (bgPtr != null)
                    {
                        targetBgR = bgPtr[i * 4];
                        targetBgG = bgPtr[i * 4 + 1];
                        targetBgB = bgPtr[i * 4 + 2];
                    }

                    float aNorm = alpha / 255.0f;
                    float invAlpha = 1.0f - aNorm;

                    ptr[0] = (byte)Math.Clamp((int)(r * aNorm + targetBgR * invAlpha), 0, 255);
                    ptr[1] = (byte)Math.Clamp((int)(g * aNorm + targetBgG * invAlpha), 0, 255);
                    ptr[2] = (byte)Math.Clamp((int)(b * aNorm + targetBgB * invAlpha), 0, 255);
                    ptr[3] = 255;
                }
            }
        }

        // Apply visual styling (Color grading, Sticker stroke, etc.)
        ImageProcessor.ApplySubjectEffects(buffer, width, height, settings);
    }

    public unsafe byte[] ComputeAlphaMask(
        byte[] buffer,
        int width,
        int height,
        MattingSettings settings)
    {
        int totalPixels = width * height;
        byte[] mask = new byte[totalPixels];

        if (buffer == null || buffer.Length < totalPixels * 4)
            return mask;

        float keyR = settings.KeyColor.R / 255.0f;
        float keyG = settings.KeyColor.G / 255.0f;
        float keyB = settings.KeyColor.B / 255.0f;

        float secKeyR = settings.SecondaryKeyColor.R / 255.0f;
        float secKeyG = settings.SecondaryKeyColor.G / 255.0f;
        float secKeyB = settings.SecondaryKeyColor.B / 255.0f;
        float secTol = Math.Clamp((float)settings.SecondaryTolerance, 0.001f, 1.0f);
        bool hasSecKey = settings.EnableSecondaryKey;

        float tol = Math.Clamp((float)settings.Tolerance, 0.001f, 1.0f);
        float smooth = Math.Max(0.001f, (float)settings.Smoothness);
        float maxKeyDist = tol + smooth;

        // Step 1: Precompute distance & raw candidate background map
        float[] distances = new float[totalPixels];
        bool[] isCandidateBg = new bool[totalPixels];

        fixed (byte* pBuf = buffer)
        {
            byte* ptr = pBuf;
            for (int i = 0; i < totalPixels; i++, ptr += 4)
            {
                float rf = ptr[0] / 255.0f;
                float gf = ptr[1] / 255.0f;
                float bf = ptr[2] / 255.0f;

                float dr = rf - keyR;
                float dg = gf - keyG;
                float db = bf - keyB;
                float dist = MathF.Sqrt(dr * dr + dg * dg + db * db) * InvSqrt3;

                // Secondary key color check (e.g. floor shadows / studio gradients)
                if (hasSecKey)
                {
                    float sdr = rf - secKeyR;
                    float sdg = gf - secKeyG;
                    float sdb = bf - secKeyB;
                    float secDist = MathF.Sqrt(sdr * sdr + sdg * sdg + sdb * sdb) * InvSqrt3;
                    if (secDist <= secTol)
                    {
                        dist = MathF.Min(dist, secDist);
                    }
                }

                distances[i] = dist;
                isCandidateBg[i] = dist <= maxKeyDist;
            }
        }

        // Step 2: If ContiguousOnly is enabled, find only exterior-connected background pixels via BFS
        bool[] isExterior = new bool[totalPixels];
        if (settings.ContiguousOnly)
        {
            // Build impenetrable barrier map from protected points, include brush strokes, and include lasso polygons
            bool[] isBarrier = new bool[totalPixels];

            if (settings.ProtectedPoints != null && settings.ProtectedPoints.Count > 0)
            {
                double rSq = settings.ProtectRadius * settings.ProtectRadius;
                foreach (var pt in settings.ProtectedPoints)
                {
                    int minX = Math.Max(0, (int)(pt.X - settings.ProtectRadius));
                    int maxX = Math.Min(width - 1, (int)(pt.X + settings.ProtectRadius));
                    int minY = Math.Max(0, (int)(pt.Y - settings.ProtectRadius));
                    int maxY = Math.Min(height - 1, (int)(pt.Y + settings.ProtectRadius));
                    for (int y = minY; y <= maxY; y++)
                    {
                        int row = y * width;
                        double dy = y - pt.Y;
                        double dySq = dy * dy;
                        for (int x = minX; x <= maxX; x++)
                        {
                            double dx = x - pt.X;
                            if (dx * dx + dySq <= rSq) isBarrier[row + x] = true;
                        }
                    }
                }
            }

            if (settings.BrushStrokes != null && settings.BrushStrokes.Count > 0)
            {
                foreach (var stroke in settings.BrushStrokes)
                {
                    if (!stroke.IsInclude) continue;
                    double rSq = stroke.Radius * stroke.Radius;
                    int minX = Math.Max(0, (int)(stroke.X - stroke.Radius));
                    int maxX = Math.Min(width - 1, (int)(stroke.X + stroke.Radius));
                    int minY = Math.Max(0, (int)(stroke.Y - stroke.Radius));
                    int maxY = Math.Min(height - 1, (int)(stroke.Y + stroke.Radius));
                    for (int y = minY; y <= maxY; y++)
                    {
                        int row = y * width;
                        double dy = y - stroke.Y;
                        double dySq = dy * dy;
                        for (int x = minX; x <= maxX; x++)
                        {
                            double dx = x - stroke.X;
                            if (dx * dx + dySq <= rSq) isBarrier[row + x] = true;
                        }
                    }
                }
            }

            int[] queue = new int[totalPixels];
            int head = 0, tail = 0;

            // Seed from 4 outer image edges (Top & Bottom rows)
            for (int x = 0; x < width; x++)
            {
                int topIdx = x;
                int botIdx = (height - 1) * width + x;
                if (isCandidateBg[topIdx] && !isExterior[topIdx] && !isBarrier[topIdx])
                {
                    isExterior[topIdx] = true;
                    queue[tail++] = topIdx;
                }
                if (isCandidateBg[botIdx] && !isExterior[botIdx] && !isBarrier[botIdx])
                {
                    isExterior[botIdx] = true;
                    queue[tail++] = botIdx;
                }
            }

            // Seed from Left & Right columns
            for (int y = 0; y < height; y++)
            {
                int leftIdx = y * width;
                int rightIdx = y * width + (width - 1);
                if (isCandidateBg[leftIdx] && !isExterior[leftIdx] && !isBarrier[leftIdx])
                {
                    isExterior[leftIdx] = true;
                    queue[tail++] = leftIdx;
                }
                if (isCandidateBg[rightIdx] && !isExterior[rightIdx] && !isBarrier[rightIdx])
                {
                    isExterior[rightIdx] = true;
                    queue[tail++] = rightIdx;
                }
            }

            // 4-way BFS expansion with boundary gradient stopping
            fixed (byte* pBuffer = buffer)
            {
                while (head < tail)
                {
                    int curr = queue[head++];
                    int cx = curr % width;
                    int cy = curr / width;

                    int curR = pBuffer[curr * 4];
                    int curG = pBuffer[curr * 4 + 1];
                    int curB = pBuffer[curr * 4 + 2];

                    // Up
                    if (cy > 0)
                    {
                        int n = curr - width;
                        if (isCandidateBg[n] && !isExterior[n] && !isBarrier[n])
                        {
                            int diff = Math.Abs(pBuffer[n * 4] - curR) + Math.Abs(pBuffer[n * 4 + 1] - curG) + Math.Abs(pBuffer[n * 4 + 2] - curB);
                            if (diff < 45)
                            {
                                isExterior[n] = true;
                                queue[tail++] = n;
                            }
                        }
                    }
                    // Down
                    if (cy < height - 1)
                    {
                        int s = curr + width;
                        if (isCandidateBg[s] && !isExterior[s] && !isBarrier[s])
                        {
                            int diff = Math.Abs(pBuffer[s * 4] - curR) + Math.Abs(pBuffer[s * 4 + 1] - curG) + Math.Abs(pBuffer[s * 4 + 2] - curB);
                            if (diff < 45)
                            {
                                isExterior[s] = true;
                                queue[tail++] = s;
                            }
                        }
                    }
                    // Left
                    if (cx > 0)
                    {
                        int w = curr - 1;
                        if (isCandidateBg[w] && !isExterior[w] && !isBarrier[w])
                        {
                            int diff = Math.Abs(pBuffer[w * 4] - curR) + Math.Abs(pBuffer[w * 4 + 1] - curG) + Math.Abs(pBuffer[w * 4 + 2] - curB);
                            if (diff < 45)
                            {
                                isExterior[w] = true;
                                queue[tail++] = w;
                            }
                        }
                    }
                    // Right
                    if (cx < width - 1)
                    {
                        int e = curr + 1;
                        if (isCandidateBg[e] && !isExterior[e] && !isBarrier[e])
                        {
                            int diff = Math.Abs(pBuffer[e * 4] - curR) + Math.Abs(pBuffer[e * 4 + 1] - curG) + Math.Abs(pBuffer[e * 4 + 2] - curB);
                            if (diff < 45)
                            {
                                isExterior[e] = true;
                                queue[tail++] = e;
                            }
                        }
                    }
                }
            }
        }

        // Step 3: Compute final Alpha values
        fixed (byte* pMask = mask)
        {
            byte* mPtr = pMask;
            for (int i = 0; i < totalPixels; i++, mPtr++)
            {
                // If ContiguousOnly is on and this pixel is NOT reached from exterior, preserve it (Alpha = 255)!
                if (settings.ContiguousOnly && !isExterior[i])
                {
                    *mPtr = 255;
                    continue;
                }

                float dist = distances[i];
                if (dist <= tol)
                {
                    *mPtr = 0;
                }
                else if (dist >= tol + smooth)
                {
                    *mPtr = 255;
                }
                else
                {
                    float t = (dist - tol) / smooth;
                    float alpha = t * t * (3.0f - 2.0f * t); // Smoothstep
                    *mPtr = (byte)(alpha * 255.0f);
                }
            }
        }

        // Step 4: Apply Protected Foreground Seeds (exclude user-selected regions from removal)
        if (settings.ProtectedPoints != null && settings.ProtectedPoints.Count > 0)
        {
            double rSq = settings.ProtectRadius * settings.ProtectRadius;
            fixed (byte* pMask = mask)
            {
                foreach (var pt in settings.ProtectedPoints)
                {
                    int minX = Math.Max(0, (int)(pt.X - settings.ProtectRadius));
                    int maxX = Math.Min(width - 1, (int)(pt.X + settings.ProtectRadius));
                    int minY = Math.Max(0, (int)(pt.Y - settings.ProtectRadius));
                    int maxY = Math.Min(height - 1, (int)(pt.Y + settings.ProtectRadius));

                    for (int y = minY; y <= maxY; y++)
                    {
                        int row = y * width;
                        double dy = y - pt.Y;
                        double dySq = dy * dy;

                        for (int x = minX; x <= maxX; x++)
                        {
                            double dx = x - pt.X;
                            if (dx * dx + dySq <= rSq)
                            {
                                pMask[row + x] = 255; // Force solid foreground
                            }
                        }
                    }
                }
            }
        }

        // Step 5: Advanced Mask Post-Processing (ROI crop, Lasso, Magic Wand, Brush strokes, Erode/Dilate, Feather, Defringe)
        MaskPostProcessor.ApplyPostProcessing(mask, width, height, settings, buffer);

        return mask;
    }
}
