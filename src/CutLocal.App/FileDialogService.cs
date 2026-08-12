using System.IO;
using System.Windows;
using CutLocal.Domain;
using Microsoft.Win32;

namespace CutLocal.App;

/// <summary>Uses the Windows file dialog for the current PNG slice.</summary>
public sealed class FileDialogService : IFileDialogService
{
    /// <inheritdoc />
    public string? SelectPng()
    {
        OpenFileDialog dialog = new()
        {
            Title = "CutLocal — PNG seç / Select PNG",
            Filter = "PNG image (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SelectPngFiles()
    {
        OpenFileDialog dialog = new()
        {
            Title = "CutLocal — PNG dosyaları / PNG files",
            Filter = "PNG image (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = true,
            DereferenceLinks = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    /// <inheritdoc />
    public string? SelectInputFolder()
    {
        OpenFolderDialog dialog = new()
        {
            Title = "CutLocal — Girdi klasörü / Input folder",
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <inheritdoc />
    public string? SelectOutputFolder(string? initialDirectory)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "CutLocal — Çıktı klasörü / Output folder",
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <inheritdoc />
    public string? SelectOnnxModel()
    {
        OpenFileDialog dialog = new()
        {
            Title = "CutLocal — Yerel ONNX / Local ONNX",
            Filter = "ONNX model (*.onnx)|*.onnx",
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? SelectModelManifest()
    {
        OpenFileDialog dialog = new()
        {
            Title = "CutLocal — Model manifesti / Model manifest",
            Filter = "JSON manifest (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public bool ConfirmCustomModelLicense() => MessageBox.Show(
        "Bu modelin lisansını ve kullanım haklarını doğruladığımı kabul ediyorum.\n\n"
        + "I confirm that I verified this model's license and usage rights.",
        "CutLocal — Custom ONNX",
        MessageBoxButton.OKCancel,
        MessageBoxImage.Warning) == MessageBoxResult.OK;

    /// <inheritdoc />
    public bool ConfirmRestrictedModelLicense(ModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return MessageBox.Show(
            $"{descriptor.DisplayName}\n\n"
            + $"Lisans / License: {descriptor.License.Spdx}\n"
            + "Bu model yalnızca ticari olmayan kullanım içindir ve atıf gerektirir.\n"
            + "This model is for non-commercial use only and requires attribution.\n\n"
            + $"Kaynak / Source: {descriptor.License.Source}\n\n"
            + "İndirerek modeli yalnızca ticari olmayan amaçlarla kullanacağımı kabul ediyorum.\n"
            + "By downloading, I agree to use the model only for non-commercial purposes.",
            "CutLocal — Restricted model license",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning) == MessageBoxResult.OK;
    }
}
