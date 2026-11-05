using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using NukkiStudio.Models;

namespace NukkiStudio.Services;

public class FFmpegService
{
    public string FFmpegPath { get; private set; } = "ffmpeg.exe";
    public string FFprobePath { get; private set; } = "ffprobe.exe";
    public bool IsFFmpegInstalled { get; private set; }

    public FFmpegService()
    {
        DetectFFmpeg();
    }

    public void SetCustomPaths(string ffmpegPath, string? ffprobePath = null)
    {
        if (File.Exists(ffmpegPath))
        {
            FFmpegPath = ffmpegPath;
            FFprobePath = ffprobePath ?? Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe");
            IsFFmpegInstalled = true;
        }
    }

    public void DetectFFmpeg()
    {
        // 1. Check if on system PATH
        if (CheckBinaryAvailable("ffmpeg.exe"))
        {
            FFmpegPath = "ffmpeg.exe";
            FFprobePath = "ffprobe.exe";
            IsFFmpegInstalled = true;
            return;
        }

        // 2. Check mise / local installations
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var miseInstalls = Path.Combine(userProfile, "AppData", "Local", "mise", "installs", "ffmpeg");
        if (Directory.Exists(miseInstalls))
        {
            var bins = Directory.GetFiles(miseInstalls, "ffmpeg.exe", SearchOption.AllDirectories);
            if (bins.Length > 0)
            {
                FFmpegPath = bins[0];
                var probeBins = Directory.GetFiles(miseInstalls, "ffprobe.exe", SearchOption.AllDirectories);
                FFprobePath = probeBins.Length > 0 ? probeBins[0] : "ffprobe.exe";
                IsFFmpegInstalled = true;
                return;
            }
        }

        // 3. Check App directory / local tools
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var localFFmpeg = Path.Combine(appDir, "ffmpeg.exe");
        if (File.Exists(localFFmpeg))
        {
            FFmpegPath = localFFmpeg;
            FFprobePath = Path.Combine(appDir, "ffprobe.exe");
            IsFFmpegInstalled = true;
            return;
        }

        IsFFmpegInstalled = false;
    }

    private static bool CheckBinaryAvailable(string binaryName)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = binaryName,
                    Arguments = "-version",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            proc.Start();
            proc.WaitForExit(1500);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<VideoMetadata> ProbeVideoAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        if (!IsFFmpegInstalled)
            throw new InvalidOperationException("FFmpeg is not installed or could not be found.");

        int width = 1920;
        int height = 1080;
        double duration = 0.0;
        double fps = 30.0;
        long totalFrames = 0;
        bool hasAudio = false;
        string codec = "unknown";

        // Query video stream info
        var args = $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,nb_frames,codec_name -show_entries format=duration -of default=noprint_wrappers=1 \"{videoPath}\"";

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = FFprobePath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        proc.Start();
        string output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
        await proc.WaitForExitAsync(cancellationToken);

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var val = parts[1].Trim();

            switch (key)
            {
                case "width" when int.TryParse(val, out var w):
                    width = w;
                    break;
                case "height" when int.TryParse(val, out var h):
                    height = h;
                    break;
                case "codec_name":
                    codec = val;
                    break;
                case "duration" when double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d):
                    duration = d;
                    break;
                case "r_frame_rate":
                    var frac = val.Split('/');
                    if (frac.Length == 2 &&
                        double.TryParse(frac[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var num) &&
                        double.TryParse(frac[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var den) &&
                        den > 0)
                    {
                        fps = num / den;
                    }
                    break;
                case "nb_frames" when long.TryParse(val, out var f):
                    totalFrames = f;
                    break;
            }
        }

        if (totalFrames <= 0 && duration > 0 && fps > 0)
        {
            totalFrames = (long)Math.Round(duration * fps);
        }

        // Check audio stream presence
        var audioArgs = $"-v error -select_streams a:0 -show_entries stream=codec_name -of default=noprint_wrappers=1 \"{videoPath}\"";
        using var aProc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = FFprobePath,
                Arguments = audioArgs,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        aProc.Start();
        string aOut = await aProc.StandardOutput.ReadToEndAsync(cancellationToken);
        await aProc.WaitForExitAsync(cancellationToken);
        hasAudio = !string.IsNullOrWhiteSpace(aOut);

