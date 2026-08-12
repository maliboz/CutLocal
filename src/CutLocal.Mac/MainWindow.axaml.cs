using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace CutLocal.Mac;

public sealed partial class MainWindow : Window
{
    private static readonly FilePickerFileType PngFileType = new("PNG image")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
    };

    private readonly MacMainWindowViewModel _viewModel = null!;
    private bool _initialized;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    internal MainWindow(MacMainWindowViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            _viewModel.ReportUiError($"Model kataloğu yüklenemedi: {exception.Message}");
        }
    }

    private void OnClosed(object? sender, EventArgs e) => _viewModel.Dispose();

    private async void OnSelectImageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "İşlenecek PNG görselini seç",
                    AllowMultiple = false,
                    FileTypeFilter = [PngFileType],
                });
            if (files.Count == 0)
            {
                return;
            }

            await _viewModel.SetInputAsync(files[0].Path.LocalPath);
        }
        catch (Exception exception)
        {
            _viewModel.ReportUiError($"Görsel açılamadı: {exception.Message}");
        }
    }

    private async void OnSelectOutputClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "Şeffaf PNG çıktısını kaydet",
                    DefaultExtension = "png",
                    SuggestedFileName = _viewModel.SuggestedOutputName,
                    FileTypeChoices = [PngFileType],
                    ShowOverwritePrompt = true,
                });
            if (file is not null)
            {
                _viewModel.SetOutputPath(file.Path.LocalPath);
            }
        }
        catch (Exception exception)
        {
            _viewModel.ReportUiError($"Çıktı yolu seçilemedi: {exception.Message}");
        }
    }

    private async void OnProcessClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.ProcessAsync();
        }
        catch (Exception exception)
        {
            _viewModel.ReportUiError($"İşlem başlatılamadı: {exception.Message}");
        }
    }

    private async void OnDownloadModelClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.DownloadSelectedModelAsync();
        }
        catch (Exception exception)
        {
            _viewModel.ReportUiError($"Model işlemi başarısız oldu: {exception.Message}");
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _viewModel.Cancel();

    private void OnRevealOutputClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.RevealOutput();
        }
        catch (Exception exception)
        {
            _viewModel.ReportUiError($"Finder açılamadı: {exception.Message}");
        }
    }
}
