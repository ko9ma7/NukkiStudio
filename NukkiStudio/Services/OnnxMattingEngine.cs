using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NukkiStudio.Models;

namespace NukkiStudio.Services;

public class OnnxMattingEngine : IMattingEngine, IDisposable
{
    private InferenceSession? _session;
    private string? _currentModelPath;
    private int _modelInputWidth = 512;
    private int _modelInputHeight = 512;
    private string _inputTensorName = "input";
    private string _outputTensorName = "output";
    private readonly object _lock = new();

    public string Name => "AI Neural Matting (ONNX)";
    public bool IsAvailable => _session != null;
    public string? LoadedModelPath => _currentModelPath;

    public void LoadModel(string modelPath, bool useGpu = false)
    {
        lock (_lock)
        {
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"ONNX Model not found at {modelPath}");

            _session?.Dispose();

            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                InterOpNumThreads = Environment.ProcessorCount,
                IntraOpNumThreads = Environment.ProcessorCount
            };

            // Attempt to load DirectML if requested and available
            if (useGpu)
            {
                try
                {
                    // If DirectML execution provider is available in environment
                    // sessionOptions.AppendExecutionProvider_DML(0);
                }
                catch
                {
                    // Fallback to CPU execution provider
                }
            }

            _session = new InferenceSession(modelPath, sessionOptions);
            _currentModelPath = modelPath;

            // Inspect input metadata
            var inputMeta = _session.InputMetadata.FirstOrDefault();
            _inputTensorName = inputMeta.Key;
            var dims = inputMeta.Value.Dimensions;

            // Most models have [batch, channels, height, width]
            if (dims != null && dims.Length >= 4)
            {
                _modelInputHeight = dims[2] > 0 ? dims[2] : 512;
                _modelInputWidth = dims[3] > 0 ? dims[3] : 512;
            }
            else
            {
                _modelInputWidth = 512;
                _modelInputHeight = 512;
            }

            var outputMeta = _session.OutputMetadata.FirstOrDefault();
            _outputTensorName = outputMeta.Key;
        }
    }

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

    public unsafe byte[] ComputeAlphaMask(
        byte[] buffer,
        int width,
        int height,
        MattingSettings settings)
    {
        lock (_lock)
        {
            if (_session == null)
            {
                throw new InvalidOperationException("ONNX AI Model is not loaded. Please select or download an AI model (.onnx).");
            }

            int inW = _modelInputWidth;
            int inH = _modelInputHeight;

            // Prepare DenseTensor<float> shape [1, 3, inH, inW]
            var tensor = new DenseTensor<float>(new[] { 1, 3, inH, inW });

            // Nearest-neighbor / bilinear sample input buffer to model dimensions
            float xRatio = (float)width / inW;
            float yRatio = (float)height / inH;

            bool isModnet = _currentModelPath?.Contains("modnet", StringComparison.OrdinalIgnoreCase) == true;

            fixed (byte* pBuf = buffer)
            {
                for (int y = 0; y < inH; y++)
                {
                    int srcY = Math.Clamp((int)(y * yRatio), 0, height - 1);
                    int srcRow = srcY * width * 4;

                    for (int x = 0; x < inW; x++)
                    {
                        int srcX = Math.Clamp((int)(x * xRatio), 0, width - 1);
                        int idx = srcRow + srcX * 4;

                        float r = pBuf[idx];
                        float g = pBuf[idx + 1];
                        float b = pBuf[idx + 2];

                        if (isModnet)
                        {
                            // MODNet normalization [-1, 1]
                            tensor[0, 0, y, x] = (r - 127.5f) / 127.5f;
                            tensor[0, 1, y, x] = (g - 127.5f) / 127.5f;
                            tensor[0, 2, y, x] = (b - 127.5f) / 127.5f;
                        }
                        else
                        {
                            // General RMBG / ImageNet normalization
                            tensor[0, 0, y, x] = ((r / 255.0f) - 0.485f) / 0.229f;
                            tensor[0, 1, y, x] = ((g / 255.0f) - 0.456f) / 0.224f;
                            tensor[0, 2, y, x] = ((b / 255.0f) - 0.406f) / 0.225f;
                        }
                    }
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputTensorName, tensor)
            };

            using var results = _session.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();

            // Rescale output mask [1, 1, inH, inW] back to [width * height]
            byte[] targetMask = new byte[width * height];
            float outXRatio = (float)inW / width;
            float outYRatio = (float)inH / height;

            fixed (byte* pMask = targetMask)
            {
                for (int y = 0; y < height; y++)
                {
                    int modY = Math.Clamp((int)(y * outYRatio), 0, inH - 1);
                    int targetRow = y * width;

                    for (int x = 0; x < width; x++)
                    {
                        int modX = Math.Clamp((int)(x * outXRatio), 0, inW - 1);
                        float val = outputTensor[0, 0, modY, modX];

                        // Clamp sigmoid probability [0, 1]
                        float alphaNorm = Math.Clamp(val, 0.0f, 1.0f);
                        pMask[targetRow + x] = (byte)(alphaNorm * 255.0f);
                    }
                }
            }

            // Apply Advanced Mask Post-Processing (ROI crop, Lasso, Magic Wand, Brush strokes, Erode/Dilate, Feather, Defringe)
            MaskPostProcessor.ApplyPostProcessing(targetMask, width, height, settings, buffer);

            return targetMask;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
