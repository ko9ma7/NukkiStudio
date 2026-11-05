using System;
using NukkiStudio.Models;

namespace NukkiStudio.Services;

/// <summary>
/// High-performance ChromaKey matting engine using direct memory operations.
/// Supports arbitrary key colors (Green Screen, Blue Screen, Magenta, etc.),
/// tolerance thresholding, smoothstep edge transitions, and automatic spill suppression.
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

        float keyR = settings.KeyColor.R / 255.0f;
        float keyG = settings.KeyColor.G / 255.0f;
        float keyB = settings.KeyColor.B / 255.0f;

        float tol = Math.Clamp((float)settings.Tolerance, 0.001f, 1.0f);
        float smooth = Math.Max(0.001f, (float)settings.Smoothness);
        float spill = Math.Clamp((float)settings.SpillSuppression, 0.0f, 1.0f);

        bool isGreenDominant = keyG > keyR && keyG > keyB;
        bool isBlueDominant = keyB > keyR && keyB > keyG;

        bool hasCustomBg = settings.BgType switch
        {
            BackgroundType.GreenScreen => true,
            BackgroundType.CustomColor => true,
            BackgroundType.CustomImage when backgroundBuffer != null && backgroundBuffer.Length >= width * height * 4 => true,
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

        int totalPixels = width * height;

        fixed (byte* pBuf = buffer)
        fixed (byte* pCustomBg = backgroundBuffer)
        {
            byte* ptr = pBuf;
            byte* bgPtr = pCustomBg;

            for (int i = 0; i < totalPixels; i++, ptr += 4)
            {
                byte r = ptr[0];
                byte g = ptr[1];
                byte b = ptr[2];

                float rf = r / 255.0f;
                float gf = g / 255.0f;
                float bf = b / 255.0f;

                // Color distance in normalized RGB space
                float dr = rf - keyR;
                float dg = gf - keyG;
                float db = bf - keyB;
                float dist = MathF.Sqrt(dr * dr + dg * dg + db * db) * InvSqrt3;

                // Alpha calculation with smoothstep
                float alpha;
                if (dist <= tol)
                {
                    alpha = 0.0f;
                }
                else if (dist >= tol + smooth)
                {
                    alpha = 1.0f;
                }
                else
                {
                    float t = (dist - tol) / smooth;
                    alpha = t * t * (3.0f - 2.0f * t); // Smoothstep
                }

                // Spill Suppression on foreground
                if (alpha > 0.0f && spill > 0.0f)
                {
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
                    ptr[3] = (byte)(alpha * 255.0f);
                }
                else
                {
                    // Composite with background
                    byte targetBgR = bgR;
                    byte targetBgG = bgG;
                    byte targetBgB = bgB;

                    if (settings.BgType == BackgroundType.CustomImage && bgPtr != null)
                    {
                        targetBgR = bgPtr[i * 4];
                        targetBgG = bgPtr[i * 4 + 1];
                        targetBgB = bgPtr[i * 4 + 2];
                    }

                    float invAlpha = 1.0f - alpha;
                    ptr[0] = (byte)Math.Clamp((int)(r * alpha + targetBgR * invAlpha), 0, 255);
                    ptr[1] = (byte)Math.Clamp((int)(g * alpha + targetBgG * invAlpha), 0, 255);
                    ptr[2] = (byte)Math.Clamp((int)(b * alpha + targetBgB * invAlpha), 0, 255);
                    ptr[3] = 255;
                }
            }
        }
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

        float tol = Math.Clamp((float)settings.Tolerance, 0.001f, 1.0f);
        float smooth = Math.Max(0.001f, (float)settings.Smoothness);

        fixed (byte* pBuf = buffer)
        fixed (byte* pMask = mask)
        {
            byte* ptr = pBuf;
            byte* mPtr = pMask;

            for (int i = 0; i < totalPixels; i++, ptr += 4, mPtr++)
            {
                float rf = ptr[0] / 255.0f;
                float gf = ptr[1] / 255.0f;
                float bf = ptr[2] / 255.0f;

                float dr = rf - keyR;
                float dg = gf - keyG;
                float db = bf - keyB;
                float dist = MathF.Sqrt(dr * dr + dg * dg + db * db) * InvSqrt3;

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
                    float alpha = t * t * (3.0f - 2.0f * t);
                    *mPtr = (byte)(alpha * 255.0f);
                }
            }
        }

        return mask;
    }
}
