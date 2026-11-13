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

    [Fact]
    public void EasyMode_DefaultsToTrue_FullCutoutPreviewWithoutSplitDivider()
    {
        var vm = new MainViewModel();

        // 1. Zero-configuration Easy Mode must be active by default
        Assert.True(vm.IsEasyMode);

        // 2. Default to 100% full cutout preview (no 50/50 split line cutting subject in half)
        Assert.False(vm.ShowSplitCompare);
        Assert.Equal(0.0, vm.SplitPosition);

        // 3. Toggling split compare should enable 50% split view
        vm.ToggleSplitCompare();
        Assert.True(vm.ShowSplitCompare);
        Assert.Equal(50.0, vm.SplitPosition);

        // 4. Toggling off should return to 100% full cutout
        vm.ToggleSplitCompare();
        Assert.False(vm.ShowSplitCompare);
        Assert.Equal(0.0, vm.SplitPosition);
    }

    [Fact]
    public void EasyMode_OneClickBackgroundButtons_UpdateSettings()
    {
        var vm = new MainViewModel();

        vm.SetEasyBgGreenCommand.Execute(null);
        Assert.Equal(BackgroundType.GreenScreen, vm.SelectedBgType);

        vm.SetEasyBgWhiteCommand.Execute(null);
        Assert.Equal(BackgroundType.CustomColor, vm.SelectedBgType);
        Assert.Equal(Colors.White, vm.CustomBgColor);

        vm.SetEasyBgTransparentCommand.Execute(null);
        Assert.Equal(BackgroundType.Transparent, vm.SelectedBgType);
    }

    [Fact]
    public void EasyMode_TouchUpWands_ActivateCorrectTools()
    {
        var vm = new MainViewModel();

        vm.ActivateEasyEraseWandCommand.Execute(null);
        Assert.Equal(MattingToolMode.MagicWandErase, vm.ActiveTool);

        vm.ActivateEasyKeepWandCommand.Execute(null);
        Assert.Equal(MattingToolMode.MagicWandKeep, vm.ActiveTool);

        vm.ResetEasyTouchUpsCommand.Execute(null);
        Assert.Empty(vm.MagicWandPoints);
        Assert.Empty(vm.LassoPolygons);
        Assert.Empty(vm.BrushStrokes);
    }

    [Fact]
    public void EasyMode_BlurAndGradientButtons_UpdateSettings()
    {
        var vm = new MainViewModel();

        vm.SetEasyBgBlurCommand.Execute(null);
        Assert.Equal(BackgroundType.BlurOriginal, vm.SelectedBgType);
        Assert.True(vm.EnableBgBlur);

        vm.SetEasyBgGradientCommand.Execute(null);
        Assert.Equal(BackgroundType.Gradient, vm.SelectedBgType);
    }

    [Fact]
    public void VisualEffects_ToggleStrokeAndShadow()
    {
        var vm = new MainViewModel();

        Assert.False(vm.EnableStroke);
        vm.ToggleStrokeCommand.Execute(null);
        Assert.True(vm.EnableStroke);

        Assert.False(vm.EnableShadow);
        vm.ToggleShadowCommand.Execute(null);
        Assert.True(vm.EnableShadow);
    }

    [Fact]
    public void VideoTimeline_TrimControls_WorkAsExpected()
    {
        var vm = new MainViewModel
        {
            IsVideoLoaded = true,
            VideoDuration = 10.0,
            VideoCurrentTime = 3.5
        };

        vm.SetTrimStartCommand.Execute(null);
        Assert.Equal(3.5, vm.VideoTrimStart);

        vm.VideoCurrentTime = 8.0;
        vm.SetTrimEndCommand.Execute(null);
        Assert.Equal(8.0, vm.VideoTrimEnd);

        vm.ResetTrimCommand.Execute(null);
        Assert.Equal(0.0, vm.VideoTrimStart);
        Assert.Equal(10.0, vm.VideoTrimEnd);
    }

    [Fact]
    public void ImageProcessor_GradientAndBlurAndStroke_ExecuteSafely()
    {
        int w = 10;
        int h = 10;
        var grad = ImageProcessor.CreateGradientBackground(w, h, Colors.Black, Colors.White);
        Assert.Equal(w * h * 4, grad.Length);

        byte[] sampleRgba = new byte[w * h * 4];
        for (int i = 0; i < sampleRgba.Length; i += 4)
        {
            sampleRgba[i] = 100;
            sampleRgba[i + 1] = 150;
            sampleRgba[i + 2] = 200;
            sampleRgba[i + 3] = 255;
        }

        var blurred = ImageProcessor.CreateBlurredBackground(sampleRgba, w, h, 2);
        Assert.Equal(sampleRgba.Length, blurred.Length);

        // Run stroke without exceptions
        ImageProcessor.ApplyStickerStroke(sampleRgba, w, h, 2, Colors.White);
        Assert.NotNull(sampleRgba);
    }

    [Fact]
    public void FFmpegService_SanitizesAndLocatesBinariesSafely()
    {
        var service = new FFmpegService();
        // Should not throw and should detect installed FFmpeg on this system
        Assert.NotNull(service.FFmpegPath);
        Assert.False(service.FFmpegPath.Contains(".mise-bins", System.StringComparison.OrdinalIgnoreCase));
    }
}
