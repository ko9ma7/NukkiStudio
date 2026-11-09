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
    private MattingMode _selectedMode = MattingMode.AiNeural; // Default to AI Neural Matting!

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

    // Interactive Tools & Fine-Tuning State
    [ObservableProperty]
    private MattingToolMode _activeTool = MattingToolMode.None;

    [ObservableProperty]
    private double _brushSize = 35.0;

    public ObservableCollection<BrushStrokePoint> BrushStrokes { get; } = new();

    [ObservableProperty]
    private int _brushStrokeCount;

    // Magic Wand Seed Points
    public ObservableCollection<MagicWandPoint> MagicWandPoints { get; } = new();

    [ObservableProperty]
    private int _magicWandPointCount;

    [ObservableProperty]
    private double _magicWandTolerance = 0.15;

    // Lasso Polygons
    public ObservableCollection<LassoPolygon> LassoPolygons { get; } = new();

    [ObservableProperty]
    private int _lassoPolygonCount;

    // Quick Smart Presets
    [ObservableProperty]
    private QuickPresetMode _selectedPreset = QuickPresetMode.Custom;

    // Edge Defringe & Color Decontamination
    [ObservableProperty]
    private bool _enableDefringe = true;

    [ObservableProperty]
    private int _defringeRadius = 2; // 1 ~ 5px

    // Canvas Zoom & Pan
    [ObservableProperty]
    private double _zoomScale = 1.0;

    [ObservableProperty]
    private double _panOffsetX = 0.0;

    [ObservableProperty]
    private double _panOffsetY = 0.0;

    [ObservableProperty]
    private string _zoomText = "100%";

    // Undo / Redo Stacks
    private readonly Stack<MattingHistoryState> _undoStack = new();
    private readonly Stack<MattingHistoryState> _redoStack = new();

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private bool _enableRoi;

    [ObservableProperty]
    private Rect? _roiRect;

    [ObservableProperty]
    private string _roiDisplayInfo = "미지정 (전체 영역)";

    // Secondary Key Color (e.g. for ground shadows / floor reflections)
    [ObservableProperty]
    private bool _enableSecondaryKey;

    [ObservableProperty]
    private Color _secondaryKeyColor = Color.FromRgb(200, 200, 200);

    [ObservableProperty]
    private double _secondaryTolerance = 0.18;

    // Mask Fine-Tuning Edge Filters
    [ObservableProperty]
    private int _maskErodeDilate; // Negative = Erode/Choke fringe (-10 ~ 0), Positive = Dilate (0 ~ 10)

    [ObservableProperty]
    private int _maskFeather; // Soft edge feather (0 ~ 20)

    [ObservableProperty]
    private double _maskThreshold; // Black level cutoff (0.0 ~ 0.5)

    [ObservableProperty]
    private double _maskContrast = 1.0; // Mask contrast (0.5 ~ 3.0)

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

        // Auto-load built-in AI model on startup
        if (File.Exists(ModelManager.BuiltInModelPath))
        {
            try
            {
                _onnxEngine.LoadModel(ModelManager.BuiltInModelPath, useGpu: true);
                CustomOnnxPath = ModelManager.BuiltInModelPath;
                StatusMessage = "AI 엔진 기본 탑재 완료 (별도 설정 없이 자동 누끼 동작)";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load built-in model: {ex.Message}");
            }
        }

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

    // Fine-Tuning & Mask Filter Property Change Hooks
    partial void OnMaskErodeDilateChanged(int value) => UpdateLivePreview();
    partial void OnMaskFeatherChanged(int value) => UpdateLivePreview();
    partial void OnMaskThresholdChanged(double value) => UpdateLivePreview();
    partial void OnMaskContrastChanged(double value) => UpdateLivePreview();
    partial void OnEnableSecondaryKeyChanged(bool value) => UpdateLivePreview();
    partial void OnSecondaryKeyColorChanged(Color value) => UpdateLivePreview();
    partial void OnSecondaryToleranceChanged(double value) => UpdateLivePreview();
    partial void OnEnableRoiChanged(bool value) => UpdateLivePreview();
    partial void OnEnableDefringeChanged(bool value) => UpdateLivePreview();
    partial void OnDefringeRadiusChanged(int value) => UpdateLivePreview();
    partial void OnMagicWandToleranceChanged(double value) => UpdateLivePreview();

    partial void OnZoomScaleChanged(double value)
    {
        ZoomText = $"{Math.Round(value * 100)}%";
    }

    partial void OnActiveToolChanged(MattingToolMode value)
    {
        IsColorPickerActive = (value == MattingToolMode.ColorPicker);
        IsProtectPickerActive = false;
        StatusMessage = value switch
        {
            MattingToolMode.ColorPicker => "🔍 배경색 스포이트: 미리보기에서 추출할 주 배경색을 클릭하세요.",
            MattingToolMode.SecondaryColorPicker => "🎯 2차 배경색 스포이트: 제거할 바닥 그림자나 보조 색상을 클릭하세요.",
            MattingToolMode.RoiBox => "🔲 관심 영역(ROI) 박스: 남길 피사체 주변을 마우스로 드래그하여 사각형으로 지정하세요.",
            MattingToolMode.IncludeBrush => "🟢 살리기 브러시: 날아간 피사체(모자, 인형, 손 등)를 마우스로 칠해 100% 복원하세요.",
            MattingToolMode.ExcludeBrush => "🔴 지우기 브러시: 미처 제거되지 않은 배경, 얼룩, 그림자 등을 문질러 지우세요.",
            MattingToolMode.MagicWandErase => "🪄 마술봉(배경 제거): 지우고 싶은 배경 영역(바닥, 구석, 그림자 등)을 클릭하면 연결된 배경이 자동 삭제됩니다.",
            MattingToolMode.MagicWandKeep => "🪄 마술봉(피사체 복원): 실수로 지워진 피사체 부위를 클릭하면 연결된 전경이 자동 복원됩니다.",
            MattingToolMode.LassoInclude => "🪢 올가미(피사체 보호): 피사체 외곽을 자유롭게 둘러싸서 내부를 100% 안전하게 보호합니다.",
            MattingToolMode.LassoExclude => "🪢 올가미(영역 삭제): 불필요한 배경이나 주변 물체를 올가미로 묶어 일괄 삭제합니다.",
            _ => "준비 완료. 마우스로 도구를 선택하거나 영상을 변환하세요."
        };
    }

    public void RecordHistoryState(string description = "")
    {
        var state = new MattingHistoryState(
            BrushStrokes,
            MagicWandPoints,
            LassoPolygons,
            RoiRect,
            EnableRoi,
            description
        );
        _undoStack.Push(state);
        _redoStack.Clear();
        UpdateUndoRedoState();
    }

    private void UpdateUndoRedoState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
    }

    [RelayCommand]
    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var currentState = new MattingHistoryState(
            BrushStrokes,
            MagicWandPoints,
            LassoPolygons,
            RoiRect,
            EnableRoi,
            "Current"
        );
        _redoStack.Push(currentState);

        var previousState = _undoStack.Pop();
        RestoreHistoryState(previousState);
        UpdateUndoRedoState();
        StatusMessage = $"실행 취소됨: {previousState.Description}";
        UpdateLivePreview();
    }

    [RelayCommand]
    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var currentState = new MattingHistoryState(
            BrushStrokes,
            MagicWandPoints,
            LassoPolygons,
            RoiRect,
            EnableRoi,
            "Current"
        );
        _undoStack.Push(currentState);

        var nextState = _redoStack.Pop();
        RestoreHistoryState(nextState);
        UpdateUndoRedoState();
        StatusMessage = $"다시 실행됨: {nextState.Description}";
        UpdateLivePreview();
    }

    private void RestoreHistoryState(MattingHistoryState state)
    {
        BrushStrokes.Clear();
        foreach (var s in state.BrushStrokes) BrushStrokes.Add(s);
        BrushStrokeCount = BrushStrokes.Count;

        MagicWandPoints.Clear();
        foreach (var w in state.MagicWandPoints) MagicWandPoints.Add(w);
        MagicWandPointCount = MagicWandPoints.Count;

        LassoPolygons.Clear();
        foreach (var l in state.LassoPolygons) LassoPolygons.Add(l);
        LassoPolygonCount = LassoPolygons.Count;

        RoiRect = state.RoiRect;
        EnableRoi = state.EnableRoi;
        RoiDisplayInfo = (EnableRoi && RoiRect.HasValue)
            ? $"{Math.Round(RoiRect.Value.Width)} x {Math.Round(RoiRect.Value.Height)} px"
            : "미지정 (전체 영역)";
    }

    [RelayCommand]
    public void ApplyPreset(QuickPresetMode preset)
    {
        RecordHistoryState($"프리셋 변경 ({preset})");
        SelectedPreset = preset;

        switch (preset)
        {
            case QuickPresetMode.StudioFigurine:
                // Best for white figurine / product on white/gray studio background
                SelectedMode = MattingMode.ChromaKey;
                ContiguousOnly = true;
                Tolerance = 0.22;
                Smoothness = 0.08;
                SpillSuppression = 0.20;
                EnableSecondaryKey = true;
                SecondaryKeyColor = Color.FromRgb(215, 215, 215);
                SecondaryTolerance = 0.16;
                MaskErodeDilate = -2; // Choke fringe outline by 2px
                MaskFeather = 1;      // 1px anti-aliasing
                MaskContrast = 1.35;
                MaskThreshold = 0.05;
                EnableDefringe = true;
                DefringeRadius = 2;
                StatusMessage = "🧸 스튜디오 피규어/제품 프리셋 적용 (외곽 잔여 흰색 깎기, 바닥 그림자 제거, 엣지 복원)";
                break;

            case QuickPresetMode.PortraitHair:
                // Best for human portrait with fine hair strands
                SelectedMode = MattingMode.AiNeural;
                MaskErodeDilate = 0;
                MaskFeather = 2;
                MaskContrast = 1.10;
                MaskThreshold = 0.02;
                SpillSuppression = 0.50;
                EnableDefringe = true;
                DefringeRadius = 2;
                StatusMessage = "👤 인물 & 헤어 프리셋 적용 (AI 고정밀 신경망, 자연스러운 머리카락 페더링)";
                break;

            case QuickPresetMode.GreenScreen:
                // Standard green/blue chroma screen video
                SelectedMode = MattingMode.ChromaKey;
                KeyColor = Color.FromRgb(0, 255, 0);
                Tolerance = 0.28;
                Smoothness = 0.12;
                SpillSuppression = 0.70;
                MaskErodeDilate = -1;
                MaskFeather = 1;
                MaskContrast = 1.0;
                EnableDefringe = true;
                DefringeRadius = 2;
                StatusMessage = "🟩 그린스크린 크로마키 프리셋 적용 (녹색 반사광 억제 및 부드러운 합성)";
                break;

            case QuickPresetMode.GraphicLogo:
                // Flat logos, graphics, icons
                Tolerance = 0.18;
                MaskThreshold = 0.25;
                MaskContrast = 2.5;
                MaskFeather = 0;
                MaskErodeDilate = 0;
                EnableDefringe = false;
                StatusMessage = "📐 로고 & 그래픽 프리셋 적용 (선명한 흑백 알파 컷오프)";
                break;
        }

        UpdateLivePreview();
    }

    [RelayCommand]
    public void ZoomIn()
    {
        ZoomScale = Math.Min(8.0, Math.Round(ZoomScale * 1.25, 2));
    }

    [RelayCommand]
    public void ZoomOut()
    {
        ZoomScale = Math.Max(0.25, Math.Round(ZoomScale / 1.25, 2));
    }

    [RelayCommand]
    public void ZoomFit()
    {
        ZoomScale = 1.0;
        PanOffsetX = 0.0;
        PanOffsetY = 0.0;
    }

    [RelayCommand]
    public void Zoom100()
    {
        ZoomScale = 1.0;
    }

    [RelayCommand]
    public void Zoom200()
    {
        ZoomScale = 2.0;
    }

    [RelayCommand]
    public void Zoom400()
    {
        ZoomScale = 4.0;
    }

    [RelayCommand]
    public void ToggleTool(MattingToolMode tool)
    {
        ActiveTool = (ActiveTool == tool) ? MattingToolMode.None : tool;
    }

    [RelayCommand]
    public void ClearBrushStrokes()
    {
        RecordHistoryState("브러시 전체 초기화");
        BrushStrokes.Clear();
        BrushStrokeCount = 0;
        StatusMessage = "브러시 스트로크가 모두 초기화되었습니다.";
        UpdateLivePreview();
    }

    public void AddMagicWandPoint(double x, double y, bool isInclude)
    {
        RecordHistoryState(isInclude ? "마술봉 영역 살리기" : "마술봉 영역 지우기");
        MagicWandPoints.Add(new MagicWandPoint(x, y, MagicWandTolerance, isInclude));
        MagicWandPointCount = MagicWandPoints.Count;
        StatusMessage = isInclude
            ? $"🪄 마술봉: ({Math.Round(x)}, {Math.Round(y)}) 주변 연결 피사체 복원"
            : $"🪄 마술봉: ({Math.Round(x)}, {Math.Round(y)}) 주변 연결 배경 제거";
        UpdateLivePreview();
    }

    public void AddLassoPolygon(List<Point> points, bool isInclude)
    {
        if (points.Count < 3) return;
        RecordHistoryState(isInclude ? "올가미 피사체 보호" : "올가미 영역 삭제");
        LassoPolygons.Add(new LassoPolygon(points, isInclude));
        LassoPolygonCount = LassoPolygons.Count;
        StatusMessage = isInclude
            ? $"🪢 올가미: {points.Count}개 정점 피사체 보호 적용"
            : $"🪢 올가미: {points.Count}개 정점 영역 삭제 적용";
        UpdateLivePreview();
    }

    [RelayCommand]
    public void ClearMagicWandPoints()
    {
        RecordHistoryState("마술봉 포인트 전체 초기화");
        MagicWandPoints.Clear();
        MagicWandPointCount = 0;
        StatusMessage = "마술봉 지정 포인트가 초기화되었습니다.";
        UpdateLivePreview();
    }

    [RelayCommand]
    public void ClearLassoPolygons()
    {
        RecordHistoryState("올가미 다각형 전체 초기화");
        LassoPolygons.Clear();
        LassoPolygonCount = 0;
        StatusMessage = "올가미 지정 다각형이 초기화되었습니다.";
        UpdateLivePreview();
    }

    [RelayCommand]
    public void ResetRoi()
    {
        RecordHistoryState("관심 영역(ROI) 해제");
        RoiRect = null;
        EnableRoi = false;
        RoiDisplayInfo = "미지정 (전체 영역)";
        StatusMessage = "관심 영역(ROI)이 초기화되어 전체 영역으로 복귀했습니다.";
        UpdateLivePreview();
    }

    [RelayCommand]
    public void ResetMaskFilters()
    {
        MaskErodeDilate = 0;
        MaskFeather = 0;
        MaskThreshold = 0.0;
        MaskContrast = 1.0;
        EnableDefringe = true;
        DefringeRadius = 2;
        StatusMessage = "마스크 보정 필터가 기본값으로 초기화되었습니다.";
        UpdateLivePreview();
    }

    [RelayCommand]
    public void ResetAllFineTuning()
    {
        RecordHistoryState("모든 세부 선택 및 마스크 초기화");
        BrushStrokes.Clear();
        BrushStrokeCount = 0;
        MagicWandPoints.Clear();
        MagicWandPointCount = 0;
        LassoPolygons.Clear();
        LassoPolygonCount = 0;
        RoiRect = null;
        EnableRoi = false;
        RoiDisplayInfo = "미지정 (전체 영역)";
        MaskErodeDilate = 0;
        MaskFeather = 0;
        MaskThreshold = 0.0;
        MaskContrast = 1.0;
        EnableDefringe = true;
        DefringeRadius = 2;
        EnableSecondaryKey = false;
        ClearProtectedPoints();
        StatusMessage = "모든 세부 선택 및 마스크 필터가 초기화되었습니다.";
        UpdateLivePreview();
    }

    public void AddBrushStroke(double x, double y, bool isInclude)
    {
        BrushStrokes.Add(new BrushStrokePoint(x, y, BrushSize, isInclude));
        BrushStrokeCount = BrushStrokes.Count;
        UpdateLivePreview();
    }

    public void AddBrushStrokes(IEnumerable<BrushStrokePoint> points)
    {
        foreach (var pt in points)
        {
            BrushStrokes.Add(pt);
        }
        BrushStrokeCount = BrushStrokes.Count;
        UpdateLivePreview();
    }

    public void SetRoi(Rect rect)
    {
        RecordHistoryState("관심 영역(ROI) 박스 설정");
        RoiRect = rect;
        EnableRoi = true;
        RoiDisplayInfo = $"{Math.Round(rect.Width)} x {Math.Round(rect.Height)} px";
        StatusMessage = $"관심 영역(ROI) 박스 적용됨 ({RoiDisplayInfo})";
        UpdateLivePreview();
    }

    partial void OnIsColorPickerActiveChanged(bool value)
    {
        if (value)
        {
            IsProtectPickerActive = false;
            ActiveTool = MattingToolMode.ColorPicker;
        }
        else if (ActiveTool == MattingToolMode.ColorPicker)
        {
            ActiveTool = MattingToolMode.None;
        }
    }

    partial void OnIsProtectPickerActiveChanged(bool value)
    {
        if (value)
        {
            IsColorPickerActive = false;
            ActiveTool = MattingToolMode.None;
        }
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
        if (item.OriginalPreview != null)
        {
            AutoDetectBackgroundColor(item.OriginalPreview);
        }
        UpdateLivePreview();
    }

    private void AutoDetectBackgroundColor(BitmapSource bmp)
    {
        try
        {
            int w = bmp.PixelWidth;
            int h = bmp.PixelHeight;
            if (w < 2 || h < 2) return;

            // Sample 4 outer corners to automatically identify background color
            var rects = new[]
            {
                new Int32Rect(0, 0, 1, 1),
                new Int32Rect(w - 1, 0, 1, 1),
                new Int32Rect(0, h - 1, 1, 1),
                new Int32Rect(w - 1, h - 1, 1, 1)
            };

            int totalB = 0, totalG = 0, totalR = 0;
            byte[] pixel = new byte[4];
            foreach (var rect in rects)
            {
                var cropped = new CroppedBitmap(bmp, rect);
                cropped.CopyPixels(pixel, 4, 0);
                totalB += pixel[0];
                totalG += pixel[1];
                totalR += pixel[2];
            }

            KeyColor = Color.FromRgb((byte)(totalR / 4), (byte)(totalG / 4), (byte)(totalB / 4));
        }
        catch { }
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

            var cropped = new CroppedBitmap(OriginalPreviewImage, new Int32Rect(pixelX, pixelY, 1, 1));
            byte[] pixel = new byte[4];
            cropped.CopyPixels(pixel, 4, 0);

            // PixelFormats.Bgra32: [0] = Blue, [1] = Green, [2] = Red, [3] = Alpha
            var sampledColor = Color.FromRgb(pixel[2], pixel[1], pixel[0]);

            if (ActiveTool == MattingToolMode.SecondaryColorPicker)
            {
                SecondaryKeyColor = sampledColor;
                EnableSecondaryKey = true;
                ActiveTool = MattingToolMode.None;
                StatusMessage = $"2차 배경/그림자 색상이 지정되었습니다: #{sampledColor.R:X2}{sampledColor.G:X2}{sampledColor.B:X2}";
                UpdateLivePreview();
                return;
            }

            if (ActiveTool == MattingToolMode.ColorPicker || IsColorPickerActive)
            {
                KeyColor = sampledColor;
                ActiveTool = MattingToolMode.None;
                IsColorPickerActive = false;
                StatusMessage = $"배경 기준색이 추출되었습니다: #{KeyColor.R:X2}{KeyColor.G:X2}{KeyColor.B:X2}";
                UpdateLivePreview();
                return;
            }

            if (IsProtectPickerActive)
            {
                ProtectedPoints.Add(new Point(pixelX, pixelY));
                ProtectedPointsCount = ProtectedPoints.Count;
                StatusMessage = $"전경 보호 영역이 추가되었습니다 ({ProtectedPointsCount}개 지점).";
                UpdateLivePreview();
                return;
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
            EnableSecondaryKey = EnableSecondaryKey,
            SecondaryKeyColor = SecondaryKeyColor,
            SecondaryTolerance = SecondaryTolerance,
            EnableRoi = EnableRoi,
            RoiRect = RoiRect,
            MaskErodeDilate = MaskErodeDilate,
            MaskFeather = MaskFeather,
            MaskThreshold = MaskThreshold,
            MaskContrast = MaskContrast,
            EnableDefringe = EnableDefringe,
            DefringeRadius = DefringeRadius,
            QuickPreset = SelectedPreset,
            MagicWandTolerance = MagicWandTolerance,
            MagicWandPoints = MagicWandPoints.ToList(),
            LassoPolygons = LassoPolygons.ToList(),
            BrushStrokes = BrushStrokes.ToList(),
            CustomBgColor = CustomBgColor,
            CustomBgImagePath = CustomBgImagePath,
            VideoFormat = SelectedVideoFormat,
            ImageFormat = SelectedImageFormat,
            PreserveAudio = PreserveAudio,
            OutputDirectory = OutputDirectory
        };
    }
}
