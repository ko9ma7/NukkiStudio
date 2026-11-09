using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using NukkiStudio.Models;
using NukkiStudio.Services;
using NukkiStudio.ViewModels;
using Xunit;

namespace NukkiStudio.Tests;

public class HistoryAndPresetTests
{
    [Fact]
    public void MattingHistoryState_DeepClonesCollections_IndependentFromMutations()
    {
        var wandPoints = new List<MagicWandPoint> { new(10, 20, 0.15, true) };
        var lassoPolys = new List<LassoPolygon>
        {
            new(new List<Point> { new(0, 0), new(10, 0), new(10, 10) }, true)
        };
        var brushStrokes = new List<BrushStrokePoint> { new(5, 5, 10, true) };

        var state = new MattingHistoryState(
            brushStrokes: brushStrokes,
            magicWandPoints: wandPoints,
            lassoPolygons: lassoPolys,
            roiRect: null,
            enableRoi: false,
            description: "Initial"
        );

        // Mutate original source lists
        wandPoints.Add(new(30, 40, 0.15, false));
        brushStrokes.Clear();

        // State snapshot should remain completely unmodified
        Assert.Single(state.MagicWandPoints);
        Assert.Single(state.BrushStrokes);
        Assert.Single(state.LassoPolygons);
    }

    [Fact]
    public void Presets_ConfigureOptimalParameters_ForVariousScenarios()
    {
        // 1. Studio Figurine preset (e.g. white hat / clothes on white background)
        var settingsStudio = new MattingSettings { QuickPreset = QuickPresetMode.StudioFigurine };
        // Figurine needs barrier protection (ContiguousOnly), defringe to kill white halo, and subtle edge choke
        Assert.True(settingsStudio.ContiguousOnly);

        // 2. Green Screen preset
        var settingsGreen = new MattingSettings { QuickPreset = QuickPresetMode.GreenScreen };
        Assert.True(settingsGreen.SpillSuppression >= 0.5);
    }

    [Fact]
    public void WebCloudMattingEngine_ThrowsHelpfulError_WhenApiKeyMissing()
    {
        var engine = new WebCloudMattingEngine();
        var settings = new MattingSettings
        {
            Mode = MattingMode.WebCloud,
            WebProvider = WebMattingProvider.RemoveBg,
            WebApiKey = "" // Empty key
        };

        byte[] buffer = new byte[16]; // 2x2
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
        {
            engine.ComputeAlphaMask(buffer, 2, 2, settings);
        });

        Assert.Contains("Remove.bg API 키가 등록되지 않았습니다", ex.Message);
    }

    [Fact]
    public void GetEngineForMode_SelectsAppropriateEngine()
    {
        var vm = new MainViewModel();

        var aiEngine = vm.GetEngineForMode(MattingMode.AiNeural);
        Assert.NotNull(aiEngine);
        Assert.Contains("AI", aiEngine.Name);

        var webEngine = vm.GetEngineForMode(MattingMode.WebCloud);
        Assert.NotNull(webEngine);
        Assert.Contains("Web Cloud", webEngine.Name);

        var chromaEngine = vm.GetEngineForMode(MattingMode.ChromaKey);
        Assert.NotNull(chromaEngine);
        Assert.Contains("Chroma", chromaEngine.Name);
    }
}
