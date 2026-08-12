using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Mac;

internal sealed class MacMainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly RemoveBackgroundUseCase _removeBackground;
    private readonly ModelManagementUseCase _modelManagement;
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly IBundledModelSeeder _bundledModelSeeder;
    private CancellationTokenSource? _operationCancellation;
    private ModelChoice? _selectedModel;
    private Bitmap? _inputPreview;
    private Bitmap? _outputPreview;
    private string? _inputPath;
    private string? _outputPath;
    private string? _lastOutputPath;
    private string _statusText = "Hazır — görsel seçerek başlayabilirsin.";
    private double _threshold = 0.5;
    private double _featherRadius = 1.0;
    private double _progressPercent;
    private bool _hardCut;
    private bool _invertMask;
    private bool _isBusy;
    private bool _isLicenseAcknowledged;
    private bool _overwriteSelectedOutput;
    private bool _disposed;

    public MacMainWindowViewModel(
        RemoveBackgroundUseCase removeBackground,
        ModelManagementUseCase modelManagement,
        IApplicationSettingsStore settingsStore,
        IBundledModelSeeder bundledModelSeeder)
    {
        _removeBackground = removeBackground;
        _modelManagement = modelManagement;
        _settingsStore = settingsStore;
        _bundledModelSeeder = bundledModelSeeder;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ModelChoice> Models { get; } = [];

    public ModelChoice? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetField(ref _selectedModel, value))
            {
                return;
            }

            IsLicenseAcknowledged = false;
            NotifyModelStateChanged();
        }
    }

    public Bitmap? InputPreview
    {
        get => _inputPreview;
        private set
        {
            Bitmap? previous = _inputPreview;
            if (SetField(ref _inputPreview, value))
            {
                previous?.Dispose();
                OnPropertyChanged(nameof(HasInputPreview));
                OnPropertyChanged(nameof(HasNoInputPreview));
            }
        }
    }

    public Bitmap? OutputPreview
    {
        get => _outputPreview;
        private set
        {
            Bitmap? previous = _outputPreview;
            if (SetField(ref _outputPreview, value))
            {
                previous?.Dispose();
                OnPropertyChanged(nameof(HasOutputPreview));
                OnPropertyChanged(nameof(HasNoOutputPreview));
            }
        }
    }

    public string? InputPath
    {
        get => _inputPath;
        private set
        {
            if (SetField(ref _inputPath, value))
            {
                OnPropertyChanged(nameof(InputDisplayName));
                OnPropertyChanged(nameof(CanProcess));
            }
        }
    }

    public string? OutputPath
    {
        get => _outputPath;
        private set
        {
            if (SetField(ref _outputPath, value))
            {
                OnPropertyChanged(nameof(OutputDisplayPath));
                OnPropertyChanged(nameof(CanProcess));
            }
        }
    }

    public string InputDisplayName => string.IsNullOrWhiteSpace(InputPath)
        ? "Henüz PNG seçilmedi"
        : Path.GetFileName(InputPath);

    public string OutputDisplayPath => string.IsNullOrWhiteSpace(OutputPath)
        ? "Çıktı yolu görsel seçilince hazırlanır"
        : OutputPath;

    public string SuggestedOutputName => string.IsNullOrWhiteSpace(InputPath)
        ? "cutlocal.png"
        : $"{Path.GetFileNameWithoutExtension(InputPath)}.cutlocal.png";

    public bool HasInputPreview => InputPreview is not null;

    public bool HasNoInputPreview => InputPreview is null;

    public bool HasOutputPreview => OutputPreview is not null;

    public bool HasNoOutputPreview => OutputPreview is null;

    public bool HasResult => !string.IsNullOrWhiteSpace(_lastOutputPath)
        && File.Exists(_lastOutputPath);

    public double Threshold
    {
        get => _threshold;
        set
        {
            if (SetField(ref _threshold, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(ThresholdText));
            }
        }
    }

    public string ThresholdText => Threshold.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    public double FeatherRadius
    {
        get => _featherRadius;
        set
        {
            if (SetField(ref _featherRadius, Math.Clamp(value, 0, 12)))
            {
                OnPropertyChanged(nameof(FeatherText));
            }
        }
    }

    public string FeatherText => $"{FeatherRadius:0.0} px";

    public bool HardCut
    {
        get => _hardCut;
        set => SetField(ref _hardCut, value);
    }

    public bool InvertMask
    {
        get => _invertMask;
        set => SetField(ref _invertMask, value);
    }

    public bool IsLicenseAcknowledged
    {
        get => _isLicenseAcknowledged;
        set
        {
            if (SetField(ref _isLicenseAcknowledged, value))
            {
                OnPropertyChanged(nameof(CanDownloadSelectedModel));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsNotBusy));
            OnPropertyChanged(nameof(CanProcess));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanDownloadSelectedModel));
        }
    }

    public bool IsNotBusy => !IsBusy;

    public bool CanCancel => IsBusy;

    public bool CanProcess => !IsBusy
        && SelectedModel?.IsInstalled == true
        && !string.IsNullOrWhiteSpace(InputPath)
        && !string.IsNullOrWhiteSpace(OutputPath);

    public bool NeedsLicenseAcknowledgement => SelectedModel is
    { Descriptor.License.CommercialUseAllowed: false, IsInstalled: false };

    public bool ShowDownloadAction => SelectedModel is { IsInstalled: false };

    public bool CanDownloadSelectedModel => !IsBusy
        && SelectedModel is { IsInstalled: false }
        && (!NeedsLicenseAcknowledgement || IsLicenseAcknowledged);

    public string SelectedModelStatus => SelectedModel?.State switch
    {
        ModelInstallationState.Installed => "Model doğrulandı ve kullanıma hazır.",
        ModelInstallationState.Partial => "İndirme yarıda kalmış; güvenli biçimde devam ettirilebilir.",
        ModelInstallationState.Corrupted => "Model doğrulamayı geçemedi; onarım gerekiyor.",
        ModelInstallationState.NotInstalled => "Model bu Mac üzerinde kurulu değil.",
        _ => "Model seçiliyor…",
    };

    public string DownloadActionText => SelectedModel?.State switch
    {
        ModelInstallationState.Partial => "İndirmeye devam et",
        ModelInstallationState.Corrupted => "Modeli onar",
        _ => "Modeli indir ve doğrula",
    };

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, Math.Clamp(value, 0, 100));
    }

    public async Task InitializeAsync()
    {
        StatusText = "Paketlenmiş model doğrulanıyor…";
        await _bundledModelSeeder.SeedAsync(CancellationToken.None);
        ApplicationSettings settings = await _settingsStore.LoadAsync(CancellationToken.None);
        Threshold = settings.Threshold;
        FeatherRadius = settings.FeatherRadius;
        HardCut = settings.HardCut;
        InvertMask = settings.InvertMask;
        await RefreshModelsAsync(settings.ModelId);
        StatusText = "Hazır — görsel seçerek başlayabilirsin.";
    }

    public async Task SetInputAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)
            || !Path.GetExtension(fullPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "Yalnızca yerel PNG dosyaları destekleniyor.";
            return;
        }

        Bitmap preview = await Task.Run(() => LoadBitmap(fullPath));
        InputPreview = preview;
        OutputPreview = null;
        _lastOutputPath = null;
        OnPropertyChanged(nameof(HasResult));
        InputPath = fullPath;
        string directory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        OutputPath = Path.Combine(directory, SuggestedOutputName);
        _overwriteSelectedOutput = false;
        StatusText = "Görsel hazır. Ayarları kontrol edip arka planı kaldırabilirsin.";
        ProgressPercent = 0;
    }

    public void SetOutputPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        OutputPath = Path.ChangeExtension(fullPath, ".png");
        _overwriteSelectedOutput = true;
        StatusText = "Çıktı konumu güncellendi.";
    }

    public async Task ProcessAsync()
    {
        if (!CanProcess || InputPath is null || OutputPath is null || SelectedModel is null)
        {
            StatusText = SelectedModel?.IsInstalled == false
                ? "Önce seçili modeli indirmen gerekiyor."
                : "İşlem için bir PNG ve geçerli çıktı yolu seçmelisin.";
            return;
        }

        IsBusy = true;
        ProgressPercent = 0;
        StatusText = "Yerel çıkarım hazırlanıyor…";
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IProgress<ProcessingProgress> progress = new Progress<ProcessingProgress>(ReportProcessingProgress);

        try
        {
            await SaveSettingsAsync();
            ProcessingResult result = await _removeBackground.ExecuteAsync(
                InputPath,
                OutputPath,
                new SingleImageProcessingOptions
                {
                    ModelId = SelectedModel.Descriptor.Id,
                    Provider = InferenceProviderKind.Cpu,
                    ExistingOutputBehavior = _overwriteSelectedOutput
                        ? ExistingOutputBehavior.Overwrite
                        : ExistingOutputBehavior.Rename,
                    MaskOptions = new MaskRefinementOptions
                    {
                        Threshold = Threshold,
                        FeatherRadius = FeatherRadius,
                        HardCut = HardCut,
                        Invert = InvertMask,
                    },
                },
                progress,
                _operationCancellation.Token);

            if (result.Outcome == ProcessingOutcome.Succeeded && result.OutputPath is not null)
            {
                OutputPath = result.OutputPath;
                _lastOutputPath = result.OutputPath;
                OutputPreview = await Task.Run(() => LoadBitmap(result.OutputPath));
                ProgressPercent = 100;
                StatusText = "Tamamlandı — özgün boyutlarda şeffaf PNG üretildi.";
                OnPropertyChanged(nameof(HasResult));
            }
            else
            {
                StatusText = result.Error?.MessageTr ?? "İşlem tamamlanamadı.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadSelectedModelAsync()
    {
        if (!CanDownloadSelectedModel || SelectedModel is null)
        {
            StatusText = NeedsLicenseAcknowledgement
                ? "Bu modelin ticari olmayan lisans koşulunu önce onaylamalısın."
                : "İndirilecek bir model seçmelisin.";
            return;
        }

        string modelId = SelectedModel.Descriptor.Id;
        IsBusy = true;
        ProgressPercent = 0;
        StatusText = "Model güvenli bağlantı üzerinden indiriliyor…";
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IProgress<ModelTransferProgress> progress = new Progress<ModelTransferProgress>(transfer =>
        {
            double ratio = transfer.TotalBytes <= 0
                ? 0
                : (double)transfer.BytesReceived / transfer.TotalBytes;
            ProgressPercent = ratio * 100;
            StatusText = $"Model indiriliyor — {FormatBytes(transfer.BytesReceived)} / {FormatBytes(transfer.TotalBytes)}";
        });

        try
        {
            ModelPackageOperationResult result = SelectedModel.State == ModelInstallationState.Corrupted
                ? await _modelManagement.RepairAsync(
                    SelectedModel.Descriptor,
                    progress,
                    _operationCancellation.Token,
                    IsLicenseAcknowledged)
                : await _modelManagement.DownloadAsync(
                    SelectedModel.Descriptor,
                    progress,
                    _operationCancellation.Token,
                    IsLicenseAcknowledged);

            StatusText = result.Succeeded
                ? "Model SHA-256 ile doğrulandı ve kullanıma hazır."
                : $"Model işlemi tamamlanamadı ({result.Code}).";
            await RefreshModelsAsync(modelId);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Model indirme iptal edildi; kısmi dosya güvenli biçimde saklandı.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Cancel() => _operationCancellation?.Cancel();

    public void RevealOutput()
    {
        if (!HasResult || _lastOutputPath is null)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(_lastOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "open",
            UseShellExecute = false,
        };
        process.StartInfo.ArgumentList.Add(directory);
        process.Start();
    }

    public void ReportUiError(string message) => StatusText = message;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _inputPreview?.Dispose();
        _outputPreview?.Dispose();
    }

    private async Task RefreshModelsAsync(string? preferredModelId)
    {
        IReadOnlyList<ModelInstallationInfo> installed = await _modelManagement.InspectAsync(CancellationToken.None);
        Models.Clear();
        foreach (ModelInstallationInfo information in installed
                     .OrderByDescending(item => item.State == ModelInstallationState.Installed)
                     .ThenBy(item => item.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            Models.Add(new ModelChoice(information));
        }

        SelectedModel = Models.FirstOrDefault(item => item.Descriptor.Id.Equals(
                preferredModelId,
                StringComparison.OrdinalIgnoreCase))
            ?? Models.FirstOrDefault(item => item.IsInstalled)
            ?? Models.FirstOrDefault();
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(new ApplicationSettings
        {
            Culture = "tr-TR",
            ModelId = SelectedModel?.Descriptor.Id ?? "u2netp",
            Provider = InferenceProviderKind.Cpu,
            OutputDirectory = OutputPath is null ? null : Path.GetDirectoryName(OutputPath),
            Threshold = Threshold,
            FeatherRadius = FeatherRadius,
            HardCut = HardCut,
            InvertMask = InvertMask,
        }, CancellationToken.None);
    }

    private void ReportProcessingProgress(ProcessingProgress progress)
    {
        ProgressPercent = progress.Value * 100;
        StatusText = progress.Status switch
        {
            ItemStatus.PreparingModel => "Model hazırlanıyor…",
            ItemStatus.Decoding => "PNG çözümleniyor…",
            ItemStatus.Preprocessing => "Görsel modele hazırlanıyor…",
            ItemStatus.Inferring => "Arka plan yerel olarak ayrılıyor…",
            ItemStatus.Postprocessing => "Maske ve kenarlar iyileştiriliyor…",
            ItemStatus.Encoding => "Şeffaf PNG yazılıyor…",
            ItemStatus.Completed => "Tamamlandı.",
            _ => "İşleniyor…",
        };
    }

    private void NotifyModelStateChanged()
    {
        OnPropertyChanged(nameof(SelectedModelStatus));
        OnPropertyChanged(nameof(NeedsLicenseAcknowledgement));
        OnPropertyChanged(nameof(ShowDownloadAction));
        OnPropertyChanged(nameof(CanDownloadSelectedModel));
        OnPropertyChanged(nameof(DownloadActionText));
        OnPropertyChanged(nameof(CanProcess));
    }

    private static Bitmap LoadBitmap(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new Bitmap(stream);
    }

    private static string FormatBytes(long bytes)
    {
        const double mebibyte = 1024 * 1024;
        return bytes >= mebibyte
            ? $"{bytes / mebibyte:0.0} MB"
            : $"{bytes / 1024d:0.0} KB";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class ModelChoice
{
    public ModelChoice(ModelInstallationInfo installation)
    {
        Descriptor = installation.Descriptor;
        State = installation.State;
    }

    public ModelDescriptor Descriptor { get; }

    public ModelInstallationState State { get; }

    public bool IsInstalled => State == ModelInstallationState.Installed;

    public string DisplayText => $"{Descriptor.DisplayName}  ·  {Descriptor.Input.Width}×{Descriptor.Input.Height}";
}
