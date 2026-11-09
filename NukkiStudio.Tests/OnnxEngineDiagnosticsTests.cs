using System;
using System.IO;
using NukkiStudio.Models;
using NukkiStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace NukkiStudio.Tests;

public class OnnxEngineDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    public OnnxEngineDiagnosticsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestOutputRange()
    {
        string modelPath = ModelManager.BuiltInModelPath;
        if (!File.Exists(modelPath)) return;

        using var engine = new OnnxMattingEngine();
        engine.LoadModel(modelPath, useGpu: false);

        int width = 320;
        int height = 320;
        byte[] rgba = new byte[width * height * 4];

        // White background with white/light-gray figurine
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                int dx = x - 160;
                int dy = y - 160;
                if (dx * dx + dy * dy < 80 * 80)
                {
                    // Light subject (240, 240, 240)
                    rgba[idx] = 240;
                    rgba[idx + 1] = 240;
                    rgba[idx + 2] = 240;
                    rgba[idx + 3] = 255;
                }
                else
                {
                    // Pure white background (255, 255, 255)
                    rgba[idx] = 255;
                    rgba[idx + 1] = 255;
                    rgba[idx + 2] = 255;
                    rgba[idx + 3] = 255;
                }
            }
        }

        var settings = new MattingSettings { Mode = MattingMode.AiNeural };
        var mask = engine.ComputeAlphaMask(rgba, width, height, settings);

        byte min = 255;
        byte max = 0;
        foreach (var b in mask)
        {
            if (b < min) min = b;
            if (b > max) max = b;
        }

        _output.WriteLine($"Mask Min: {min}, Max: {max}, Center: {mask[160 * width + 160]}, Corner: {mask[0]}");
        Assert.True(max > min);
    }
}
