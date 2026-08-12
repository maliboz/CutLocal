using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging;

namespace CutLocal.App;

/// <summary>Coordinates the offline-first model catalog and explicit package operations.</summary>
public sealed partial class ModelManagerViewModel : ObservableObject, IDisposable
{
    private static readonly Action<ILogger, string, Exception?> LogOperationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(5201, nameof(LogOperationFailure)),
            "Model manager operation failed with code {Code}; local paths omitted");

    private readonly ModelManagementUseCase _useCase;
    private readonly IFileDialogService _fileDialogs;
    private readonly ILocalizationService _localization;
    private readonly ILogger<ModelManagerViewModel> _logger;
    private readonly object _taskGate = new();
    private readonly List<Task> _activeTasks = [];
    private bool _disposed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText;

    /// <summary>Initializes the model manager.</summary>
    public ModelManagerViewModel(
        ModelManagementUseCase useCase,
        IFileDialogService fileDialogs,
        ILocalizationService localization,
        ILogger<ModelManagerViewModel> logger)
    {
        _useCase = useCase;
        _fileDialogs = fileDialogs;
        _localization = localization;
        _logger = logger;
        _statusText = localization.GetString("Model.Status.Ready");
        _localization.CultureChanged += OnCultureChanged;
    }

    /// <summary>Gets the reviewed catalog and accepted custom packages.</summary>
    public ObservableCollection<ModelPackageItemViewModel> Models { get; } = [];

    /// <summary>Refreshes local package state without making a network request.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await RefreshAsync(cancellationToken);
    }

    /// <summary>Cancels active transfers and waits for their partial files to close.</summary>
    public async Task ShutdownAsync()
    {
        foreach (ModelPackageItemViewModel item in Models)
        {
            item.Pause();
        }

        Task[] tasks;
        lock (_taskGate)
        {
            tasks = [.. _activeTasks];
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.CultureChanged -= OnCultureChanged;
        foreach (ModelPackageItemViewModel item in Models)
        {
            item.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    internal Task DownloadAsync(ModelPackageItemViewModel item) =>
        StartReviewedPackageOperation(
            item,
            (progress, token, accepted) =>
                _useCase.DownloadAsync(item.Descriptor, progress, token, accepted));

    internal Task RepairAsync(ModelPackageItemViewModel item) =>
        StartReviewedPackageOperation(
            item,
            (progress, token, accepted) =>
                _useCase.RepairAsync(item.Descriptor, progress, token, accepted));

    internal Task DeleteAsync(ModelPackageItemViewModel item) => TrackAsync(
        RunDeleteAsync(item));

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        string? onnxPath = _fileDialogs.SelectOnnxModel();
        if (onnxPath is null)
        {
            return;
        }

        string? manifestPath = _fileDialogs.SelectModelManifest();
        if (manifestPath is null || !_fileDialogs.ConfirmCustomModelLicense())
        {
            StatusText = _localization.GetString("Model.Status.ImportCancelled");
            return;
        }

        IsBusy = true;
        try
        {
            ModelPackageOperationResult result = await _useCase.ImportAsync(
                new ModelImportRequest
                {
                    OnnxPath = onnxPath,
                    ManifestPath = manifestPath,
                    LicenseAcknowledged = true,
                },
                CancellationToken.None);
            SetResultStatus(result);
            await RefreshAsync(CancellationToken.None, updateStatus: false);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException)
        {
            LogOperationFailure(_logger, "MODEL_IMPORT_UI", exception);
            StatusText = _localization.GetString("Model.Status.Failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanImport() => !IsBusy && !Models.Any(item => item.IsBusy);

    private Task StartReviewedPackageOperation(
        ModelPackageItemViewModel item,
        Func<IProgress<ModelTransferProgress>, CancellationToken, bool, ValueTask<ModelPackageOperationResult>>
            operation)
    {
        bool accepted = item.Descriptor.License.CommercialUseAllowed
            || _fileDialogs.ConfirmRestrictedModelLicense(item.Descriptor);
        if (!accepted)
        {
            StatusText = _localization.GetString("Model.Status.LicenseDeclined");
            return Task.CompletedTask;
        }

        return TrackAsync(RunPackageOperationAsync(
            item,
            (progress, token) => operation(progress, token, accepted)));
    }

    private async Task RunPackageOperationAsync(
        ModelPackageItemViewModel item,
        Func<IProgress<ModelTransferProgress>, CancellationToken, ValueTask<ModelPackageOperationResult>> operation)
    {
        CancellationToken token = item.BeginOperation();
        IsBusy = true;
        StatusText = _localization.GetString("Model.Status.Working");
        try
        {
            Progress<ModelTransferProgress> progress = new(item.ReportProgress);
            ModelPackageOperationResult result = await operation(progress, token);
            SetResultStatus(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusText = _localization.GetString("Model.Status.Paused");
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or HttpRequestException
            or UnauthorizedAccessException)
        {
            LogOperationFailure(_logger, "MODEL_PACKAGE_UI", exception);
            StatusText = _localization.GetString("Model.Status.Failed");
        }
        finally
        {
            item.EndOperation();
            IsBusy = Models.Any(model => model.IsBusy);
            await RefreshAsync(CancellationToken.None, updateStatus: false);
        }
    }

    private async Task RunDeleteAsync(ModelPackageItemViewModel item)
    {
        CancellationToken token = item.BeginOperation();
        IsBusy = true;
        try
        {
            ModelPackageOperationResult result = await _useCase.DeleteAsync(item.Descriptor, token);
            SetResultStatus(result);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogOperationFailure(_logger, "MODEL_DELETE_UI", exception);
            StatusText = _localization.GetString("Model.Status.Failed");
        }
        finally
        {
            item.EndOperation();
            IsBusy = Models.Any(model => model.IsBusy);
            await RefreshAsync(CancellationToken.None, updateStatus: false);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken, bool updateStatus = true)
    {
        IReadOnlyList<ModelInstallationInfo> models = await _useCase.InspectAsync(cancellationToken);
        foreach (ModelPackageItemViewModel item in Models)
        {
            item.Dispose();
        }

        Models.Clear();
        foreach (ModelInstallationInfo model in models.OrderBy(
                     item => item.Descriptor.DisplayName,
                     StringComparer.CurrentCulture))
        {
            Models.Add(new ModelPackageItemViewModel(this, model, _localization));
        }

        if (updateStatus)
        {
            StatusText = _localization.GetString("Model.Status.Ready");
        }

        ImportCommand.NotifyCanExecuteChanged();
    }

    private async Task TrackAsync(Task task)
    {
        lock (_taskGate)
        {
            _activeTasks.Add(task);
        }

        try
        {
            await task;
        }
        finally
        {
            lock (_taskGate)
            {
                _activeTasks.Remove(task);
            }
        }
    }

    private void SetResultStatus(ModelPackageOperationResult result)
    {
        string key = $"Model.Status.{result.Code}";
        string localized = _localization.GetString(key);
        StatusText = localized.Equals(key, StringComparison.Ordinal)
            ? result.Code
            : localized;
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        StatusText = _localization.GetString("Model.Status.Ready");
        foreach (ModelPackageItemViewModel item in Models)
        {
            item.RefreshLocalization();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        foreach (ModelPackageItemViewModel item in Models)
        {
            item.RefreshOwnerBusy();
        }
    }
}

/// <summary>Represents one virtualized model-manager row.</summary>
public sealed partial class ModelPackageItemViewModel : ObservableObject, IDisposable
{
    private readonly ModelManagerViewModel _owner;
    private readonly ILocalizationService _localization;
    private CancellationTokenSource? _operationCancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanRepair))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(CanRepair))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private ModelInstallationState _state;

    [ObservableProperty]
    private long _localBytes;

    /// <summary>Initializes one row from an inspected package.</summary>
    public ModelPackageItemViewModel(
        ModelManagerViewModel owner,
        ModelInstallationInfo info,
        ILocalizationService localization)
    {
        _owner = owner;
        _localization = localization;
        Descriptor = info.Descriptor;
        IsUserSupplied = info.IsUserSupplied;
        _state = info.State;
        _localBytes = info.LocalBytes;
        _progressValue = Descriptor.FileSizeBytes > 0
            ? Math.Clamp((double)info.LocalBytes / Descriptor.FileSizeBytes, 0, 1)
            : 0;
    }

    /// <summary>Gets the reviewed model manifest.</summary>
    public ModelDescriptor Descriptor { get; }
    /// <summary>Gets whether this row came from an acknowledged user import.</summary>
    public bool IsUserSupplied { get; }
    /// <summary>Gets a formatted exact package size.</summary>
    public string SizeText => FormatBytes(Descriptor.FileSizeBytes);
    /// <summary>Gets version text.</summary>
    public string VersionText => $"v{Descriptor.Version}";
    /// <summary>Gets the SPDX license and source policy.</summary>
    public string LicenseText => Descriptor.License.CommercialUseAllowed
        ? $"{Descriptor.License.Spdx} · {Descriptor.License.Source}"
        : $"{Descriptor.License.Spdx} · {_localization.GetString("Model.License.NonCommercial")} · "
            + Descriptor.License.Source;
    /// <summary>Gets declared provider compatibility.</summary>
    public string ProviderText => string.Join(", ", Descriptor.SupportedProviders.Select(
        provider => provider.ToUpperInvariant()));
    /// <summary>Gets the localized package state.</summary>
    public string StateText => _localization.GetString($"Model.State.{State}");
    /// <summary>Gets whether download/resume is available.</summary>
    public bool CanDownload => !_owner.IsBusy && !IsBusy && State is ModelInstallationState.NotInstalled
        or ModelInstallationState.Partial;
    /// <summary>Gets whether an active transfer can be paused.</summary>
    public bool CanPause => IsBusy;
    /// <summary>Gets whether corrupted content can be repaired.</summary>
    public bool CanRepair => !_owner.IsBusy && !IsBusy
        && State == ModelInstallationState.Corrupted
        && !IsUserSupplied;
    /// <summary>Gets whether local content can be deleted.</summary>
    public bool CanDelete => !_owner.IsBusy && !IsBusy && State != ModelInstallationState.NotInstalled;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => _owner.DownloadAsync(this);

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void PauseOperation() => Pause();

    [RelayCommand(CanExecute = nameof(CanRepair))]
    private Task RepairAsync() => _owner.RepairAsync(this);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => _owner.DeleteAsync(this);

    internal CancellationToken BeginOperation()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        return _operationCancellation.Token;
    }

    internal void EndOperation()
    {
        IsBusy = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    internal void Pause() => _operationCancellation?.Cancel();

    internal void ReportProgress(ModelTransferProgress progress)
    {
        LocalBytes = progress.BytesReceived;
        ProgressValue = progress.TotalBytes > 0
            ? Math.Clamp((double)progress.BytesReceived / progress.TotalBytes, 0, 1)
            : 0;
    }

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(LicenseText));
    }

    internal void RefreshOwnerBusy()
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanRepair));
        OnPropertyChanged(nameof(CanDelete));
        DownloadCommand.NotifyCanExecuteChanged();
        RepairCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        GC.SuppressFinalize(this);
    }

    private static string FormatBytes(long value)
    {
        const double mebibyte = 1024 * 1024;
        return value >= mebibyte
            ? $"{value / mebibyte:0.0} MiB"
            : $"{value / 1024d:0.0} KiB";
    }
}
