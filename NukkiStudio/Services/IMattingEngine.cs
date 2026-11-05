using NukkiStudio.Models;

namespace NukkiStudio.Services;

public interface IMattingEngine
{
    string Name { get; }
    bool IsAvailable { get; }

    /// <summary>
    /// Processes in-place a raw 32-bit RGBA or BGRA frame buffer, modifying the Alpha channel (and RGB for spill suppression),
    /// or compositing onto a background if specified.
    /// </summary>
    void ProcessRgbaBuffer(
        byte[] buffer,
        int width,
        int height,
        MattingSettings settings,
        byte[]? backgroundBuffer = null
    );

    /// <summary>
    /// Extracts an 8-bit grayscale alpha mask array [width * height] (0 = background, 255 = foreground).
    /// </summary>
    byte[] ComputeAlphaMask(
        byte[] buffer,
        int width,
        int height,
        MattingSettings settings
    );
}
