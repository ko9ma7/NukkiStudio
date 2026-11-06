using System.Windows;
using System.Windows.Media;
using NukkiStudio.Models;
using NukkiStudio.Services;
using Xunit;

namespace NukkiStudio.Tests;

public class MaskPostProcessorTests
{
    [Fact]
    public void ApplyPostProcessing_RoiBox_CropsOutsideToZero()
    {
        int width = 100;
        int height = 100;
        byte[] mask = new byte[width * height];
        for (int i = 0; i < mask.Length; i++) mask[i] = 255;

        var settings = new MattingSettings
        {
            EnableRoi = true,
            RoiRect = new Rect(20, 20, 60, 60)
        };

        MaskPostProcessor.ApplyPostProcessing(mask, width, height, settings);

        // Inside ROI (e.g. 50, 50) must remain 255
        Assert.Equal(255, mask[50 * width + 50]);

        // Outside ROI (e.g. 5, 5 or 95, 95) must be 0
        Assert.Equal(0, mask[5 * width + 5]);
        Assert.Equal(0, mask[95 * width + 95]);
    }

    [Fact]
    public void ApplyPostProcessing_IncludeBrush_RestoresAlphaTo255()
    {
        int width = 50;
        int height = 50;
        byte[] mask = new byte[width * height]; // All transparent 0

        var settings = new MattingSettings
        {
            BrushStrokes = new List<BrushStrokePoint>
            {
                new BrushStrokePoint(25, 25, 10, IsInclude: true)
            }
        };

        MaskPostProcessor.ApplyPostProcessing(mask, width, height, settings);

        // Point inside brush radius must be forced to 255
        Assert.Equal(255, mask[25 * width + 25]);
        // Point far outside brush radius must remain 0
        Assert.Equal(0, mask[0]);
    }

    [Fact]
    public void ApplyPostProcessing_ExcludeBrush_ForcesAlphaToZero()
    {
        int width = 50;
        int height = 50;
        byte[] mask = new byte[width * height];
        Array.Fill(mask, (byte)255); // All solid 255

        var settings = new MattingSettings
        {
            BrushStrokes = new List<BrushStrokePoint>
            {
                new BrushStrokePoint(25, 25, 10, IsInclude: false)
            }
        };

        MaskPostProcessor.ApplyPostProcessing(mask, width, height, settings);

        // Point inside brush radius must be forced to 0
        Assert.Equal(0, mask[25 * width + 25]);
        // Point far outside brush radius must remain 255
        Assert.Equal(255, mask[0]);
    }

    [Fact]
    public void ApplyPostProcessing_ErodeChoke_ShrinksMaskBorders()
    {
        int width = 30;
        int height = 30;
        byte[] mask = new byte[width * height];

        // Create a solid square of 255 from (10,10) to (20,20)
        for (int y = 10; y <= 20; y++)
        {
            for (int x = 10; x <= 20; x++)
            {
                mask[y * width + x] = 255;
            }
        }

        var settings = new MattingSettings
        {
            MaskErodeDilate = -2 // Erode 2px
        };

        MaskPostProcessor.ApplyPostProcessing(mask, width, height, settings);

        // Outer border (10, 10) must now be eroded to 0
        Assert.Equal(0, mask[10 * width + 10]);
        // Deep interior (15, 15) must remain 255
        Assert.Equal(255, mask[15 * width + 15]);
    }

    [Fact]
    public void ApplyPostProcessing_Threshold_ClampsFaintArtifacts()
    {
        int width = 10;
        int height = 10;
        byte[] mask = new byte[width * height];
        mask[0] = 30; // Faint artifact (30 / 255 ~= 0.117)
        mask[1] = 200; // Strong foreground

        var settings = new MattingSettings
        {
            MaskThreshold = 0.20 // Cutoff anything below 20% alpha
        };

        MaskPostProcessor.ApplyPostProcessing(mask, width, height, settings);

        Assert.Equal(0, mask[0]); // Clamped to zero
        Assert.True(mask[1] > 0); // Preserved
    }

    [Fact]
    public void ChromaKey_WithSecondaryKeyColor_RemovesBothColors()
    {
        var engine = new ChromaKeyMattingEngine();
        int width = 2;
        int height = 1;

        // Pixel 0: White studio background (255, 255, 255)
        // Pixel 1: Grey ground shadow (180, 180, 180)
        byte[] rgba = new byte[]
        {
            255, 255, 255, 255,
            180, 180, 180, 255
        };

        var settings = new MattingSettings
        {
            KeyColor = Colors.White,
            Tolerance = 0.15,
            ContiguousOnly = false,
            EnableSecondaryKey = true,
            SecondaryKeyColor = Color.FromRgb(180, 180, 180),
            SecondaryTolerance = 0.15
        };

        var mask = engine.ComputeAlphaMask(rgba, width, height, settings);

        // Both primary white and secondary grey shadow should be removed (alpha == 0)
        Assert.Equal(0, mask[0]);
        Assert.Equal(0, mask[1]);
    }
}
