using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NukkiStudio.Models;

public partial class MediaItem : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
    public MediaType Type { get; }
    public long FileSizeBytes { get; }
    public string FormattedFileSize => FormatBytes(FileSizeBytes);

    [ObservableProperty]
    private ProcessingStatus _status = ProcessingStatus.Pending;

    [ObservableProperty]
    private double _progressPercent = 0.0;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string? _outputPath;

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private BitmapSource? _originalPreview;

    [ObservableProperty]
    private BitmapSource? _processedPreview;

    [ObservableProperty]
    private VideoMetadata? _videoMetadata;

    [ObservableProperty]
    private TimeSpan _elapsedTime;

    [ObservableProperty]
    private TimeSpan? _estimatedTimeRemaining;

    [ObservableProperty]
    private double _currentFps;

    public MattingSettings Settings { get; set; } = new();

    public MediaItem(string filePath)
    {
        FilePath = filePath;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        Type = ext switch
        {
            ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" or ".wmv" or ".flv" or ".m4v" => MediaType.Video,
            _ => MediaType.Image
        };

        if (File.Exists(filePath))
        {
            var fi = new FileInfo(filePath);
            FileSizeBytes = fi.Length;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
