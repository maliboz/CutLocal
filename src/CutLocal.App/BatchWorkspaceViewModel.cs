using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging;

namespace CutLocal.App;

/// <summary>Coordinates the durable, bounded Phase 4 batch workspace.</summary>
public sealed partial class BatchWorkspaceViewModel : ObservableObject, IDisposable
{
    private static readonly Action<ILogger, string, Exception?> LogBatchUiFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(5101, nameof(LogBatchUiFailure)),
            "Phase 4 batch UI operation failed with code {Code}; paths omitted");

    private readonly AddImagesUseCase _addImages;
    private readonly AddFolderUseCase _addFolder;
    private readonly RetryFailedItemsUseCase _retryFailed;
    private readonly RemoveBatchItemsUseCase _removeItems;
    private readonly ReconfigureBatchUseCase _reconfigure;
    private readonly RecoverInterruptedJobUseCase _recover;
    private readonly ProcessBatchUseCase _processBatch;
    private readonly IProcessingJobStore _jobStore;
    private readonly IFileDialogService _fileDialog;
    private readonly IFileLauncher _fileLauncher;
    private readonly ILocalizationService _localization;
    private readonly ILogger<BatchWorkspaceViewModel> _logger;
    private ProcessingJob? _job;
    private ProcessingPreset? _preset;
    private CancellationTokenSource? _operationCancellation;
    private Task<ProcessingJob>? _runningTask;
    private bool _initialized;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearBatchCommand))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeBatchCommand))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearBatchCommand))]
    private bool _isDiscovering;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartBatchCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearBatchCommand))]
    private bool _isExternalBusy;

    [ObservableProperty]
    private bool _includeSubfolders = true;

    [ObservableProperty]
    private string _statusText = "—";

    [ObservableProperty]
    private string _summaryText = "0 / 0";

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private int _terminalCount;

    [ObservableProperty]
    private int _totalCount;

    /// <summary>Initializes the Phase 4 workspace.</summary>
    public BatchWorkspaceViewModel(
        AddImagesUseCase addImages,
        AddFolderUseCase addFolder,
        RetryFailedItemsUseCase retryFailed,
        RemoveBatchItemsUseCase removeItems,
        ReconfigureBatchUseCase reconfigure,
        RecoverInterruptedJobUseCase recover,
        ProcessBatchUseCase processBatch,
        IProcessingJobStore jobStore,
        IFileDialogService fileDialog,
        IFileLauncher fileLauncher,
        ILocalizationService localization,
        ILogger<BatchWorkspaceViewModel> logger)
    {
        _addImages = addImages;
        _addFolder = addFolder;
        _retryFailed = retryFailed;
        _removeItems = removeItems;
        _reconfigure = reconfigure;
        _recover = recover;
        _processBatch = processBatch;
        _jobStore = jobStore;
        _fileDialog = fileDialog;
        _fileLauncher = fileLauncher;
        _localization = localization;
        _logger = logger;
        StatusText = LocalizedStatus(JobStatus.Queued);
        _localization.CultureChanged += OnCultureChanged;
    }

    /// <summary>Gets lightweight queue row view models.</summary>
    public ObservableCollection<BatchItemViewModel> Items { get; } = [];

    /// <summary>Gets whether the queue has any item.</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>Gets whether add/remove and settings operations are available.</summary>
    public bool CanEdit => !IsActive && !IsDiscovering && !IsExternalBusy;

    /// <summary>Gets the latest durable job snapshot.</summary>
    public ProcessingJob? CurrentJob => _job;

    /// <summary>Adds dropped PNG files through the same durable discovery boundary as the picker.</summary>
    public async Task<bool> AcceptDroppedFilesAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!CanAdd() || _preset is null)
        {
            return false;
        }

        string[] candidates = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        int previousCount = Items.Count;
        await DiscoverAsync(
            token => _addImages.Execute(_job, _preset, candidates, token),
            cancellationToken);
        return Items.Count > previousCount;
    }

    /// <summary>Loads and normalizes the last durable queue once.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            ProcessingJob? recovered = await _recover.ExecuteAsync(cancellationToken);
            if (recovered is not null)
            {
                ApplyJob(recovered);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            LogBatchUiFailure(_logger, "BATCH_RECOVERY", exception);
            StatusText = Localized("Kuyruk kurtarılamadı", "The queue could not be recovered");
        }
    }

    /// <summary>Updates the preset captured by the next add/start operation.</summary>
    public void ConfigurePreset(ProcessingPreset? preset)
    {
        _preset = preset;
        StartBatchCommand.NotifyCanExecuteChanged();
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        OpenBatchOutputFolderCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Cancels active work, waits for its final durable snapshot, and releases events.</summary>
    public async Task ShutdownAsync()
    {
        if (_disposed)
        {
            return;
        }

        _operationCancellation?.Cancel();
        if (_runningTask is not null)
        {
            try
            {
                await _runningTask;
            }
            catch (Exception exception) when (exception is OperationCanceledException
                or IOException
                or UnauthorizedAccessException)
            {
                LogBatchUiFailure(_logger, "BATCH_SHUTDOWN", exception);
            }
        }

        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _localization.CultureChanged -= OnCultureChanged;
        foreach (BatchItemViewModel item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        GC.SuppressFinalize(this);
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddFilesAsync()
    {
        IReadOnlyList<string> paths = _fileDialog.SelectPngFiles();
        if (paths.Count == 0 || _preset is null)
        {
            return;
        }

        await DiscoverAsync(token => _addImages.Execute(_job, _preset, paths, token));
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddFolderAsync()
    {
        string? folder = _fileDialog.SelectInputFolder();
        if (folder is null || _preset is null)
        {
            return;
        }

        await DiscoverAsync(token => _addFolder.Execute(
            _job,
            _preset,
            folder,
            IncludeSubfolders,
            token));
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartBatchAsync()
    {
        if (_job is null || _preset is null)
        {
            return;
        }

        _job = _reconfigure.Execute(_job, _preset);
        await SaveCurrentJobAsync("BATCH_RECONFIGURE");
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsActive = true;
        IsPaused = false;
        Progress<BatchProgressUpdate> progress = new(OnBatchProgress);
        try
        {
            _runningTask = _processBatch.ExecuteAsync(
                _job,
                progress,
                _operationCancellation.Token);
            ProcessingJob result = await _runningTask;
            ApplyJob(result);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            LogBatchUiFailure(_logger, "BATCH_EXECUTE", exception);
            StatusText = Localized("Toplu işlem tamamlanamadı", "Batch processing could not finish");
        }
        finally
        {
            _runningTask = null;
            IsPaused = false;
            IsActive = false;
            UpdateCommandStates();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseBatchAsync()
    {
        if (await _processBatch.PauseAsync(CancellationToken.None))
        {
            IsPaused = true;
            StatusText = LocalizedStatus(JobStatus.Paused);
        }
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeBatchAsync()
    {
        if (await _processBatch.ResumeAsync(CancellationToken.None))
        {
            IsPaused = false;
            StatusText = LocalizedStatus(JobStatus.Running);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelBatch()
    {
        StatusText = Localized("İptal bekleniyor", "Waiting to cancel");
        _operationCancellation?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryFailedAsync()
    {
        if (_job is null || _preset is null)
        {
            return;
        }

        ApplyJob(_retryFailed.Execute(_job, _preset));
        await SaveCurrentJobAsync("BATCH_RETRY_RESET");
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private async Task RemoveSelectedAsync()
    {
        if (_job is null)
        {
            return;
        }

        Guid[] selected = Items.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
        ApplyJob(_removeItems.Execute(_job, selected));
        await SaveCurrentJobAsync("BATCH_REMOVE");
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearBatchAsync()
    {
        try
        {
            await _jobStore.DeleteAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogBatchUiFailure(_logger, "BATCH_CLEAR", exception);
            StatusText = Localized("Kuyruk temizlenemedi", "The queue could not be cleared");
            return;
        }

        _job = null;
        ClearRows();
        OverallProgress = 0;
        TerminalCount = 0;
        TotalCount = 0;
        SummaryText = "0 / 0";
        StatusText = LocalizedStatus(JobStatus.Queued);
        UpdateCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanOpenOutputFolder))]
    private void OpenBatchOutputFolder()
    {
        try
        {
            string outputDirectory = _preset!.Output.OutputDirectory;
            _fileLauncher.OpenContainingFolder(Path.Combine(outputDirectory, "cutlocal-output.png"));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception
            or System.Runtime.InteropServices.COMException)
        {
            LogBatchUiFailure(_logger, "BATCH_OPEN_OUTPUT", exception);
            StatusText = Localized("Çıktı klasörü açılamadı", "The output folder could not be opened");
        }
    }

    private async Task DiscoverAsync(
        Func<CancellationToken, AddInputsResult> discovery,
        CancellationToken cancellationToken = default)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsDiscovering = true;
        StatusText = Localized("PNG dosyaları taranıyor", "Discovering PNG files");
        try
        {
            AddInputsResult result = await Task.Run(
                () => discovery(_operationCancellation.Token),
                _operationCancellation.Token);
            ApplyJob(result.Job);
            await SaveCurrentJobAsync("BATCH_DISCOVERY_SAVE");
            StatusText = Localized(
                $"{result.AddedCount} eklendi · {result.DuplicateCount} tekrar · {result.RejectedCount} reddedildi",
                $"{result.AddedCount} added · {result.DuplicateCount} duplicate · {result.RejectedCount} rejected");
        }
        catch (OperationCanceledException) when (_operationCancellation.IsCancellationRequested)
        {
            StatusText = Localized("Tarama iptal edildi", "Discovery cancelled");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            LogBatchUiFailure(_logger, "BATCH_DISCOVERY", exception);
            StatusText = Localized("Klasör veya dosyalar eklenemedi", "Files or folder could not be added");
        }
        finally
        {
            IsDiscovering = false;
            UpdateCommandStates();
        }
    }

    private async Task SaveCurrentJobAsync(string errorCode)
    {
        if (_job is null)
        {
            return;
        }

        try
        {
            await _jobStore.SaveAsync(_job, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            LogBatchUiFailure(_logger, errorCode, exception);
            StatusText = Localized("Kuyruk kaydedilemedi", "The queue could not be saved");
        }
    }

    private void OnBatchProgress(BatchProgressUpdate update)
    {
        if (update.Item is not null)
        {
            BatchItemViewModel? row = Items.FirstOrDefault(item => item.Id == update.Item.Id);
            row?.Update(update.Item, _localization.CurrentCulture);
        }

        TerminalCount = update.TerminalCount;
        TotalCount = update.TotalCount;
        OverallProgress = update.OverallProgress;
        SummaryText = $"{TerminalCount} / {TotalCount}";
        StatusText = LocalizedStatus(update.JobStatus);
        IsPaused = update.JobStatus == JobStatus.Paused;
    }

    private void ApplyJob(ProcessingJob job)
    {
        _job = job;
        Dictionary<Guid, BatchItemViewModel> existing = Items.ToDictionary(item => item.Id);
        if (existing.Count != job.Items.Count || job.Items.Any(item => !existing.ContainsKey(item.Id)))
        {
            ClearRows();
            foreach (ProcessingItem item in job.Items)
            {
                BatchItemViewModel row = new(item, _localization.CurrentCulture);
                row.PropertyChanged += OnItemPropertyChanged;
                Items.Add(row);
            }
        }
        else
        {
            foreach (ProcessingItem item in job.Items)
            {
                existing[item.Id].Update(item, _localization.CurrentCulture);
            }
        }

        TotalCount = job.Items.Count;
        TerminalCount = job.Items.Count(item => IsTerminal(item.Status));
        OverallProgress = job.Items.Count == 0
            ? 0
            : job.Items.Sum(item => item.Progress) / job.Items.Count;
        SummaryText = $"{TerminalCount} / {TotalCount}";
        StatusText = LocalizedStatus(job.Status);
        OnPropertyChanged(nameof(HasItems));
        UpdateCommandStates();
    }

    private void ClearRows()
    {
        foreach (BatchItemViewModel item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        Items.Clear();
        OnPropertyChanged(nameof(HasItems));
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BatchItemViewModel.IsSelected))
        {
            RemoveSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        foreach (BatchItemViewModel item in Items)
        {
            item.RefreshCulture(_localization.CurrentCulture);
        }

        StatusText = LocalizedStatus(_job?.Status ?? JobStatus.Queued);
    }

    private void UpdateCommandStates()
    {
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        StartBatchCommand.NotifyCanExecuteChanged();
        PauseBatchCommand.NotifyCanExecuteChanged();
        ResumeBatchCommand.NotifyCanExecuteChanged();
        CancelBatchCommand.NotifyCanExecuteChanged();
        RetryFailedCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        ClearBatchCommand.NotifyCanExecuteChanged();
        OpenBatchOutputFolderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEdit));
    }

    private bool CanAdd() => CanEdit && _preset is not null;

    private bool CanStart() => CanEdit
        && _preset is not null
        && _job?.Items.Any(item => item.Status == ItemStatus.Queued) == true;

    private bool CanPause() => IsActive && !IsPaused;

    private bool CanResume() => IsActive && IsPaused;

    private bool CanCancel() => IsActive || IsDiscovering;

    private bool CanRetry() => CanEdit
        && _job?.Items.Any(item => item.Status == ItemStatus.Failed) == true;

    private bool CanRemoveSelected() => CanEdit && Items.Any(item => item.IsSelected);

    private bool CanClear() => CanEdit && HasItems;

    private bool CanOpenOutputFolder() => _preset is not null
        && Directory.Exists(_preset.Output.OutputDirectory);

    private static bool IsTerminal(ItemStatus status) => status is
        ItemStatus.Completed or ItemStatus.Failed or ItemStatus.Cancelled or ItemStatus.Skipped;

    private string LocalizedStatus(JobStatus status) => status switch
    {
        JobStatus.Queued => Localized("Kuyruk hazır", "Queue ready"),
        JobStatus.Running => Localized("Toplu işlem çalışıyor", "Batch is running"),
        JobStatus.Paused => Localized("Toplu işlem duraklatıldı", "Batch is paused"),
        JobStatus.Completed => Localized("Toplu işlem tamamlandı", "Batch completed"),
        JobStatus.CompletedWithErrors => Localized("Hatalarla tamamlandı", "Completed with errors"),
        JobStatus.Cancelled => Localized("Toplu işlem iptal edildi", "Batch cancelled"),
        JobStatus.Failed => Localized("Toplu işlem başarısız", "Batch failed"),
        JobStatus.Interrupted => Localized("Önceki kuyruk kurtarıldı", "Previous queue recovered"),
        _ => status.ToString(),
    };

    private string Localized(string turkish, string english) =>
        _localization.CurrentCulture.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            ? turkish
            : english;
}

/// <summary>Represents one lightweight virtualized batch row.</summary>
public sealed partial class BatchItemViewModel : ObservableObject
{
    private ProcessingItem _snapshot;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "0%";

    [ObservableProperty]
    private string _elapsedText = "—";

    [ObservableProperty]
    private string _providerText = "—";

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private int _attemptCount;

    /// <summary>Initializes one row.</summary>
    public BatchItemViewModel(ProcessingItem item, string cultureName)
    {
        _snapshot = item;
        Id = item.Id;
        InputPath = item.InputPath;
        FileName = Path.GetFileName(item.InputPath);
        Update(item, cultureName);
    }

    /// <summary>Gets the durable item identity.</summary>
    public Guid Id { get; }

    /// <summary>Gets the local input path for tooltip display.</summary>
    public string InputPath { get; }

    /// <summary>Gets the privacy-safe filename.</summary>
    public string FileName { get; }

    /// <summary>Gets the latest output path.</summary>
    public string OutputPath => _snapshot.OutputPath;

    /// <summary>Updates the row from an immutable engine snapshot.</summary>
    public void Update(ProcessingItem item, string cultureName)
    {
        _snapshot = item;
        Progress = item.Progress;
        ProgressText = $"{item.Progress * 100:0}%";
        ElapsedText = item.Elapsed?.ToString("m\\:ss\\.fff", CultureInfo.GetCultureInfo(cultureName)) ?? "—";
        ProviderText = item.ProviderId ?? "—";
        ErrorText = item.Error is null
            ? string.Empty
            : cultureName.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
                ? item.Error.MessageTr
                : item.Error.MessageEn;
        AttemptCount = item.AttemptCount;
        StatusText = Status(item.Status, cultureName);
        OnPropertyChanged(nameof(OutputPath));
    }

    /// <summary>Refreshes localized status/error text without changing the snapshot.</summary>
    public void RefreshCulture(string cultureName) => Update(_snapshot, cultureName);

    private static string Status(ItemStatus status, string cultureName)
    {
        bool turkish = cultureName.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        return status switch
        {
            ItemStatus.Queued => turkish ? "Bekliyor" : "Queued",
            ItemStatus.PreparingModel => turkish ? "Model hazırlanıyor" : "Preparing model",
            ItemStatus.Decoding => turkish ? "Çözümleniyor" : "Decoding",
            ItemStatus.Preprocessing => turkish ? "Tensor hazırlanıyor" : "Preprocessing",
            ItemStatus.Inferring => turkish ? "Ayrılıyor" : "Inferring",
            ItemStatus.Postprocessing => turkish ? "Maske işleniyor" : "Postprocessing",
            ItemStatus.Encoding => turkish ? "Kaydediliyor" : "Encoding",
            ItemStatus.Completed => turkish ? "Tamamlandı" : "Completed",
            ItemStatus.Failed => turkish ? "Başarısız" : "Failed",
            ItemStatus.Cancelled => turkish ? "İptal" : "Cancelled",
            ItemStatus.Skipped => turkish ? "Atlandı" : "Skipped",
            _ => status.ToString(),
        };
    }
}
