using NukkiStudio.Models;
using NukkiStudio.Services;
using Xunit;

namespace NukkiStudio.Tests;

public class FFmpegArgsTests
{
    [Fact]
    public void BuildEncoderArguments_ProRes4444_ContainsCorrectCodecAndProfile()
    {
        string input = "test_input.mp4";
        string output = "output.mov";

        string args = FFmpegService.BuildEncoderArguments(
            input, output, 1920, 1080, 29.97,
            VideoExportFormat.ProRes4444_Mov,
            preserveAudio: true,
            hasAudioSource: true
        );

        Assert.Contains("-c:v prores_ks", args);
        Assert.Contains("-profile:v 4", args);
        Assert.Contains("-pix_fmt yuva444p10le", args);
        Assert.Contains("-c:a copy", args);
        Assert.Contains("output.mov", args);
    }

    [Fact]
    public void BuildEncoderArguments_WebM_VP9_ContainsTransparentPixelFormat()
    {
        string input = "test_input.mp4";
        string output = "output.webm";

        string args = FFmpegService.BuildEncoderArguments(
            input, output, 1280, 720, 30.0,
            VideoExportFormat.WebM_VP9,
            preserveAudio: false,
            hasAudioSource: true
        );

        Assert.Contains("-c:v libvpx-vp9", args);
        Assert.Contains("-pix_fmt yuva420p", args);
        Assert.DoesNotContain("-c:a copy", args); // Audio not preserved
        Assert.Contains("output.webm", args);
    }

    [Fact]
    public void BuildEncoderArguments_GreenScreen_GeneratesH264Mp4()
    {
        string input = "test_input.mp4";
        string output = "output.mp4";

        string args = FFmpegService.BuildEncoderArguments(
            input, output, 1920, 1080, 60.0,
            VideoExportFormat.GreenScreen_Mp4,
            preserveAudio: true,
            hasAudioSource: true
        );

        Assert.Contains("-c:v libx264", args);
        Assert.Contains("-pix_fmt yuv420p", args);
        Assert.Contains("output.mp4", args);
    }

    [Fact]
    public void DetectFFmpeg_FindsValidWorkingFFmpegWithoutError()
    {
        var service = new FFmpegService();
        Assert.True(service.IsFFmpegInstalled);
        Assert.EndsWith("ffmpeg.exe", service.FFmpegPath, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".mise-bins", service.FFmpegPath, System.StringComparison.OrdinalIgnoreCase);
    }
}
