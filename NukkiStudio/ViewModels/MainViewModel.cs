using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NukkiStudio.Models;
using NukkiStudio.Services;

namespace NukkiStudio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly FFmpegService _ffmpegService;
    private readonly ChromaKeyMattingEngine _chromaEngine;
    private readonly OnnxMattingEngine _onnxEngine;
    private readonly ModelManager _modelManager;
    private readonly DialogService _dialogService;
    private CancellationTokenSource? _processingCts;

    [ObservableProperty]
    private ObservableCollection<MediaItem> _queue = new();

    [ObservableProperty]
    private MediaItem? _selectedItem;

    // Active Settings Bindings
    [ObservableProperty]
    private MattingMode _selectedMode = MattingMode.ChromaKey;

    [ObservableProperty]
    private BackgroundType _selectedBgType = BackgroundType.Transparent;

    [ObservableProperty]
    private Color _keyColor = Color.FromRgb(0, 255, 0);

    [ObservableProperty]
    private double _tolerance = 0.28;

    [ObservableProperty]
    private double _smoothness = 0.12;

    [ObservableProperty]
    private double _spillSuppression = 0.50;

    [ObservableProperty]
    private Color _customBgColor = Colors.White;

    [ObservableProperty]
    private string? _customBgImagePath;

    [ObservableProperty]
    private VideoExportFormat _selectedVideoFormat = VideoExportFormat.ProRes4444_Mov;

    [ObservableProperty]
    private ImageExportFormat _selectedImageFormat = ImageExportFormat.PngTransparent;

    [ObservableProperty]
    private bool _preserveAudio = true;

    [ObservableProperty]
    private string _outputDirectory = "";

    // Execution States
    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _statusMessage = "Ready. Drag & Drop video or images to start.";

    private bool _isFFmpegAvailable;
    public bool IsFFmpegAvailable
    {
        get => _isFFmpegAvailable;
        set => SetProperty(ref _isFFmpegAvailable, value);
    }

    private string _ffmpegStatusText = "";
    public string FFmpegStatusText
    {
        get => _ffmpegStatusText;
        set => SetProperty(ref _ffmpegStatusText, value);
    }

    // AI Models
    [ObservableProperty]
    private ObservableCollection<AiModelInfo> _modelPresets = new();

    [ObservableProperty]
    private AiModelInfo? _selectedModelPreset;

    [ObservableProperty]
    private string _customOnnxPath = "";

    [ObservableProperty]
    private bool _isModelDownloading;

    [ObservableProperty]
    private double _modelDownloadProgress;

    // Interactive Preview
    [ObservableProperty]
    private BitmapSource? _originalPreviewImage;

    [ObservableProperty]
    private BitmapSource? _processedPreviewImage;

    [ObservableProperty]
    private double _splitPosition = 50.0; // 0% to 100% split view

    [ObservableProperty]
    private bool _isColorPickerActive;

    [ObservableProperty]
    private bool _isProtectPickerActive;

    [ObservableProperty]
    private bool _contiguousOnly = true;

    [ObservableProperty]
    private double _protectRadius = 35.0;

    public ObservableCollection<Point> ProtectedPoints { get; } = new();

    [ObservableProperty]
    private int _protectedPointsCount;

    public MainViewModel()
    {
        _ffmpegService = new FFmpegService();
        _chromaEngine = new ChromaKeyMattingEngine();
        _onnxEngine = new OnnxMattingEngine();
        _modelManager = new ModelManager();
        _dialogService = new DialogService();

        OutputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "NukkiStudio_Export"
        );

        foreach (var preset in ModelManager.Presets)
        {
            ModelPresets.Add(preset);
        }
        SelectedModelPreset = ModelPresets.FirstOrDefault();

        CheckFFmpegStatus();
    }

    private void CheckFFmpegStatus()
    {
        IsFFmpegAvailable = _ffmpegService.IsFFmpegInstalled;
        FFmpegStatusText = IsFFmpegAvailable
            ? $"FFmpeg Ready ({Path.GetFileName(_ffmpegService.FFmpegPath)})"
            : "FFmpeg not detected. Please install FFmpeg for video processing.";
    }

    partial void OnSelectedItemChanged(MediaItem? value)
    {
        if (value != null)
        {
            LoadPreviewForItem(value);
        }
    }

    partial void OnToleranceChanged(double value) => UpdateLivePreview();
    partial void OnSmoothnessChanged(double value) => UpdateLivePreview();
    partial void OnSpillSuppressionChanged(double value) => UpdateLivePreview();
    partial void OnKeyColorChanged(Color value) => UpdateLivePreview();
    partial void OnSelectedModeChanged(MattingMode value) => UpdateLivePreview();
    partial void OnSelectedBgTypeChanged(BackgroundType value) => UpdateLivePreview();
    partial void OnCustomBgColorChanged(Color value) => UpdateLivePreview();
    partial void OnContiguousOnlyChanged(bool value) => UpdateLivePreview();
    partial void OnProtectRadiusChanged(double value) => UpdateLivePreview();

    partial void OnIsColorPickerActiveChanged(bool value)
    {
        if (value) IsProtectPickerActive = false;
    }

    partial void OnIsProtectPickerActiveChanged(bool value)
    {
        if (value) IsColorPickerActive = false;
    }

    [RelayCommand]
    public void ClearProtectedPoints()
    {
        ProtectedPoints.Clear();
        ProtectedPointsCount = 0;
        UpdateLivePreview();
    }

    [RelayCommand]
    public void AddFiles()
    {
        var files = _dialogService.OpenMediaFiles();
        if (files != null)
        {
            AddFilesInternal(files);
        }
    }

    public void AddFilesInternal(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            if (File.Exists(path) && !Queue.Any(q => q.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                var item = new MediaItem(path);
                Queue.Add(item);
                _ = InitializeMediaItemAsync(item);
            }
        }

        if (SelectedItem == null && Queue.Count > 0)
        {
            SelectedItem = Queue.First();
        }
    }

    private async Task InitializeMediaItemAsync(MediaItem item)
    {
        try
        {
            if (item.Type == MediaType.Video)
            {
                if (_ffmpegService.IsFFmpegInstalled)
                {
                    item.VideoMetadata = await _ffmpegService.ProbeVideoAsync(item.FilePath);
                    item.Thumbnail = await _ffmpegService.ExtractThumbnailAsync(item.FilePath, 1.0);
                    item.OriginalPreview = item.Thumbnail;
                }
            }
            else
            {
                var bmp = ImageProcessor.LoadBitmap(item.FilePath);
                item.OriginalPreview = bmp;
                item.Thumbnail = ImageProcessor.CreateThumbnail(bmp, 128, 128);
            }

            if (SelectedItem == item)
            {
                LoadPreviewForItem(item);
            }
        }
        catch (Exception ex)
        {
            item.StatusMessage = $"Load Error: {ex.Message}";
        }
    }

    private void LoadPreviewForItem(MediaItem item)
    {
        OriginalPreviewImage = item.OriginalPreview;
        UpdateLivePreview();
    }

    public void UpdateLivePreview()
    {
        if (OriginalPreviewImage == null)
        {
            ProcessedPreviewImage = null;
            return;
        }

        try
        {
            var currentSettings = CreateCurrentSettings();
            int width = OriginalPreviewImage.PixelWidth;
            int height = OriginalPreviewImage.PixelHeight;

            // For ultra-smooth UI response on 4K/8K images, downscale preview if large
            BitmapSource sourceToProcess = OriginalPreviewImage;
            if (width > 1280 || height > 720)
            {
                sourceToProcess = ImageProcessor.CreateThumbnail(OriginalPreviewImage, 1280, 720);
                width = sourceToProcess.PixelWidth;
                height = sourceToProcess.PixelHeight;
            }

            byte[] pixels = ImageProcessor.GetPixelBytes(sourceToProcess, out width, out height, out _);

            // Convert BGRA to RGBA for matting engines
            ImageProcessor.SwapRedBlue(pixels, width, height);

            byte[]? bgPixels = null;
            if (currentSettings.BgType == BackgroundType.CustomImage)
            {
                bgPixels = ImageProcessor.LoadResizedBackgroundBuffer(currentSettings.CustomBgImagePath, width, height);
                if (bgPixels != null) ImageProcessor.SwapRedBlue(bgPixels, width, height);
            }

            if (SelectedMode == MattingMode.ChromaKey)
            {
                _chromaEngine.ProcessRgbaBuffer(pixels, width, height, currentSettings, bgPixels);
            }
            else
            {
                if (_onnxEngine.IsAvailable)
                {
                    _onnxEngine.ProcessRgbaBuffer(pixels, width, height, currentSettings, bgPixels);
                }
                else
                {
                    // Fallback to ChromaKey if ONNX not yet loaded
                    _chromaEngine.ProcessRgbaBuffer(pixels, width, height, currentSettings, bgPixels);
                }
            }

            // Convert back to BGRA for WPF display
            ImageProcessor.SwapRedBlue(pixels, width, height);
            ProcessedPreviewImage = ImageProcessor.CreateBitmapFromPixels(pixels, width, height);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Preview update error: {ex.Message}");
        }
    }

    [RelayCommand]
    public void RemoveItem(MediaItem item)
    {
        Queue.Remove(item);
        if (SelectedItem == item)
        {
            SelectedItem = Queue.FirstOrDefault();
        }
    }

    [RelayCommand]
    public void ClearQueue()
    {
        Queue.Clear();
        SelectedItem = null;
        OriginalPreviewImage = null;
        ProcessedPreviewImage = null;
    }

    [RelayCommand]
    public void SelectOutputFolder()
    {
        var folder = _dialogService.SelectOutputFolder(OutputDirectory);
        if (!string.IsNullOrEmpty(folder))
        {
            OutputDirectory = folder;
        }
    }

    [RelayCommand]
    public void OpenOutputFolder()
    {
        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = OutputDirectory,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    public void SelectCustomBgImage()
    {
        var file = _dialogService.OpenBackgroundImageFile();
        if (!string.IsNullOrEmpty(file))
        {
            CustomBgImagePath = file;
            SelectedBgType = BackgroundType.CustomImage;
            UpdateLivePreview();
        }
    }

    [RelayCommand]
    public void SelectCustomOnnxModel()
    {
        var file = _dialogService.OpenOnnxModelFile();
        if (!string.IsNullOrEmpty(file))
        {
            CustomOnnxPath = file;
            try
            {
                _onnxEngine.LoadModel(file, useGpu: true);
                _dialogService.ShowInfo("AI Model Loaded", $"성공적으로 모델을 불러왔습니다: {Path.GetFileName(file)}");
                UpdateLivePreview();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("Model Load Error", ex.Message);
            }
        }
    }

    [RelayCommand]
    public async Task DownloadSelectedPresetModelAsync()
    {
        if (SelectedModelPreset == null) return;

        IsModelDownloading = true;
        ModelDownloadProgress = 0.0;
        StatusMessage = $"Downloading AI model: {SelectedModelPreset.Name}...";

        try
        {
            var progress = new Progress<double>(p => ModelDownloadProgress = p * 100.0);
            var downloadedPath = await _modelManager.DownloadModelAsync(SelectedModelPreset, progress);

            CustomOnnxPath = downloadedPath;
            _onnxEngine.LoadModel(downloadedPath, useGpu: true);
            StatusMessage = $"AI Model {SelectedModelPreset.Name} loaded and ready!";
            _dialogService.ShowInfo("다운로드 완료", $"{SelectedModelPreset.Name} 모델이 성공적으로 다운로드 및 적용되었습니다.");
            UpdateLivePreview();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Model download failed: {ex.Message}";
            _dialogService.ShowError("다운로드 실패", $"모델 다운로드 중 오류가 발생했습니다: {ex.Message}");
        }
        finally
        {
            IsModelDownloading = false;
        }
    }

    [RelayCommand]
    public async Task StartProcessingAsync()
    {
        if (Queue.Count == 0)
        {
            _dialogService.ShowInfo("안내", "처리할 파일이 없습니다. 먼저 영상 또는 이미지를 추가해주세요.");
            return;
        }

        if (IsProcessing) return;

        if (!Directory.Exists(OutputDirectory))
        {
            Directory.CreateDirectory(OutputDirectory);
        }

        IsProcessing = true;
        _processingCts = new CancellationTokenSource();
        var token = _processingCts.Token;

        int totalCount = Queue.Count;
        int completedCount = 0;
        StatusMessage = "Processing batch queue...";

        try
        {
            for (int i = 0; i < Queue.Count; i++)
            {
                if (token.IsCancellationRequested) break;

                var item = Queue[i];
                SelectedItem = item;
                item.Status = ProcessingStatus.Processing;
                item.ProgressPercent = 0.0;
                item.StatusMessage = "Processing...";

                var settings = CreateCurrentSettings();
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    if (item.Type == MediaType.Image)
                    {
                        await ProcessImageItemAsync(item, settings, token);
                    }
                    else
                    {
                        if (!_ffmpegService.IsFFmpegInstalled)
                        {
                            throw new InvalidOperationException("FFmpeg가 감지되지 않아 비디오를 처리할 수 없습니다.");
                        }
                        await ProcessVideoItemAsync(item, settings, token);
                    }

                    item.Status = ProcessingStatus.Completed;
                    item.ProgressPercent = 100.0;
                    item.StatusMessage = "Completed!";
                    item.ElapsedTime = stopwatch.Elapsed;
                    completedCount++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = ProcessingStatus.Cancelled;
                    item.StatusMessage = "Cancelled";
                    break;
                }
                catch (Exception ex)
                {
                    item.Status = ProcessingStatus.Failed;
                    item.StatusMessage = $"Error: {ex.Message}";
                }

                OverallProgress = (double)completedCount / totalCount * 100.0;
            }

            StatusMessage = token.IsCancellationRequested
                ? "Processing cancelled."
                : $"Batch processing finished. {completedCount}/{totalCount} completed.";
        }
        finally
        {
            IsProcessing = false;
            _processingCts?.Dispose();
            _processingCts = null;
        }
    }

    private async Task ProcessImageItemAsync(MediaItem item, MattingSettings settings, CancellationToken token)
    {
        await Task.Run(() =>
        {
            var bmp = ImageProcessor.LoadBitmap(item.FilePath);
            int width = bmp.PixelWidth;
            int height = bmp.PixelHeight;

            byte[] pixels = ImageProcessor.GetPixelBytes(bmp, out width, out height, out _);
            ImageProcessor.SwapRedBlue(pixels, width, height);

            byte[]? bgPixels = null;
            if (settings.BgType == BackgroundType.CustomImage)
            {
                bgPixels = ImageProcessor.LoadResizedBackgroundBuffer(settings.CustomBgImagePath, width, height);
                if (bgPixels != null) ImageProcessor.SwapRedBlue(bgPixels, width, height);
            }

            IMattingEngine engine = settings.Mode == MattingMode.AiNeural && _onnxEngine.IsAvailable
                ? _onnxEngine
                : _chromaEngine;

            engine.ProcessRgbaBuffer(pixels, width, height, settings, bgPixels);
            ImageProcessor.SwapRedBlue(pixels, width, height);

            var resultBmp = ImageProcessor.CreateBitmapFromPixels(pixels, width, height);

            // Determine output extension and filename
            string ext = settings.ImageFormat switch
            {
                ImageExportFormat.PngTransparent => ".png",
                ImageExportFormat.AlphaMaskPng => "_mask.png",
                ImageExportFormat.JpgComposite => ".jpg",
                _ => ".png"
            };

            string outFileName = $"{Path.GetFileNameWithoutExtension(item.FilePath)}_nukki{ext}";
            string outPath = Path.Combine(OutputDirectory, outFileName);

            ImageProcessor.SaveImage(resultBmp, outPath, settings.ImageFormat);
            item.OutputPath = outPath;
            item.ProcessedPreview = resultBmp;
        }, token);
    }

    private async Task ProcessVideoItemAsync(MediaItem item, MattingSettings settings, CancellationToken token)
    {
        if (item.VideoMetadata == null)
        {
            item.VideoMetadata = await _ffmpegService.ProbeVideoAsync(item.FilePath, token);
        }

        string ext = settings.VideoFormat switch
        {
            VideoExportFormat.ProRes4444_Mov => ".mov",
            VideoExportFormat.WebM_VP9 => ".webm",
            VideoExportFormat.AlphaMask_Mp4 => "_mask.mp4",
            VideoExportFormat.PngSequence => "_frames",
            _ => ".mp4"
        };

        string outName = $"{Path.GetFileNameWithoutExtension(item.FilePath)}_nukki{ext}";
        string outPath = Path.Combine(OutputDirectory, outName);
        item.OutputPath = outPath;

        IMattingEngine engine = settings.Mode == MattingMode.AiNeural && _onnxEngine.IsAvailable
            ? _onnxEngine
            : _chromaEngine;

        var progress = new Progress<(long frameIndex, long totalFrames, double fps, BitmapSource? preview)>(p =>
        {
            if (p.totalFrames > 0)
            {
                item.ProgressPercent = (double)p.frameIndex / p.totalFrames * 100.0;
            }
            item.CurrentFps = p.fps;
            item.StatusMessage = $"Frame {p.frameIndex}/{p.totalFrames} ({p.fps:F1} FPS)";
            if (p.preview != null)
            {
                item.ProcessedPreview = p.preview;
                if (SelectedItem == item)
                {
                    ProcessedPreviewImage = p.preview;
                }
            }
        });

        await _ffmpegService.ProcessVideoStreamingAsync(
            item.FilePath,
            outPath,
            item.VideoMetadata,
            settings,
            engine,
            progress,
            token
        );
    }

    [RelayCommand]
    public void CancelProcessing()
    {
        _processingCts?.Cancel();
    }

    public void SampleColorFromPreview(int pixelX, int pixelY)
    {
        if (OriginalPreviewImage == null) return;
        try
        {
            if (pixelX < 0 || pixelX >= OriginalPreviewImage.PixelWidth ||
                pixelY < 0 || pixelY >= OriginalPreviewImage.PixelHeight)
                return;

            if (IsProtectPickerActive)
            {
                ProtectedPoints.Add(new Point(pixelX, pixelY));
                ProtectedPointsCount = ProtectedPoints.Count;
                StatusMessage = $"전경 보호 영역이 추가되었습니다 ({ProtectedPointsCount}개 지점).";
                UpdateLivePreview();
                return;
            }

            if (IsColorPickerActive)
            {
                var cropped = new CroppedBitmap(OriginalPreviewImage, new Int32Rect(pixelX, pixelY, 1, 1));
                byte[] pixel = new byte[4];
                cropped.CopyPixels(pixel, 4, 0);

                // PixelFormats.Bgra32: [0] = Blue, [1] = Green, [2] = Red, [3] = Alpha
                KeyColor = Color.FromRgb(pixel[2], pixel[1], pixel[0]);
                IsColorPickerActive = false;
                StatusMessage = $"배경 기준색이 추출되었습니다: #{KeyColor.R:X2}{KeyColor.G:X2}{KeyColor.B:X2}";
                UpdateLivePreview();
            }
        }
        catch { }
    }

    private MattingSettings CreateCurrentSettings()
    {
        return new MattingSettings
        {
            Mode = SelectedMode,
            BgType = SelectedBgType,
            KeyColor = KeyColor,
            Tolerance = Tolerance,
            Smoothness = Smoothness,
            SpillSuppression = SpillSuppression,
            ContiguousOnly = ContiguousOnly,
            ProtectedPoints = ProtectedPoints.ToList(),
            ProtectRadius = ProtectRadius,
            CustomBgColor = CustomBgColor,
            CustomBgImagePath = CustomBgImagePath,
            VideoFormat = SelectedVideoFormat,
            ImageFormat = SelectedImageFormat,
            PreserveAudio = PreserveAudio,
            OutputDirectory = OutputDirectory
        };
    }
}
