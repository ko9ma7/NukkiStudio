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
    AiNeural,    // AI Deep Learning Matting (U2Net, RMBG-1.4, etc. via ONNX) - 100% Offline & Free
    WebCloud,    // Web Cloud AI Matting (Remove.bg API, HuggingFace Inference API, etc.)
    ChromaKey    // Real-time green/blue screen keying with tolerance and spill suppression
}

public enum WebMattingProvider
{
    RemoveBg,       // remove.bg REST API (World standard for web e-commerce & photos)
    CustomEndpoint  // Custom REST API endpoint (HuggingFace / local rembg HTTP server)
}

public enum BackgroundType
{
    Transparent,   // Alpha channel preserved
    GreenScreen,   // #00FF00 chroma green
    CustomColor,   // Solid custom hex color
    Gradient,      // Modern studio radial/linear gradient
    BlurOriginal,  // Blur original background (DSLR cinematic portrait blur)
    CustomImage    // Composite onto custom background image
}

public enum VideoExportFormat
{
    ProRes4444_Mov,    // Apple ProRes 4444 (.mov) with transparent alpha - Premiere/DaVinci/FCP
    WebM_VP9,          // WebM VP9 (.webm) with transparent alpha - Web & video editors
    GreenScreen_Mp4,   // H.264 MP4 (.mp4) with clean #00FF00 green screen background
    CustomBg_Mp4,      // H.264 MP4 (.mp4) composited with custom background
    AlphaMask_Mp4,     // Grayscale matte video (.mp4)
    PngSequence,       // Folder of transparent PNG frames
    AnimatedGif        // Animated GIF with transparency - Discord / Web / Messenger
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
    ExcludeBrush,          // Paint background removal (Alpha = 0)
    MagicWandErase,        // Smart Magic Wand: click connected background to erase (Alpha = 0)
    MagicWandKeep,         // Smart Magic Wand: click connected subject region to keep (Alpha = 255)
    LassoInclude,          // Polygon / freehand lasso: surround subject to protect (Alpha = 255)
    LassoExclude           // Polygon / freehand lasso: surround region to delete (Alpha = 0)
}

public enum QuickPresetMode
{
    Custom,
    StudioFigurine,    // White/gray studio product & figurine (anti-halo, exterior choke, shadow remover)
    PortraitHair,      // Human portrait & hair matting (AI neural, soft edges, hair detail)
    GreenScreen,       // Video chroma green/blue screen
    GraphicLogo        // High-contrast clean logo/text
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
