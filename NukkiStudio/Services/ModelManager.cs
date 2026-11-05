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

    public static readonly List<AiModelInfo> Presets = new()
    {
        new AiModelInfo(
            Id: "modnet_photographic",
            Name: "MODNet (Real-time Human/Portrait Matting)",
            Description: "Fast portrait video matting optimized for people and webcams (512x512).",
            DefaultResolution: 512,
            DownloadUrl: "https://github.com/actonet/MODNet/releases/download/v0.1/modnet_photographic_portrait_matting.onnx",
            ApproximateSizeBytes: 26_000_000
        ),
        new AiModelInfo(
            Id: "u2netp",
            Name: "U2Net-P (Lightweight General Matting)",
            Description: "Super lightweight general salient object & background removal (320x320, ~4MB).",
            DefaultResolution: 320,
            DownloadUrl: "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx",
            ApproximateSizeBytes: 4_700_000
        ),
        new AiModelInfo(
            Id: "bria_rmbg_1_4",
            Name: "BRIA RMBG-1.4 (High Precision General)",
            Description: "State-of-the-art general background removal for products, animals, and people (1024x1024).",
            DefaultResolution: 1024,
            DownloadUrl: "https://huggingface.co/briaai/RMBG-1.4/resolve/main/onnx/model.onnx",
            ApproximateSizeBytes: 176_000_000
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
