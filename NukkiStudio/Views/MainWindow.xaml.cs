using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NukkiStudio.Models;
using NukkiStudio.ViewModels;

namespace NukkiStudio.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private bool _isDraggingRoi;
    private Point _roiStartCanvasPos;
    private Point _roiStartPixelPos;

    private bool _isPaintingBrush;
    private Point _lastPaintedPixelPos;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSplitClip();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                ViewModel.AddFilesInternal(files);
            }
        }
    }

    private void QueueItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is MediaItem item)
        {
            ViewModel.SelectedItem = item;
        }
    }

    private void SplitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSplitClip();
    }

    private void PreviewContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSplitClip();
    }

    private void UpdateSplitClip()
    {
        if (PreviewContainer == null || SplitClipGeometry == null || SplitDividerLine == null)
            return;

        double w = PreviewContainer.ActualWidth;
        double h = PreviewContainer.ActualHeight;

        if (w <= 0 || h <= 0) return;

        double splitX = w * (ViewModel.SplitPosition / 100.0);

        // Clip original image so only the left portion shows
        SplitClipGeometry.Rect = new Rect(0, 0, splitX, h);

        // Position the split indicator line
        SplitDividerLine.X1 = splitX;
        SplitDividerLine.Y1 = 0;
        SplitDividerLine.X2 = splitX;
        SplitDividerLine.Y2 = h;

        // Position confirmed ROI rectangle if enabled
        UpdateRoiVisualRect();
    }

    private void UpdateRoiVisualRect()
    {
        if (RoiVisualRect == null) return;

        if (ViewModel.EnableRoi && ViewModel.RoiRect.HasValue)
        {
            var canvasRect = ImageToCanvasRect(ViewModel.RoiRect.Value);
            if (!canvasRect.IsEmpty && canvasRect.Width > 0 && canvasRect.Height > 0)
            {
                Canvas.SetLeft(RoiVisualRect, canvasRect.Left);
                Canvas.SetTop(RoiVisualRect, canvasRect.Top);
                RoiVisualRect.Width = canvasRect.Width;
                RoiVisualRect.Height = canvasRect.Height;
                RoiVisualRect.Visibility = Visibility.Visible;
                return;
            }
        }

        RoiVisualRect.Visibility = Visibility.Collapsed;
    }

    private bool TryGetImageCoordinates(Point canvasPos, out double pixelX, out double pixelY, out double scale)
    {
        pixelX = 0;
        pixelY = 0;
        scale = 1.0;

        if (ViewModel.OriginalPreviewImage == null || PreviewContainer == null) return false;

        double containerW = PreviewContainer.ActualWidth;
        double containerH = PreviewContainer.ActualHeight;
        double imgW = ViewModel.OriginalPreviewImage.PixelWidth;
        double imgH = ViewModel.OriginalPreviewImage.PixelHeight;

        if (containerW <= 0 || containerH <= 0 || imgW <= 0 || imgH <= 0) return false;

        scale = Math.Min(containerW / imgW, containerH / imgH);
        double renderW = imgW * scale;
        double renderH = imgH * scale;
        double offsetX = (containerW - renderW) / 2.0;
        double offsetY = (containerH - renderH) / 2.0;

        if (canvasPos.X < offsetX || canvasPos.X > offsetX + renderW ||
            canvasPos.Y < offsetY || canvasPos.Y > offsetY + renderH)
        {
            return false;
        }

        pixelX = Math.Clamp((canvasPos.X - offsetX) / scale, 0, imgW - 1);
        pixelY = Math.Clamp((canvasPos.Y - offsetY) / scale, 0, imgH - 1);
        return true;
    }

    private Rect ImageToCanvasRect(Rect imgRect)
    {
        if (ViewModel.OriginalPreviewImage == null || PreviewContainer == null) return Rect.Empty;

        double containerW = PreviewContainer.ActualWidth;
        double containerH = PreviewContainer.ActualHeight;
        double imgW = ViewModel.OriginalPreviewImage.PixelWidth;
        double imgH = ViewModel.OriginalPreviewImage.PixelHeight;

        if (containerW <= 0 || containerH <= 0 || imgW <= 0 || imgH <= 0) return Rect.Empty;

        double scale = Math.Min(containerW / imgW, containerH / imgH);
        double offsetX = (containerW - imgW * scale) / 2.0;
        double offsetY = (containerH - imgH * scale) / 2.0;

        return new Rect(
            offsetX + imgRect.X * scale,
            offsetY + imgRect.Y * scale,
            imgRect.Width * scale,
            imgRect.Height * scale
        );
    }

    private void OverlayCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var canvasPos = e.GetPosition(OverlayCanvas);

        if (!TryGetImageCoordinates(canvasPos, out double pixelX, out double pixelY, out _))
            return;

        // 1. Color Picker Eyedroppers
        if (ViewModel.ActiveTool == MattingToolMode.ColorPicker ||
            ViewModel.ActiveTool == MattingToolMode.SecondaryColorPicker ||
            ViewModel.IsColorPickerActive ||
            ViewModel.IsProtectPickerActive)
        {
            ViewModel.SampleColorFromPreview((int)pixelX, (int)pixelY);
            return;
        }

        // 2. ROI Box Mode
        if (ViewModel.ActiveTool == MattingToolMode.RoiBox)
        {
            _isDraggingRoi = true;
            _roiStartCanvasPos = canvasPos;
            _roiStartPixelPos = new Point(pixelX, pixelY);

            Canvas.SetLeft(DragRoiVisualRect, canvasPos.X);
            Canvas.SetTop(DragRoiVisualRect, canvasPos.Y);
            DragRoiVisualRect.Width = 0;
            DragRoiVisualRect.Height = 0;
            DragRoiVisualRect.Visibility = Visibility.Visible;
            OverlayCanvas.CaptureMouse();
            return;
        }

        // 3. Include / Exclude Brush Mode
        if (ViewModel.ActiveTool == MattingToolMode.IncludeBrush || ViewModel.ActiveTool == MattingToolMode.ExcludeBrush)
        {
            _isPaintingBrush = true;
            _lastPaintedPixelPos = new Point(pixelX, pixelY);
            bool isInclude = (ViewModel.ActiveTool == MattingToolMode.IncludeBrush);
            ViewModel.AddBrushStroke(pixelX, pixelY, isInclude);
            OverlayCanvas.CaptureMouse();
        }
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var canvasPos = e.GetPosition(OverlayCanvas);
        bool insideImage = TryGetImageCoordinates(canvasPos, out double pixelX, out double pixelY, out double scale);

        // Update cursor indicator
        if (insideImage && (ViewModel.ActiveTool == MattingToolMode.IncludeBrush || ViewModel.ActiveTool == MattingToolMode.ExcludeBrush))
        {
            double brushScreenDiameter = Math.Max(6, ViewModel.BrushSize * 2.0 * scale);
            BrushCursorIndicator.Width = brushScreenDiameter;
            BrushCursorIndicator.Height = brushScreenDiameter;
            BrushCursorIndicator.Stroke = (ViewModel.ActiveTool == MattingToolMode.IncludeBrush)
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) // Emerald Green
                : new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Coral Red

            Canvas.SetLeft(BrushCursorIndicator, canvasPos.X - brushScreenDiameter / 2.0);
            Canvas.SetTop(BrushCursorIndicator, canvasPos.Y - brushScreenDiameter / 2.0);
            BrushCursorIndicator.Visibility = Visibility.Visible;
        }
        else
        {
            BrushCursorIndicator.Visibility = Visibility.Collapsed;
        }

        // Handle Active Dragging for ROI
        if (_isDraggingRoi)
        {
            double left = Math.Min(_roiStartCanvasPos.X, canvasPos.X);
            double top = Math.Min(_roiStartCanvasPos.Y, canvasPos.Y);
            double width = Math.Abs(canvasPos.X - _roiStartCanvasPos.X);
            double height = Math.Abs(canvasPos.Y - _roiStartCanvasPos.Y);

            Canvas.SetLeft(DragRoiVisualRect, left);
            Canvas.SetTop(DragRoiVisualRect, top);
            DragRoiVisualRect.Width = width;
            DragRoiVisualRect.Height = height;
            return;
        }

        // Handle Active Painting for Brushes
        if (_isPaintingBrush && insideImage)
        {
            double dx = pixelX - _lastPaintedPixelPos.X;
            double dy = pixelY - _lastPaintedPixelPos.Y;
            double distSq = dx * dx + dy * dy;
            double minSpacing = Math.Max(4.0, ViewModel.BrushSize * 0.4);

            // Interpolate / stamp if mouse moved enough
            if (distSq >= minSpacing * minSpacing)
            {
                _lastPaintedPixelPos = new Point(pixelX, pixelY);
                bool isInclude = (ViewModel.ActiveTool == MattingToolMode.IncludeBrush);
                ViewModel.AddBrushStroke(pixelX, pixelY, isInclude);
            }
        }
    }

    private void OverlayCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingRoi)
        {
            _isDraggingRoi = false;
            DragRoiVisualRect.Visibility = Visibility.Collapsed;
            OverlayCanvas.ReleaseMouseCapture();

            var canvasPos = e.GetPosition(OverlayCanvas);
            TryGetImageCoordinates(canvasPos, out double endPixelX, out double endPixelY, out _);

            double minX = Math.Min(_roiStartPixelPos.X, endPixelX);
            double minY = Math.Min(_roiStartPixelPos.Y, endPixelY);
            double roiW = Math.Abs(endPixelX - _roiStartPixelPos.X);
            double roiH = Math.Abs(endPixelY - _roiStartPixelPos.Y);

            if (roiW > 5 && roiH > 5)
            {
                var roi = new Rect(minX, minY, roiW, roiH);
                ViewModel.SetRoi(roi);
                UpdateRoiVisualRect();
            }
            return;
        }

        if (_isPaintingBrush)
        {
            _isPaintingBrush = false;
            OverlayCanvas.ReleaseMouseCapture();
        }
    }

    private void OverlayCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        BrushCursorIndicator.Visibility = Visibility.Collapsed;
    }

    private void GreenPreset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.KeyColor = Color.FromRgb(0, 255, 0);
    }

    private void BluePreset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.KeyColor = Color.FromRgb(0, 71, 187); // Cobalt / Chroma Blue
    }

    private void WhitePreset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.KeyColor = Color.FromRgb(255, 255, 255);
    }
}