        return new VideoMetadata(width, height, duration, fps, totalFrames, hasAudio, codec);
    }

    public async Task<BitmapSource?> ExtractThumbnailAsync(string videoPath, double timeSeconds = 0.5, CancellationToken cancellationToken = default)
    {
        if (!IsFFmpegInstalled) return null;

        string timeStr = TimeSpan.FromSeconds(Math.Max(0.1, timeSeconds)).ToString(@"hh\:mm\:ss\.fff");
        var args = $"-ss {timeStr} -i \"{videoPath}\" -vframes 1 -f image2pipe -vcodec png -";

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        proc.Start();
        using var ms = new MemoryStream();
        await proc.StandardOutput.BaseStream.CopyToAsync(ms, cancellationToken);
        await proc.WaitForExitAsync(cancellationToken);

        if (ms.Length == 0) return null;

        ms.Seek(0, SeekOrigin.Begin);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }

    public static string BuildEncoderArguments(
        string inputVideoPath,
        string outputPath,
        int width,
        int height,
        double fps,
        VideoExportFormat format,
        bool preserveAudio,
        bool hasAudioSource)
    {
        string fpsStr = fps.ToString("0.###", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();

        // Input 0: Raw video stream from standard input
        sb.Append($"-y -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fpsStr} -i - ");

        // Input 1: Original audio from source if requested and exists
        bool includeAudio = preserveAudio && hasAudioSource && format != VideoExportFormat.PngSequence;
        if (includeAudio)
        {
            sb.Append($"-i \"{inputVideoPath}\" -map 0:v -map 1:a? ");
        }
        else
        {
            sb.Append("-map 0:v ");
        }

        switch (format)
        {
            case VideoExportFormat.ProRes4444_Mov:
                // Apple ProRes 4444 with transparent alpha
                sb.Append($"-c:v prores_ks -profile:v 4 -pix_fmt yuva444p10le {(includeAudio ? "-c:a copy" : "")} \"{outputPath}\"");
                break;

            case VideoExportFormat.WebM_VP9:
                // WebM VP9 with transparent alpha
                sb.Append($"-c:v libvpx-vp9 -pix_fmt yuva420p -b:v 0 -crf 25 {(includeAudio ? "-c:a libopus" : "")} \"{outputPath}\"");
                break;

            case VideoExportFormat.GreenScreen_Mp4:
            case VideoExportFormat.CustomBg_Mp4:
                // Standard H.264 MP4
                sb.Append($"-c:v libx264 -pix_fmt yuv420p -preset medium -crf 18 {(includeAudio ? "-c:a copy" : "")} \"{outputPath}\"");
                break;

            case VideoExportFormat.AlphaMask_Mp4:
                // Grayscale alpha matte
                sb.Append($"-c:v libx264 -pix_fmt yuv420p -preset medium -crf 18 \"{outputPath}\"");
                break;

            case VideoExportFormat.PngSequence:
                // Sequence of PNG files
                var framePattern = Path.Combine(outputPath, "frame_%05d.png");
                sb.Append($"-c:v png \"{framePattern}\"");
                break;
        }

        return sb.ToString();
    }

    public async Task ProcessVideoStreamingAsync(
        string inputVideoPath,
        string outputPath,
        VideoMetadata meta,
        MattingSettings settings,
        IMattingEngine mattingEngine,
        IProgress<(long frameIndex, long totalFrames, double fps, BitmapSource? preview)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsFFmpegInstalled)
            throw new InvalidOperationException("FFmpeg binary is not found.");

        int width = meta.Width;
        int height = meta.Height;
        int frameBytes = width * height * 4;
        byte[] frameBuffer = new byte[frameBytes];
        byte[]? customBgBuffer = null;

        if (settings.BgType == BackgroundType.CustomImage)
        {
            customBgBuffer = ImageProcessor.LoadResizedBackgroundBuffer(settings.CustomBgImagePath, width, height);
        }

        // 1. Decoder process: streams raw RGBA frames to stdout
        var decoderArgs = $"-i \"{inputVideoPath}\" -f rawvideo -pix_fmt rgba -";
        using var decoder = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments = decoderArgs,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        // 2. Encoder process: receives processed RGBA frames via stdin
        var encoderArgs = BuildEncoderArguments(
            inputVideoPath,
            outputPath,
            width,
            height,
            meta.Fps,
            settings.VideoFormat,
            settings.PreserveAudio,
            meta.HasAudio
        );

        if (settings.VideoFormat == VideoExportFormat.PngSequence && !Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        using var encoder = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = FFmpegPath,
                Arguments = encoderArgs,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true
            }
        };

        decoder.Start();
        encoder.Start();

        var decoderStream = decoder.StandardOutput.BaseStream;
        var encoderStream = encoder.StandardInput.BaseStream;

        long frameIndex = 0;
        long totalFrames = meta.TotalFrames > 0 ? meta.TotalFrames : 1;
        var stopwatch = Stopwatch.StartNew();
        var fpsTimer = Stopwatch.StartNew();
        long fpsFrameCount = 0;
        double currentFps = 0.0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Read exactly 1 frame from decoder
                int readTotal = 0;
                while (readTotal < frameBytes)
                {
                    int read = await decoderStream.ReadAsync(
                        frameBuffer.AsMemory(readTotal, frameBytes - readTotal),
                        cancellationToken
                    );
                    if (read == 0) break; // EOF
                    readTotal += read;
                }

                if (readTotal < frameBytes)
                    break; // Stream finished

                frameIndex++;
                fpsFrameCount++;

                // Apply Matting
                mattingEngine.ProcessRgbaBuffer(frameBuffer, width, height, settings, customBgBuffer);

                // Write to encoder
                await encoderStream.WriteAsync(frameBuffer.AsMemory(0, frameBytes), cancellationToken);

                // Calculate FPS every 10 frames
                if (fpsTimer.ElapsedMilliseconds >= 500)
                {
                    currentFps = fpsFrameCount / (fpsTimer.ElapsedMilliseconds / 1000.0);
                    fpsTimer.Restart();
                    fpsFrameCount = 0;
                }

                // Generate visual preview for UI periodically (every ~15 frames or at least once per second)
                BitmapSource? preview = null;
                if (frameIndex % 15 == 0 || frameIndex == 1)
                {
                    // Copy bytes for preview
                    var previewBytes = (byte[])frameBuffer.Clone();
                    // WPF uses BGRA, swap red and blue for preview
                    ImageProcessor.SwapRedBlue(previewBytes, width, height);
                    preview = ImageProcessor.CreateBitmapFromPixels(previewBytes, width, height);
                }

                progress?.Report((frameIndex, totalFrames, currentFps, preview));
            }

            encoderStream.Flush();
            encoder.StandardInput.Close();

            await decoder.WaitForExitAsync(cancellationToken);
            await encoder.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!decoder.HasExited) decoder.Kill(true);
                if (!encoder.HasExited) encoder.Kill(true);
            }
            catch { }
            throw;
        }
        finally
        {
            try
            {
                if (!decoder.HasExited) decoder.Kill(true);
                if (!encoder.HasExited) encoder.Kill(true);
            }
            catch { }
        }
    }
}
