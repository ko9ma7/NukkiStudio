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

    // AI Neural parameters
    public string SelectedModelPath { get; set; } = "";
    public bool UseGpuAcceleration { get; set; } = true;

    // Export formats
    public VideoExportFormat VideoFormat { get; set; } = VideoExportFormat.ProRes4444_Mov;
    public ImageExportFormat ImageFormat { get; set; } = ImageExportFormat.PngTransparent;

    // Video output parameters
    public int OutputWidth { get; set; } = 0; // 0 = Keep original
    public int OutputHeight { get; set; } = 0;
    public double OutputFps { get; set; } = 0; // 0 = Keep original
    public bool PreserveAudio { get; set; } = true;
    public string OutputDirectory { get; set; } = "";

    public MattingSettings Clone()
    {
        return (MattingSettings)MemberwiseClone();
    }
}
