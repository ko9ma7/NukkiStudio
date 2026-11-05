using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace NukkiStudio.Services;

public class DialogService
{
    public string[]? OpenMediaFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = "영상 또는 이미지 파일 선택 (Select Media Files)",
            Multiselect = true,
            Filter = "모든 지원 미디어 (*.mp4,*.mov,*.png,*.jpg...)|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.wmv;*.png;*.jpg;*.jpeg;*.webp;*.bmp|동영상 파일 (*.mp4,*.mov,*.webm...)|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.wmv|이미지 파일 (*.png,*.jpg,*.webp...)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|모든 파일 (*.*)|*.*"
        };

        return dlg.ShowDialog() == true ? dlg.FileNames : null;
    }

    public string? OpenOnnxModelFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "ONNX AI 모델 파일 선택 (Select ONNX Model)",
            Filter = "ONNX Model (*.onnx)|*.onnx|All Files (*.*)|*.*"
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? OpenBackgroundImageFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "배경 이미지 선택 (Select Background Image)",
            Filter = "이미지 파일 (*.png,*.jpg,*.jpeg,*.webp,*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|모든 파일 (*.*)|*.*"
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? SelectOutputFolder(string? initialDirectory = null)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "저장 폴더 선택 (Select Export Folder)",
            InitialDirectory = initialDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public bool Confirm(string title, string message)
    {
        return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }
}
