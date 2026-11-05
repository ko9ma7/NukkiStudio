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
            BgType = BackgroundType.Transparent
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
            BgType = BackgroundType.Transparent
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
            BgType = BackgroundType.Transparent
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
            CustomBgColor = Color.FromRgb(255, 255, 255) // White
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
            Smoothness = 0.05
        };

        byte[] mask = _engine.ComputeAlphaMask(buffer, 2, 1, settings);

        Assert.Equal(2, mask.Length);
        Assert.Equal(0, mask[0]);   // Background = 0
        Assert.Equal(255, mask[1]); // Foreground = 255
    }
}
