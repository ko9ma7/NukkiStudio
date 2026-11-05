using NukkiStudio.Services;
using Xunit;

namespace NukkiStudio.Tests;

public class ImageProcessorTests
{
    [Fact]
    public void SwapRedBlue_CorrectlyExchangesRedAndBlueChannels()
    {
        // 1 pixel RGBA: R=200, G=100, B=50, A=255
        byte[] buffer = new byte[] { 200, 100, 50, 255 };

        ImageProcessor.SwapRedBlue(buffer, 1, 1);

        // After swap (BGRA): B=200, G=100, R=50, A=255
        Assert.Equal(50, buffer[0]);
        Assert.Equal(100, buffer[1]);
        Assert.Equal(200, buffer[2]);
        Assert.Equal(255, buffer[3]);

        // Swap back to RGBA
        ImageProcessor.SwapRedBlue(buffer, 1, 1);
        Assert.Equal(200, buffer[0]);
        Assert.Equal(100, buffer[1]);
        Assert.Equal(50, buffer[2]);
        Assert.Equal(255, buffer[3]);
    }
}
