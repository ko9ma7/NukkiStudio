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
            BackgroundType.Gradient when backgroundBuffer != null && backgroundBuffer.Length >= width * height * 4 => true,
            BackgroundType.BlurOriginal when backgroundBuffer != null && backgroundBuffer.Length >= width * height * 4 => true,
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

                    if (bgPtr != null)
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

        // Apply visual styling (Color grading, Sticker stroke, etc.)
        ImageProcessor.ApplySubjectEffects(buffer, width, height, settings);
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

            // 1. Bilinear downsampling of RGBA to model dimensions [inW, inH]
            fixed (byte* pBuf = buffer)
            {
                for (int y = 0; y < inH; y++)
                {
                    float srcY = y * yRatio;
                    int y0 = Math.Clamp((int)srcY, 0, height - 1);
                    int y1 = Math.Clamp(y0 + 1, 0, height - 1);
                    float wy = srcY - y0;

                    int row0 = y0 * width * 4;
                    int row1 = y1 * width * 4;

                    for (int x = 0; x < inW; x++)
                    {
                        float srcX = x * xRatio;
                        int x0 = Math.Clamp((int)srcX, 0, width - 1);
                        int x1 = Math.Clamp(x0 + 1, 0, width - 1);
                        float wx = srcX - x0;

                        int idx00 = row0 + x0 * 4;
                        int idx10 = row0 + x1 * 4;
                        int idx01 = row1 + x0 * 4;
                        int idx11 = row1 + x1 * 4;

                        float r = (pBuf[idx00] * (1 - wx) + pBuf[idx10] * wx) * (1 - wy) +
                                  (pBuf[idx01] * (1 - wx) + pBuf[idx11] * wx) * wy;
                        float g = (pBuf[idx00 + 1] * (1 - wx) + pBuf[idx10 + 1] * wx) * (1 - wy) +
                                  (pBuf[idx01 + 1] * (1 - wx) + pBuf[idx11 + 1] * wx) * wy;
                        float b = (pBuf[idx00 + 2] * (1 - wx) + pBuf[idx10 + 2] * wx) * (1 - wy) +
                                  (pBuf[idx01 + 2] * (1 - wx) + pBuf[idx11 + 2] * wx) * wy;

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

            // 2. High-Quality Bilinear upsampling of output mask to original dimensions
            byte[] targetMask = new byte[width * height];
            float outXRatio = (float)inW / width;
            float outYRatio = (float)inH / height;

            fixed (byte* pMask = targetMask)
            {
                for (int y = 0; y < height; y++)
                {
                    float srcY = y * outYRatio;
                    int y0 = Math.Clamp((int)srcY, 0, inH - 1);
                    int y1 = Math.Clamp(y0 + 1, 0, inH - 1);
                    float wy = srcY - y0;
                    int targetRow = y * width;

                    for (int x = 0; x < width; x++)
                    {
                        float srcX = x * outXRatio;
                        int x0 = Math.Clamp((int)srcX, 0, inW - 1);
                        int x1 = Math.Clamp(x0 + 1, 0, inW - 1);
                        float wx = srcX - x0;

                        float v00 = outputTensor[0, 0, y0, x0];
                        float v10 = outputTensor[0, 0, y0, x1];
                        float v01 = outputTensor[0, 0, y1, x0];
                        float v11 = outputTensor[0, 0, y1, x1];

                        float val = (v00 * (1 - wx) + v10 * wx) * (1 - wy) +
                                    (v01 * (1 - wx) + v11 * wx) * wy;

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
