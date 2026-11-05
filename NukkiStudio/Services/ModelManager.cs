using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NukkiStudio.Services;

public record AiModelInfo(
    string Id,
    string Name,
    string Description,
    int DefaultResolution,
    string DownloadUrl,
    long ApproximateSizeBytes
);

public class ModelManager
{
    public static string ModelsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NukkiStudio",
        "Models"
    );

    public static string BuiltInModelPath
    {
        get
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var localPath = Path.Combine(appDir, "Assets", "Models", "u2netp.onnx");
            if (File.Exists(localPath)) return localPath;

            var devPath = Path.Combine(appDir, "..", "..", "..", "Assets", "Models", "u2netp.onnx");
            if (File.Exists(devPath)) return Path.GetFullPath(devPath);

            return localPath;
        }
    }

    public static readonly List<AiModelInfo> Presets = new()
    {
        new AiModelInfo(
            Id: "u2netp",
            Name: "U2Net-P (기본 내장 · 초고속 자동 AI)",
            Description: "앱에 기본 내장되어 별도 설치 없이 즉시 동작하는 초경량 사물/인물 AI 누끼 모델.",
            DefaultResolution: 320,
            DownloadUrl: "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx",
            ApproximateSizeBytes: 4_700_000
        ),
        new AiModelInfo(
            Id: "bria_rmbg_1_4",
            Name: "BRIA RMBG-1.4 (고정밀 스튜디오 스펙)",
            Description: "복잡한 제품, 털, 머리카락까지 정밀하게 따내는 1024x1024 고해상도 AI 모델.",
            DefaultResolution: 1024,
            DownloadUrl: "https://huggingface.co/briaai/RMBG-1.4/resolve/main/onnx/model.onnx",
            ApproximateSizeBytes: 176_000_000
        ),
        new AiModelInfo(
            Id: "modnet_photographic",
            Name: "MODNet (인물 영상 전문)",
            Description: "실시간 웹캠/인물 비디오에 최적화된 포트레이트 매팅 모델 (512x512).",
            DefaultResolution: 512,
            DownloadUrl: "https://github.com/actonet/MODNet/releases/download/v0.1/modnet_photographic_portrait_matting.onnx",
            ApproximateSizeBytes: 26_000_000
        )
    };

    public ModelManager()
    {
        if (!Directory.Exists(ModelsDirectory))
        {
            Directory.CreateDirectory(ModelsDirectory);
        }
    }

    public List<string> GetInstalledModelPaths()
    {
        var list = new List<string>();
        if (Directory.Exists(ModelsDirectory))
        {
            list.AddRange(Directory.GetFiles(ModelsDirectory, "*.onnx"));
        }
        return list;
    }

    public bool IsModelInstalled(string modelFileName)
    {
        var fullPath = Path.Combine(ModelsDirectory, modelFileName);
        return File.Exists(fullPath);
    }

    public async Task<string> DownloadModelAsync(
        AiModelInfo model,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var targetFile = Path.Combine(ModelsDirectory, $"{model.Id}.onnx");
        if (File.Exists(targetFile))
            return targetFile;

        var tempFile = targetFile + ".tmp";
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await client.GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? model.ApproximateSizeBytes;
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;
            if (totalBytes > 0)
            {
                progress?.Report((double)totalRead / totalBytes);
            }
        }

        fs.Close();
        if (File.Exists(targetFile)) File.Delete(targetFile);
        File.Move(tempFile, targetFile);

        return targetFile;
    }
}
