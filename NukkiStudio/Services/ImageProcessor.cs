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
}
