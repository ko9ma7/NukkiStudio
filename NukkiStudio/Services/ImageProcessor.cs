using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NukkiStudio.Models;

namespace NukkiStudio.Services;

public class ImageProcessor
{
    /// <summary>
    /// Loads an image file and converts it to a standard 32-bit BGRA/RGBA BitmapSource.
    /// </summary>
    public static BitmapSource LoadBitmap(string filePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Pbgra32)
        {
            var formatted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            formatted.Freeze();
            return formatted;
        }

        return bitmap;
    }

    /// <summary>
    /// Extracts raw 32-bit BGRA bytes from a BitmapSource.
    /// </summary>
    public static byte[] GetPixelBytes(BitmapSource bitmap, out int width, out int height, out int stride)
    {
        width = bitmap.PixelWidth;
        height = bitmap.PixelHeight;
        stride = width * 4;
        byte[] pixels = new byte[height * stride];

        if (bitmap.Format != PixelFormats.Bgra32)
        {
            var formatted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            formatted.CopyPixels(pixels, stride, 0);
        }
        else
        {
            bitmap.CopyPixels(pixels, stride, 0);
        }

        return pixels;
    }

    /// <summary>
    /// Creates a frozen 32-bit BGRA BitmapSource from a byte buffer.
    /// </summary>
    public static BitmapSource CreateBitmapFromPixels(byte[] pixels, int width, int height)
    {
        int stride = width * 4;
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride
        );
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Swaps Red and Blue channels in-place for RGBA <-> BGRA conversion.
    /// </summary>
    public static unsafe void SwapRedBlue(byte[] buffer, int width, int height)
    {
        int total = width * height;
        fixed (byte* pBuf = buffer)
        {
            byte* ptr = pBuf;
            for (int i = 0; i < total; i++, ptr += 4)
            {
                byte tmp = ptr[0];
                ptr[0] = ptr[2];
                ptr[2] = tmp;
            }
        }
    }

    /// <summary>
    /// Saves a BitmapSource to an image file according to the requested export format.
    /// </summary>
    public static void SaveImage(BitmapSource bitmap, string outputPath, ImageExportFormat format, int jpegQuality = 95)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        BitmapEncoder encoder = format switch
        {
            ImageExportFormat.PngTransparent => new PngBitmapEncoder(),
            ImageExportFormat.AlphaMaskPng => new PngBitmapEncoder(),
            ImageExportFormat.JpgComposite => new JpegBitmapEncoder { QualityLevel = jpegQuality },
            _ => new PngBitmapEncoder()
        };

        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
    }

    /// <summary>
    /// Creates a thumbnail of specified maximum dimensions while preserving aspect ratio.
    /// </summary>
    public static BitmapSource CreateThumbnail(BitmapSource source, int maxWidth = 256, int maxHeight = 256)
    {
        double scale = Math.Min((double)maxWidth / source.PixelWidth, (double)maxHeight / source.PixelHeight);
        if (scale >= 1.0)
            return source;

        int newWidth = Math.Max(1, (int)(source.PixelWidth * scale));
        int newHeight = Math.Max(1, (int)(source.PixelHeight * scale));

        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    /// <summary>
    /// Loads and resizes a background image to exact width and height.
    /// </summary>
    public static byte[]? LoadResizedBackgroundBuffer(string? imagePath, int targetWidth, int targetHeight)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        var bmp = LoadBitmap(imagePath);
        if (bmp.PixelWidth == targetWidth && bmp.PixelHeight == targetHeight)
        {
            return GetPixelBytes(bmp, out _, out _, out _);
        }

        double scaleX = (double)targetWidth / bmp.PixelWidth;
        double scaleY = (double)targetHeight / bmp.PixelHeight;

        var transformed = new TransformedBitmap(bmp, new ScaleTransform(scaleX, scaleY));
        transformed.Freeze();

        return GetPixelBytes(transformed, out _, out _, out _);
    }

    /// <summary>
    /// Creates a smooth vertical studio gradient background.
    /// </summary>
    public static byte[] CreateGradientBackground(int width, int height, Color topColor, Color bottomColor)
    {
        byte[] buf = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            float t = (float)y / Math.Max(1, height - 1);
            byte r = (byte)Math.Clamp(topColor.R + (bottomColor.R - topColor.R) * t, 0, 255);
            byte g = (byte)Math.Clamp(topColor.G + (bottomColor.G - topColor.G) * t, 0, 255);
            byte b = (byte)Math.Clamp(topColor.B + (bottomColor.B - topColor.B) * t, 0, 255);
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int idx = row + x * 4;
                buf[idx] = r;
                buf[idx + 1] = g;
                buf[idx + 2] = b;
                buf[idx + 3] = 255;
            }
        }
        return buf;
    }

    /// <summary>
    /// Creates a blurred copy of an RGBA image buffer using a fast two-pass box blur.
    /// </summary>
    public static unsafe byte[] CreateBlurredBackground(byte[] src, int width, int height, int radius)
    {
        radius = Math.Clamp(radius, 1, 40);
        byte[] dst = new byte[src.Length];
        byte[] temp = new byte[src.Length];

        // Horizontal pass
        fixed (byte* pSrc = src)
        fixed (byte* pTemp = temp)
        fixed (byte* pDst = dst)
        {
            for (int y = 0; y < height; y++)
            {
                int row = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int rSum = 0, gSum = 0, bSum = 0, aSum = 0, count = 0;
                    int xStart = Math.Max(0, x - radius);
                    int xEnd = Math.Min(width - 1, x + radius);

                    for (int kx = xStart; kx <= xEnd; kx++)
                    {
                        int idx = row + kx * 4;
                        rSum += pSrc[idx];
                        gSum += pSrc[idx + 1];
                        bSum += pSrc[idx + 2];
                        aSum += pSrc[idx + 3];
                        count++;
                    }

                    int outIdx = row + x * 4;
                    pTemp[outIdx] = (byte)(rSum / count);
                    pTemp[outIdx + 1] = (byte)(gSum / count);
                    pTemp[outIdx + 2] = (byte)(bSum / count);
                    pTemp[outIdx + 3] = (byte)(aSum / count);
                }
            }

            // Vertical pass
            for (int x = 0; x < width; x++)
            {
                int col = x * 4;
                for (int y = 0; y < height; y++)
                {
                    int rSum = 0, gSum = 0, bSum = 0, aSum = 0, count = 0;
                    int yStart = Math.Max(0, y - radius);
                    int yEnd = Math.Min(height - 1, y + radius);

                    for (int ky = yStart; ky <= yEnd; ky++)
                    {
                        int idx = ky * width * 4 + col;
                        rSum += pTemp[idx];
                        gSum += pTemp[idx + 1];
                        bSum += pTemp[idx + 2];
                        aSum += pTemp[idx + 3];
                        count++;
                    }

                    int outIdx = y * width * 4 + col;
                    pDst[outIdx] = (byte)(rSum / count);
                    pDst[outIdx + 1] = (byte)(gSum / count);
                    pDst[outIdx + 2] = (byte)(bSum / count);
                    pDst[outIdx + 3] = (byte)(aSum / count);
                }
            }
        }

        return dst;
    }

    /// <summary>
    /// Applies Brightness, Contrast, and Saturation color grading to pixels with Alpha > 0.
    /// </summary>
    public static unsafe void ApplyColorGrading(byte[] buffer, int width, int height, double brightness, double contrast, double saturation)
    {
        if (Math.Abs(brightness) < 0.01 && Math.Abs(contrast - 1.0) < 0.01 && Math.Abs(saturation - 1.0) < 0.01)
            return;

        int total = width * height;
        float bAdd = (float)brightness;
        float cMult = (float)contrast;
        float sMult = (float)saturation;

        fixed (byte* pBuf = buffer)
        {
            byte* ptr = pBuf;
            for (int i = 0; i < total; i++, ptr += 4)
            {
                byte a = ptr[3];
                if (a == 0) continue;

                float r = ptr[0];
                float g = ptr[1];
                float b = ptr[2];

                // 1. Brightness (-100 ~ +100)
                r += bAdd;
                g += bAdd;
                b += bAdd;

                // 2. Contrast around mid-gray 128
                r = ((r - 128f) * cMult) + 128f;
                g = ((g - 128f) * cMult) + 128f;
                b = ((b - 128f) * cMult) + 128f;

                // 3. Saturation
                if (Math.Abs(sMult - 1.0f) >= 0.01f)
                {
                    float gray = 0.299f * r + 0.587f * g + 0.114f * b;
                    r = gray + (r - gray) * sMult;
                    g = gray + (g - gray) * sMult;
                    b = gray + (b - gray) * sMult;
                }

                ptr[0] = (byte)Math.Clamp(r, 0f, 255f);
                ptr[1] = (byte)Math.Clamp(g, 0f, 255f);
                ptr[2] = (byte)Math.Clamp(b, 0f, 255f);
            }
        }
    }

    /// <summary>
    /// Generates a solid colored sticker stroke/outline along the cutout perimeter.
    /// </summary>
    public static unsafe void ApplyStickerStroke(byte[] buffer, int width, int height, int thickness, Color strokeColor)
    {
        if (thickness <= 0) return;
        thickness = Math.Clamp(thickness, 1, 20);

        byte[] strokeMask = new byte[width * height];
        fixed (byte* pBuf = buffer)
        fixed (byte* pMask = strokeMask)
        {
            // 1. Identify solid subject pixels
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int bufRow = row * 4;
                for (int x = 0; x < width; x++)
                {
                    if (pBuf[bufRow + x * 4 + 3] > 64)
                    {
                        pMask[row + x] = 255;
                    }
                }
            }

            // 2. Dilate mask by thickness
            byte[] dilated = (byte[])strokeMask.Clone();
            fixed (byte* pDil = dilated)
            {
                for (int y = 0; y < height; y++)
                {
                    int minY = Math.Max(0, y - thickness);
                    int maxY = Math.Min(height - 1, y + thickness);

                    for (int x = 0; x < width; x++)
                    {
                        if (pMask[y * width + x] == 255) continue;

                        int minX = Math.Max(0, x - thickness);
                        int maxX = Math.Min(width - 1, x + thickness);

                        bool found = false;
                        for (int ny = minY; ny <= maxY && !found; ny++)
                        {
                            int dy = ny - y;
                            int nRow = ny * width;
                            for (int nx = minX; nx <= maxX; nx++)
                            {
                                int dx = nx - x;
                                if (dx * dx + dy * dy <= thickness * thickness && pMask[nRow + nx] == 255)
                                {
                                    found = true;
                                    break;
                                }
                            }
                        }

                        if (found)
                        {
                            pDil[y * width + x] = 255;
                        }
                    }
                }

                // 3. Composite stroke onto buffer
                byte sR = strokeColor.R;
                byte sG = strokeColor.G;
                byte sB = strokeColor.B;
                byte sA = strokeColor.A;

                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    int bufRow = row * 4;
                    for (int x = 0; x < width; x++)
                    {
                        byte origA = pBuf[bufRow + x * 4 + 3];
                        if (origA < 200 && pDil[row + x] == 255)
                        {
                            float strokeBlend = (255 - origA) / 255.0f;
                            pBuf[bufRow + x * 4] = (byte)(sR * strokeBlend + pBuf[bufRow + x * 4] * (1 - strokeBlend));
                            pBuf[bufRow + x * 4 + 1] = (byte)(sG * strokeBlend + pBuf[bufRow + x * 4 + 1] * (1 - strokeBlend));
                            pBuf[bufRow + x * 4 + 2] = (byte)(sB * strokeBlend + pBuf[bufRow + x * 4 + 2] * (1 - strokeBlend));
                            pBuf[bufRow + x * 4 + 3] = Math.Max(origA, sA);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies comprehensive subject post-matting effects: Color Grading, Sticker Stroke, etc.
    /// </summary>
    public static void ApplySubjectEffects(byte[] buffer, int width, int height, MattingSettings settings)
    {
        // 1. Color grading
        ApplyColorGrading(buffer, width, height, settings.Brightness, settings.Contrast, settings.Saturation);

        // 2. Sticker Stroke
        if (settings.EnableStroke && settings.StrokeThickness > 0)
        {
            ApplyStickerStroke(buffer, width, height, settings.StrokeThickness, settings.StrokeColor);
        }
    }
}
