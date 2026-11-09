using System;
using System.IO;
using NukkiStudio.Models;
using NukkiStudio.Services;
using Xunit;

namespace NukkiStudio.Tests;

public class OnnxEngineTests
{
    [Fact]
    public void OnnxMattingEngine_ComputesAlphaMaskFromBuiltInModel()
    {
        string modelPath = ModelManager.BuiltInModelPath;
        if (!File.Exists(modelPath)) return;

        using var engine = new OnnxMattingEngine();
        engine.LoadModel(modelPath, useGpu: false);
        Assert.True(engine.IsAvailable);

        int width = 100;
        int height = 100;
        byte[] rgba = new byte[width * height * 4];

        // Draw a red circle in the center on white background
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                int dx = x - 50;
                int dy = y - 50;
                if (dx * dx + dy * dy < 25 * 25)
                {
                    rgba[idx] = 255;   // R
                    rgba[idx + 1] = 0; // G
                    rgba[idx + 2] = 0; // B
                    rgba[idx + 3] = 255;
                }
                else
                {
                    rgba[idx] = 255;
                    rgba[idx + 1] = 255;
                    rgba[idx + 2] = 255;
                    rgba[idx + 3] = 255;
                }
            }
        }

        var settings = new MattingSettings { Mode = MattingMode.AiNeural };
        var mask = engine.ComputeAlphaMask(rgba, width, height, settings);

        Assert.Equal(width * height, mask.Length);

        // Center pixel should have higher alpha than corner pixel
        byte centerAlpha = mask[50 * width + 50];
        byte cornerAlpha = mask[0];

        // Output diagnostics
        Assert.True(centerAlpha > cornerAlpha, $"Center {centerAlpha} should be greater than corner {cornerAlpha}");
    }
}
