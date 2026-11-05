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
    }

    private void OriginalPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((!ViewModel.IsColorPickerActive && !ViewModel.IsProtectPickerActive) || ViewModel.OriginalPreviewImage == null)
            return;

        var pos = e.GetPosition(OriginalPreviewElement);
        double actualW = OriginalPreviewElement.ActualWidth;
        double actualH = OriginalPreviewElement.ActualHeight;

        if (actualW <= 0 || actualH <= 0) return;

        int imgW = ViewModel.OriginalPreviewImage.PixelWidth;
        int imgH = ViewModel.OriginalPreviewImage.PixelHeight;

        int pixelX = (int)((pos.X / actualW) * imgW);
        int pixelY = (int)((pos.Y / actualH) * imgH);

        ViewModel.SampleColorFromPreview(pixelX, pixelY);
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
