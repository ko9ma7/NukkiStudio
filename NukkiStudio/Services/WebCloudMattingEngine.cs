using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using NukkiStudio.Models;

namespace NukkiStudio.Services;

public class WebCloudMattingEngine : IMattingEngine
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    public string Name => "Web Cloud AI (remove.bg / Web API)";
    public bool IsAvailable => true;

    public unsafe void ProcessRgbaBuffer(
        byte[] buffer,
        int width,
        int height,
        MattingSettings settings,
        byte[]? backgroundBuffer = null)
    {
        var mask = ComputeAlphaMask(buffer, width, height, settings);

        bool hasCustomBg = settings.BgType switch
        {
            BackgroundType.GreenScreen => true,
            BackgroundType.CustomColor => true,
            BackgroundType.CustomImage when backgroundBuffer != null && backgroundBuffer.Length >= width * height * 4 => true,
            _ => false
        };

        byte bgR = settings.BgType switch
        {
            BackgroundType.GreenScreen => 0,
            BackgroundType.CustomColor => settings.CustomBgColor.R,
            _ => 0
        };
        byte bgG = settings.BgType switch
        {
            BackgroundType.GreenScreen => 255,
            BackgroundType.CustomColor => settings.CustomBgColor.G,
            _ => 0
        };
        byte bgB = settings.BgType switch
        {
            BackgroundType.GreenScreen => 0,
            BackgroundType.CustomColor => settings.CustomBgColor.B,
            _ => 0
        };

        int totalPixels = width * height;
        fixed (byte* pBuf = buffer)
        fixed (byte* pMask = mask)
        fixed (byte* pCustomBg = backgroundBuffer)
        {
            byte* ptr = pBuf;
            byte* mPtr = pMask;
            byte* bgPtr = pCustomBg;

            for (int i = 0; i < totalPixels; i++, ptr += 4, mPtr++)
            {
                byte alpha = *mPtr;

                if (!hasCustomBg)
                {
                    ptr[3] = alpha;
                }
                else
                {
                    byte r = ptr[0];
                    byte g = ptr[1];
                    byte b = ptr[2];

                    byte targetBgR = bgR;
                    byte targetBgG = bgG;
                    byte targetBgB = bgB;

                    if (settings.BgType == BackgroundType.CustomImage && bgPtr != null)
                    {
                        targetBgR = bgPtr[i * 4];
                        targetBgG = bgPtr[i * 4 + 1];
                        targetBgB = bgPtr[i * 4 + 2];
                    }

                    float aNorm = alpha / 255.0f;
                    float invA = 1.0f - aNorm;

                    ptr[0] = (byte)Math.Clamp((int)(r * aNorm + targetBgR * invA), 0, 255);
                    ptr[1] = (byte)Math.Clamp((int)(g * aNorm + targetBgG * invA), 0, 255);
                    ptr[2] = (byte)Math.Clamp((int)(b * aNorm + targetBgB * invA), 0, 255);
                    ptr[3] = 255;
                }
            }
        }
    }

    public byte[] ComputeAlphaMask(
        byte[] buffer,
        int width,
        int height,
        MattingSettings settings)
    {
        // Run sync wrapper for IMattingEngine contract
        return Task.Run(() => ComputeAlphaMaskAsync(buffer, width, height, settings)).GetAwaiter().GetResult();
    }

    public async Task<byte[]> ComputeAlphaMaskAsync(
        byte[] buffer,
        int width,
        int height,
        MattingSettings settings)
    {
        if (settings.WebProvider == WebMattingProvider.RemoveBg && string.IsNullOrWhiteSpace(settings.WebApiKey))
        {
            throw new InvalidOperationException("Remove.bg API 키가 등록되지 않았습니다. 우측 패널에서 무료 API 키를 등록하거나 [🤖 스마트 AI 자동 누끼] 모드를 선택하세요.");
        }

        // 1. Encode RGBA buffer to PNG in memory
        byte[] imageBytes = EncodeRgbaToPng(buffer, width, height);

        // 2. Dispatch to cloud provider
        byte[] resultPngBytes;
        if (settings.WebProvider == WebMattingProvider.RemoveBg)
        {
            resultPngBytes = await CallRemoveBgApiAsync(imageBytes, settings.WebApiKey);
        }
        else
        {
            string endpoint = string.IsNullOrWhiteSpace(settings.WebEndpointUrl)
                ? "https://api.remove.bg/v1.0/removebg"
                : settings.WebEndpointUrl;
            resultPngBytes = await CallCustomEndpointAsync(imageBytes, endpoint, settings.WebApiKey);
        }

        // 3. Decode returned transparent PNG to extract alpha mask
        byte[] mask = ExtractAlphaFromPng(resultPngBytes, width, height);

        // 4. Apply fine-tuning post processing
        MaskPostProcessor.ApplyPostProcessing(mask, width, height, settings, buffer);

        return mask;
    }

    private static async Task<byte[]> CallRemoveBgApiAsync(byte[] imageBytes, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.remove.bg/v1.0/removebg");
        request.Headers.Add("X-Api-Key", apiKey.Trim());

        using var content = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(imageBytes);
        byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(byteContent, "image_file", "source.png");
        content.Add(new StringContent("auto"), "size");
        content.Add(new StringContent("png"), "format");
        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Remove.bg API 요청 실패 (상태 코드 {response.StatusCode}): {err}");
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    private static async Task<byte[]> CallCustomEndpointAsync(byte[] imageBytes, string endpointUrl, string? apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Add("Authorization", $"Bearer {apiKey.Trim()}");
        }

        using var content = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(imageBytes);
        byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(byteContent, "image_file", "source.png");
        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"웹 누끼 엔드포인트 요청 실패 ({response.StatusCode}): {err}");
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    private static byte[] EncodeRgbaToPng(byte[] buffer, int width, int height)
    {
        // WPF uses BGRA32, swap RGBA to BGRA for encoder
        byte[] bgra = new byte[buffer.Length];
        for (int i = 0; i < buffer.Length; i += 4)
        {
            bgra[i] = buffer[i + 2];     // B
            bgra[i + 1] = buffer[i + 1]; // G
            bgra[i + 2] = buffer[i];     // R
            bgra[i + 3] = buffer[i + 3]; // A
        }

        var bmp = ImageProcessor.CreateBitmapFromPixels(bgra, width, height);
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static byte[] ExtractAlphaFromPng(byte[] pngBytes, int targetW, int targetH)
    {
        using var ms = new MemoryStream(pngBytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        int srcW = frame.PixelWidth;
        int srcH = frame.PixelHeight;

        // Convert to Bgra32 if not already
        var formatted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int stride = srcW * 4;
        byte[] pixels = new byte[srcH * stride];
        formatted.CopyPixels(pixels, stride, 0);

        byte[] mask = new byte[targetW * targetH];

        // Bilinear resample alpha channel to target dimensions
        float xRatio = (float)srcW / targetW;
        float yRatio = (float)srcH / targetH;

        for (int y = 0; y < targetH; y++)
        {
            float srcY = y * yRatio;
            int y0 = Math.Clamp((int)srcY, 0, srcH - 1);
            int y1 = Math.Clamp(y0 + 1, 0, srcH - 1);
            float wy = srcY - y0;
            int targetRow = y * targetW;

            for (int x = 0; x < targetW; x++)
            {
                float srcX = x * xRatio;
                int x0 = Math.Clamp((int)srcX, 0, srcW - 1);
                int x1 = Math.Clamp(x0 + 1, 0, srcW - 1);
                float wx = srcX - x0;

                // Alpha is index 3 in Bgra32
                byte a00 = pixels[(y0 * srcW + x0) * 4 + 3];
                byte a10 = pixels[(y0 * srcW + x1) * 4 + 3];
                byte a01 = pixels[(y1 * srcW + x0) * 4 + 3];
                byte a11 = pixels[(y1 * srcW + x1) * 4 + 3];

                float val = (a00 * (1 - wx) + a10 * wx) * (1 - wy) +
                            (a01 * (1 - wx) + a11 * wx) * wy;

                mask[targetRow + x] = (byte)Math.Clamp((int)Math.Round(val), 0, 255);
            }
        }

        return mask;
    }
}
