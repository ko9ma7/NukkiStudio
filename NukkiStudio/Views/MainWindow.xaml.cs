using System;
using System.Collections.Generic;
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

    private bool _isPanning;
    private Point _panStartScreenPos;
    private double _panInitialOffsetX;
    private double _panInitialOffsetY;

    private bool _isDrawingLasso;
    private readonly List<Point> _currentLassoPoints = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSplitClip();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                if (ViewModel.CanRedo) ViewModel.Redo();
            }
            else
            {
                if (ViewModel.CanUndo) ViewModel.Undo();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (ViewModel.CanRedo) ViewModel.Redo();
            e.Handled = true;
        }
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

        // Hide divider line if split is at 0 (full cutout) or if ShowSplitCompare is false
        SplitDividerLine.Visibility = (ViewModel.ShowSplitCompare && ViewModel.SplitPosition > 0 && ViewModel.SplitPosition < 100)
            ? Visibility.Visible
            : Visibility.Collapsed;

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
        // 0. Pan Handling (Middle Click OR Left Click while Spacebar held down)
        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
        {
            _isPanning = true;
            _panStartScreenPos = e.GetPosition(this);
            _panInitialOffsetX = ViewModel.PanOffsetX;
            _panInitialOffsetY = ViewModel.PanOffsetY;
            OverlayCanvas.CaptureMouse();
            Cursor = Cursors.SizeAll;
            return;
        }

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

        // 3. Magic Wand Mode (1-Click Smart Edge-Aware Keep or Erase)
        if (ViewModel.ActiveTool == MattingToolMode.MagicWandErase || ViewModel.ActiveTool == MattingToolMode.MagicWandKeep)
        {
            bool isErase = (ViewModel.ActiveTool == MattingToolMode.MagicWandErase);
            ViewModel.AddMagicWandPoint(pixelX, pixelY, isErase);
            return;
        }

        // 4. Lasso Polygon Mode (Surround region to include or exclude)
        if (ViewModel.ActiveTool == MattingToolMode.LassoInclude || ViewModel.ActiveTool == MattingToolMode.LassoExclude)
        {
            _isDrawingLasso = true;
            _currentLassoPoints.Clear();
            _currentLassoPoints.Add(new Point(pixelX, pixelY));
            ActiveLassoVisual.Points.Clear();
            ActiveLassoVisual.Points.Add(canvasPos);
            ActiveLassoVisual.Stroke = (ViewModel.ActiveTool == MattingToolMode.LassoInclude)
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) // Green
                : new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Red
            ActiveLassoVisual.Visibility = Visibility.Visible;
            OverlayCanvas.CaptureMouse();
            return;
        }

        // 5. Include / Exclude Brush Mode
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
        // 0. Active Pan dragging
        if (_isPanning)
        {
            var curScreenPos = e.GetPosition(this);
            ViewModel.PanOffsetX = _panInitialOffsetX + (curScreenPos.X - _panStartScreenPos.X);
            ViewModel.PanOffsetY = _panInitialOffsetY + (curScreenPos.Y - _panStartScreenPos.Y);
            return;
        }

        var canvasPos = e.GetPosition(OverlayCanvas);
        bool insideImage = TryGetImageCoordinates(canvasPos, out double pixelX, out double pixelY, out double scale);

        // Update cursor indicator for brushes
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

        // Handle Active Lasso Polygon outline drawing
        if (_isDrawingLasso && insideImage)
        {
            if (_currentLassoPoints.Count > 0)
            {
                var lastPt = _currentLassoPoints[^1];
                double dx = pixelX - lastPt.X;
                double dy = pixelY - lastPt.Y;
                if (dx * dx + dy * dy >= 9.0) // Sample every ~3 pixels for smooth curves
                {
                    _currentLassoPoints.Add(new Point(pixelX, pixelY));
                    ActiveLassoVisual.Points.Add(canvasPos);
                }
            }
            return;
        }

        // Handle Active Continuous Painting for Brushes (Interpolate steps to eliminate gaps)
        if (_isPaintingBrush && insideImage)
        {
            double dx = pixelX - _lastPaintedPixelPos.X;
            double dy = pixelY - _lastPaintedPixelPos.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double step = Math.Max(2.0, ViewModel.BrushSize * 0.35);

            if (dist >= step)
            {
                int steps = (int)Math.Ceiling(dist / step);
                bool isInclude = (ViewModel.ActiveTool == MattingToolMode.IncludeBrush);
                for (int s = 1; s <= steps; s++)
                {
                    double t = (double)s / steps;
                    double interpX = _lastPaintedPixelPos.X + dx * t;
                    double interpY = _lastPaintedPixelPos.Y + dy * t;
                    ViewModel.AddBrushStroke(interpX, interpY, isInclude);
                }
                _lastPaintedPixelPos = new Point(pixelX, pixelY);
            }
        }
    }

    private void OverlayCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // 0. End Pan
        if (_isPanning)
        {
            _isPanning = false;
            OverlayCanvas.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            return;
        }

        // 1. End ROI Drag
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

        // 2. End Lasso
        if (_isDrawingLasso)
        {
            _isDrawingLasso = false;
            OverlayCanvas.ReleaseMouseCapture();
            ActiveLassoVisual.Visibility = Visibility.Collapsed;

            if (_currentLassoPoints.Count >= 3)
            {
                bool isInclude = (ViewModel.ActiveTool == MattingToolMode.LassoInclude);
                ViewModel.AddLassoPolygon(_currentLassoPoints, isInclude);
            }
            _currentLassoPoints.Clear();
            ActiveLassoVisual.Points.Clear();
            return;
        }

        // 3. End Brush
        if (_isPaintingBrush)
        {
            _isPaintingBrush = false;
            OverlayCanvas.ReleaseMouseCapture();
        }
    }

    private void OverlayCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
        {
            ViewModel.ZoomIn();
        }
        else if (e.Delta < 0)
        {
            ViewModel.ZoomOut();
        }
        e.Handled = true;
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
