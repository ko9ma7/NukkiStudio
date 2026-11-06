namespace NukkiStudio.Models;

public enum MediaType
{
    Image,
    Video
}

public enum ProcessingStatus
{
    Pending,
    Processing,
    Completed,
    Cancelled,
    Failed
}

public enum MattingMode
{
    ChromaKey,   // Real-time green/blue screen keying with tolerance and spill suppression
    AiNeural     // AI Deep Learning Matting (RMBG-1.4, MODNet, U2Net via ONNX)
}

public enum BackgroundType
{
    Transparent,  // Alpha channel preserved
    GreenScreen,  // #00FF00 chroma green
    CustomColor,  // Solid custom hex color
    CustomImage   // Composite onto custom background image
}

public enum VideoExportFormat
{
    ProRes4444_Mov,    // Apple ProRes 4444 (.mov) with transparent alpha - Premiere/DaVinci/FCP
    WebM_VP9,          // WebM VP9 (.webm) with transparent alpha - Web & video editors
    GreenScreen_Mp4,   // H.264 MP4 (.mp4) with clean #00FF00 green screen background
    CustomBg_Mp4,      // H.264 MP4 (.mp4) composited with custom background
    AlphaMask_Mp4,     // Grayscale matte video (.mp4)
    PngSequence        // Folder of transparent PNG frames
}

public enum ImageExportFormat
{
    PngTransparent,    // 32-bit ARGB PNG
    WebpTransparent,   // Lossless Transparent WebP
    JpgComposite,      // JPG with solid background
    AlphaMaskPng       // 8-bit Grayscale Alpha Matte PNG
}

public enum MattingToolMode
{
    None,
    ColorPicker,           // Primary background key color eyedropper
    SecondaryColorPicker,  // Secondary / shadow key color eyedropper
    RoiBox,                // Crop / isolate subject bounding box
    IncludeBrush,          // Paint foreground (Alpha = 255)
    ExcludeBrush           // Paint background removal (Alpha = 0)
}

public record VideoMetadata(
    int Width,
    int Height,
    double DurationSeconds,
    double Fps,
    long TotalFrames,
    bool HasAudio,
    string Codec
);
