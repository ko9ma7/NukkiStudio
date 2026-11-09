using System.Windows.Media;

namespace NukkiStudio.Models;

public class MattingSettings
{
    // General
    public MattingMode Mode { get; set; } = MattingMode.AiNeural;
    public BackgroundType BgType { get; set; } = BackgroundType.Transparent;
    public Color CustomBgColor { get; set; } = Colors.White;
    public string? CustomBgImagePath { get; set; }

    // ChromaKey parameters
    public Color KeyColor { get; set; } = Color.FromRgb(0, 255, 0); // Default Pure Green #00FF00
    public double Tolerance { get; set; } = 0.28; // 0.0 ~ 1.0 (Color similarity threshold)
    public double Smoothness { get; set; } = 0.12; // 0.0 ~ 1.0 (Edge transition falloff)
    public double SpillSuppression { get; set; } = 0.50; // 0.0 ~ 1.0 (Removes green reflections on hair/edges)
    public bool ContiguousOnly { get; set; } = true; // Only remove background connected to exterior borders
    public List<System.Windows.Point> ProtectedPoints { get; set; } = new(); // Areas to exclude from removal
    public double ProtectRadius { get; set; } = 35.0;

    // Secondary Key Color (e.g. for ground shadows / secondary background tint)
    public bool EnableSecondaryKey { get; set; } = false;
    public Color SecondaryKeyColor { get; set; } = Color.FromRgb(200, 200, 200);
    public double SecondaryTolerance { get; set; } = 0.18;

    // Subject Isolation Bounding Box (ROI)
    public bool EnableRoi { get; set; } = false;
    public System.Windows.Rect? RoiRect { get; set; }

    // Mask Post-Processing Fine-Tuning
    public int MaskErodeDilate { get; set; } = 0; // Negative = Erode/Choke fringe, Positive = Dilate
    public int MaskFeather { get; set; } = 0; // Edge blur/feather radius (0 ~ 20)
    public double MaskThreshold { get; set; } = 0.0; // Black cutoff for faint artifacts (0.0 ~ 0.8)
    public double MaskContrast { get; set; } = 1.0; // S-curve contrast boost (0.5 ~ 3.0)

    // Interactive Paint Strokes
    public List<NukkiStudio.Services.BrushStrokePoint> BrushStrokes { get; set; } = new();

    // AI Neural parameters
    public string SelectedModelPath { get; set; } = "";
    public bool UseGpuAcceleration { get; set; } = true;

    // Quick Preset Profile
    public QuickPresetMode QuickPreset { get; set; } = QuickPresetMode.Custom;

    // Edge Defringe / Decontamination (Removes white halo reflection from edge pixels)
    public bool EnableDefringe { get; set; } = true;
    public int DefringeRadius { get; set; } = 2; // 1 ~ 5px

    // Magic Wand Seed Points
    public double MagicWandTolerance { get; set; } = 0.15;
    public List<NukkiStudio.Services.MagicWandPoint> MagicWandPoints { get; set; } = new();

    // Lasso Polygons
    public List<NukkiStudio.Services.LassoPolygon> LassoPolygons { get; set; } = new();

    // Export formats
    public VideoExportFormat VideoFormat { get; set; } = VideoExportFormat.ProRes4444_Mov;
    public ImageExportFormat ImageFormat { get; set; } = ImageExportFormat.PngTransparent;

    // Web Cloud Matting parameters
    public WebMattingProvider WebProvider { get; set; } = WebMattingProvider.RemoveBg;
    public string WebApiKey { get; set; } = "";
    public string WebEndpointUrl { get; set; } = "https://api.remove.bg/v1.0/removebg";

    // Video output parameters
    public int OutputWidth { get; set; } = 0; // 0 = Keep original
    public int OutputHeight { get; set; } = 0;
    public double OutputFps { get; set; } = 0; // 0 = Keep original
    public bool PreserveAudio { get; set; } = true;
    public string OutputDirectory { get; set; } = "";

    public MattingSettings Clone()
    {
        var clone = (MattingSettings)MemberwiseClone();
        clone.ProtectedPoints = new List<System.Windows.Point>(ProtectedPoints);
        clone.BrushStrokes = new List<NukkiStudio.Services.BrushStrokePoint>(BrushStrokes);
        clone.MagicWandPoints = new List<NukkiStudio.Services.MagicWandPoint>(MagicWandPoints);
        clone.LassoPolygons = LassoPolygons.Select(l => new NukkiStudio.Services.LassoPolygon(new List<System.Windows.Point>(l.Points), l.IsInclude)).ToList();
        return clone;
    }
}
