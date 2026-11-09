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
}
