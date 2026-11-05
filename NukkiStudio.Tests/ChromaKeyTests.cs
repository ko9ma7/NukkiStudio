using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using NukkiStudio.Models;
using NukkiStudio.Services;
using Xunit;

namespace NukkiStudio.Tests;

public class ChromaKeyTests
{
    private readonly ChromaKeyMattingEngine _engine = new();

    [Fact]
    public void PureKeyColor_ShouldBecomeFullyTransparent()
    {
        // 1x1 RGBA image with pure green (0, 255, 0, 255)
        byte[] buffer = new byte[] { 0, 255, 0, 255 };
        var settings = new MattingSettings
        {
            KeyColor = Color.FromRgb(0, 255, 0),
            Tolerance = 0.20,
            Smoothness = 0.10,
            BgType = BackgroundType.Transparent,
            ContiguousOnly = false
        };

        _engine.ProcessRgbaBuffer(buffer, 1, 1, settings);

        // Alpha channel (index 3) should be 0
        Assert.Equal(0, buffer[3]);
    }

    [Fact]
    public void DistinctForeground_ShouldRemainFullyOpaque()
    {
        // 1x1 RGBA image with pure red (255, 0, 0, 255)
        byte[] buffer = new byte[] { 255, 0, 0, 255 };
        var settings = new MattingSettings
        {
            KeyColor = Color.FromRgb(0, 255, 0),
            Tolerance = 0.25,
            Smoothness = 0.10,
            BgType = BackgroundType.Transparent,
            ContiguousOnly = false
        };

        _engine.ProcessRgbaBuffer(buffer, 1, 1, settings);

        // Alpha channel (index 3) should remain 255
        Assert.Equal(255, buffer[3]);
        Assert.Equal(255, buffer[0]); // Red remains untouched
    }

    [Fact]
    public void SpillSuppression_ReducesExcessiveGreenOnForegroundEdges()
    {
        // Foreground hair edge that has green bounce light: R=100, G=160, B=100
        byte[] buffer = new byte[] { 100, 160, 100, 255 };
        var settings = new MattingSettings
        {
            KeyColor = Color.FromRgb(0, 255, 0),
            Tolerance = 0.15,
            Smoothness = 0.25,
            SpillSuppression = 0.80,
            BgType = BackgroundType.Transparent,
            ContiguousOnly = false
        };

        _engine.ProcessRgbaBuffer(buffer, 1, 1, settings);

        // Green value should be significantly reduced from 160
        Assert.True(buffer[1] < 160, $"Expected green < 160, got {buffer[1]}");
    }

    [Fact]
    public void BackgroundComposite_ReplacesKeyedPixelsWithSolidColor()
    {
        // 1x1 RGBA image with pure green
        byte[] buffer = new byte[] { 0, 255, 0, 255 };
        var settings = new MattingSettings
        {
            KeyColor = Color.FromRgb(0, 255, 0),
            Tolerance = 0.20,
            Smoothness = 0.05,
            BgType = BackgroundType.CustomColor,
            CustomBgColor = Color.FromRgb(255, 255, 255), // White
            ContiguousOnly = false
        };

        _engine.ProcessRgbaBuffer(buffer, 1, 1, settings);

        // Keyed out pixel should be replaced with solid white and 255 alpha
        Assert.Equal(255, buffer[0]); // R
        Assert.Equal(255, buffer[1]); // G
        Assert.Equal(255, buffer[2]); // B
        Assert.Equal(255, buffer[3]); // A
    }

    [Fact]
    public void ComputeAlphaMask_ReturnsCorrectMaskArray()
    {
        // 2x1 buffer: [Green, Red]
        byte[] buffer = new byte[] {
            0, 255, 0, 255,   // Pixel 0: Green
            255, 0, 0, 255    // Pixel 1: Red
        };

        var settings = new MattingSettings
        {
            KeyColor = Color.FromRgb(0, 255, 0),
            Tolerance = 0.20,
            Smoothness = 0.05,
            ContiguousOnly = false
        };

        byte[] mask = _engine.ComputeAlphaMask(buffer, 2, 1, settings);

        Assert.Equal(2, mask.Length);
        Assert.Equal(0, mask[0]);   // Background = 0
        Assert.Equal(255, mask[1]); // Foreground = 255
    }

    [Fact]
    public void ContiguousOnly_ProtectsInternalIslandsFromBeingKeyedOut()
    {
        // 5x5 image where border is White (255, 255, 255),
        // middle ring is Yellow (255, 255, 0),
        // and center pixel (2, 2) is White (255, 255, 255).
        int width = 5;
        int height = 5;
        byte[] buffer = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                if (x == 0 || x == 4 || y == 0 || y == 4)
                {
                    // Exterior border: Pure White
                    buffer[idx] = 255; buffer[idx + 1] = 255; buffer[idx + 2] = 255; buffer[idx + 3] = 255;
                }
                else if (x == 2 && y == 2)
                {
                    // Enclosed island: Pure White (like the white duck head inside yellow duck hood)
                    buffer[idx] = 255; buffer[idx + 1] = 255; buffer[idx + 2] = 255; buffer[idx + 3] = 255;
                }
                else
                {
                    // Surrounding barrier: Pure Yellow (duck hood)
                    buffer[idx] = 255; buffer[idx + 1] = 255; buffer[idx + 2] = 0; buffer[idx + 3] = 255;
                }
            }
        }

        var settings = new MattingSettings
        {
            KeyColor = Color.FromRgb(255, 255, 255), // Key out white background
            Tolerance = 0.15,
            Smoothness = 0.05,
            ContiguousOnly = true // Only remove white connected to exterior borders
        };

        byte[] mask = _engine.ComputeAlphaMask(buffer, width, height, settings);

        // Exterior border pixel (0, 0) should be transparent (alpha = 0)
        Assert.Equal(0, mask[0]);

        // Middle barrier yellow pixel (1, 1) should be solid (alpha = 255)
        Assert.Equal(255, mask[1 * width + 1]);

        // Enclosed center white pixel (2, 2) MUST be preserved as solid (alpha = 255)!
        Assert.Equal(255, mask[2 * width + 2]);
    }

    [Fact]
    public void ProtectedPoints_PreservesSpecifiedRegionRegardlessOfColor()
    {
        // 3x3 image of pure green (all pixels matching key color)
        int width = 3;
        int height = 3;
        byte[] buffer = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            buffer[i * 4 + 1] = 255; // G = 255
            buffer[i * 4 + 3] = 255; // A = 255
        }

        var settings = new MattingSettings
        {
            KeyColor = Color.FromRgb(0, 255, 0),
            Tolerance = 0.20,
            Smoothness = 0.05,
            ContiguousOnly = false,
            ProtectedPoints = new List<Point> { new Point(1, 1) }, // Center protected
            ProtectRadius = 1.0
        };

        byte[] mask = _engine.ComputeAlphaMask(buffer, width, height, settings);

        // Corner (0, 0) is keyed out
        Assert.Equal(0, mask[0]);

        // Center (1, 1) is protected and forced to alpha 255
        Assert.Equal(255, mask[1 * width + 1]);
    }
}
