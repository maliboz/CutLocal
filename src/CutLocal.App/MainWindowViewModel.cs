using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging;

namespace CutLocal.App;

/// <summary>Coordinates the single-image and durable Phase 4 batch workspaces.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const int PreviewMaximumEdge = 1600;
    private static readonly Action<ILogger, string, Exception?> LogUiFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(5001, nameof(LogUiFailure)),
            "Phase 3 UI operation failed with code {Code}; path omitted");

    private readonly RemoveBackgroundUseCase _removeBackground;
    private readonly BatchWorkspaceViewModel _batch;
    private readonly IFileDialogService _fileDialog;
    private readonly IClipboardService _clipboard;
    private readonly IPreviewBitmapService _previewBitmaps;
    private readonly IFileLauncher _fileLauncher;
    private readonly IModelCatalog _modelCatalog;
    private readonly IInferenceProviderCatalog _providerCatalog;
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IModelManagerDialog? _modelManagerDialog;
    private readonly DispatcherTimer _previewDebounceTimer;
    private readonly DispatcherTimer _settingsSaveTimer;
    private CancellationTokenSource? _processingCancellation;
    private CancellationTokenSource? _inputPreviewCancellation;
    private ClipboardCapture? _ownedClipboardCapture;
    private string _lastStatusKey = "Status.Ready";
    private bool _initialized;
    private bool _isLivePreviewRun;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInput))]
    [NotifyPropertyChangedFor(nameof(InputFileName))]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearInputCommand))]
    private string? _inputPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    [NotifyCanExecuteChangedFor(nameof(OpenOutputFolderCommand))]
    private string? _outputPath;

    [ObservableProperty]
    private BitmapSource? _beforePreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProcessedPreview))]
    private BitmapSource? _afterPreview;

    [ObservableProperty]
    private BitmapSource? _maskPreview;

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(SelectInputCommand))]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearInputCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseOutputFolderCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDropActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingleMode))]
    private bool _isBatchMode;

    private int _batchConcurrency = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    private ModelOption? _selectedModel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    private ProviderOption? _selectedProvider;

    [ObservableProperty]
    private CultureOption? _selectedCulture;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    private string? _outputDirectory;

    [ObservableProperty]
    private string _fileNameSuffix = ".cutlocal";

    [ObservableProperty]
    private ExistingOutputBehavior _existingOutputBehavior = ExistingOutputBehavior.Rename;

    [ObservableProperty]
    private double _threshold = 0.5;

    [ObservableProperty]
    private double _featherRadius;

    [ObservableProperty]
    private bool _hardCut;

    [ObservableProperty]
    private bool _invertMask;

    [ObservableProperty]
    private bool _isLivePreviewEnabled = true;

    [ObservableProperty]
    private string _lastProviderText = "—";

    [ObservableProperty]
    private string _lastElapsedText = "—";

    /// <summary>Initializes the desktop view model.</summary>
    public MainWindowViewModel(
        RemoveBackgroundUseCase removeBackground,
        BatchWorkspaceViewModel batch,
        IFileDialogService fileDialog,
        IClipboardService clipboard,
        IPreviewBitmapService previewBitmaps,
        IFileLauncher fileLauncher,
        IModelCatalog modelCatalog,
        IInferenceProviderCatalog providerCatalog,
        IApplicationSettingsStore settingsStore,
        ILocalizationService localization,
        ILogger<MainWindowViewModel> logger,
        IModelManagerDialog? modelManagerDialog = null)
    {
        _removeBackground = removeBackground;
        _batch = batch;
        _fileDialog = fileDialog;
        _clipboard = clipboard;
        _previewBitmaps = previewBitmaps;
        _fileLauncher = fileLauncher;
        _modelCatalog = modelCatalog;
        _providerCatalog = providerCatalog;
        _settingsStore = settingsStore;
        _localization = localization;
        _logger = logger;
        _modelManagerDialog = modelManagerDialog;
        _statusText = localization.GetString(_lastStatusKey);
        _previewDebounceTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(450),
            DispatcherPriority.Background,
            OnPreviewDebounceElapsed,
            Dispatcher.CurrentDispatcher);
        _previewDebounceTimer.Stop();
        _settingsSaveTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(650),
            DispatcherPriority.ApplicationIdle,
            OnSettingsSaveElapsed,
            Dispatcher.CurrentDispatcher);
        _settingsSaveTimer.Stop();
        _localization.CultureChanged += OnCultureChanged;
        _batch.PropertyChanged += OnBatchPropertyChanged;
    }

    /// <summary>Gets model choices discovered from valid local manifests.</summary>
    public ObservableCollection<ModelOption> Models { get; } = [];

    /// <summary>Gets Auto and concrete offline provider/device choices.</summary>
    public ObservableCollection<ProviderOption> Providers { get; } = [];

    /// <summary>Gets localized existing-output choices.</summary>
    public ObservableCollection<ExistingOutputOption> ExistingOutputOptions { get; } = [];

    /// <summary>Gets supported UI cultures.</summary>
    public IReadOnlyList<CultureOption> Cultures => _localization.Cultures;

    /// <summary>Gets the durable batch workspace.</summary>
    public BatchWorkspaceViewModel Batch => _batch;

    /// <summary>Gets whether input is selected.</summary>
    public bool HasInput => !string.IsNullOrWhiteSpace(InputPath);

    /// <summary>Gets whether a committed output exists in this session.</summary>
    public bool HasOutput => !string.IsNullOrWhiteSpace(OutputPath);

    /// <summary>Gets whether after/mask proxies are available.</summary>
    public bool HasProcessedPreview => AfterPreview is not null;

    /// <summary>Gets whether mutable controls may accept input.</summary>
    public bool IsIdle => !IsBusy && !_batch.IsActive && !_batch.IsDiscovering;

    /// <summary>Gets whether the single-image workspace is visible.</summary>
    public bool IsSingleMode => !IsBatchMode;

    /// <summary>Gets or sets requested CPU worker concurrency; GPU execution is clamped to one.</summary>
    public int BatchConcurrency
    {
        get => _batchConcurrency;
        set
        {
            int clamped = Math.Clamp(value, 1, 2);
            if (SetProperty(ref _batchConcurrency, clamped))
            {
                SyncBatchPreset();
                ScheduleSettingsSave();
            }
        }
    }

    /// <summary>Gets the privacy-safe selected filename.</summary>
    public string InputFileName => InputPath is null
        ? _localization.GetString("Status.NoInput")
        : Path.GetFileName(InputPath);

    /// <summary>Loads models and offline-ready providers once.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        try
        {
            ApplicationSettings settings = await _settingsStore.LoadAsync(cancellationToken);
            CultureOption culture = Cultures.FirstOrDefault(option => option.Name.Equals(
                settings.Culture,
                StringComparison.OrdinalIgnoreCase)) ?? Cultures[0];
            if (!culture.Name.Equals(_localization.CurrentCulture, StringComparison.OrdinalIgnoreCase))
            {
                _localization.SetCulture(culture.Name);
            }

            IReadOnlyList<ModelDescriptor> models = await _modelCatalog.GetAllAsync(cancellationToken);
            IReadOnlyList<InferenceProviderDescriptor> providers =
                await _providerCatalog.GetAllAsync(cancellationToken);

            Models.Clear();
            foreach (ModelDescriptor model in models.OrderBy(model => model.DisplayName, StringComparer.CurrentCulture))
            {
                Models.Add(new ModelOption(
                    model.Id,
                    model.DisplayName,
                    $"{model.Tier} · {model.Input.Width}×{model.Input.Height}",
                    model));
            }

            Providers.Clear();
            Providers.Add(CreateAutoProviderOption());
            foreach (InferenceProviderDescriptor provider in providers)
            {
                Providers.Add(CreateProviderOption(provider));
            }

            RebuildExistingOutputOptions();
            SelectedModel = Models.FirstOrDefault(model => model.Id.Equals(
                settings.ModelId,
                StringComparison.OrdinalIgnoreCase))
                ?? Models.FirstOrDefault(model => model.Id.Equals(
                    "u2netp",
                    StringComparison.OrdinalIgnoreCase))
                ?? Models.FirstOrDefault();
            SelectedProvider = Providers.FirstOrDefault(provider =>
                provider.Kind == settings.Provider
                && (provider.Kind != InferenceProviderKind.DirectMl
                    || provider.AdapterIndex == settings.DirectMlAdapterIndex))
                ?? Providers[0];
            SelectedCulture = culture;
            OutputDirectory = settings.OutputDirectory is not null
                && Directory.Exists(settings.OutputDirectory)
                    ? Path.GetFullPath(settings.OutputDirectory)
                    : null;
            FileNameSuffix = IsSafeSuffix(settings.FileNameSuffix)
                ? settings.FileNameSuffix
                : ".cutlocal";
            ExistingOutputBehavior = Enum.IsDefined(settings.ExistingOutputBehavior)
                ? settings.ExistingOutputBehavior
                : ExistingOutputBehavior.Rename;
            Threshold = Math.Clamp(settings.Threshold, 0, 1);
            FeatherRadius = Math.Clamp(settings.FeatherRadius, 0, 24);
            HardCut = settings.HardCut;
            InvertMask = settings.InvertMask;
            IsLivePreviewEnabled = settings.IsLivePreviewEnabled;
            BatchConcurrency = Math.Clamp(settings.Concurrency, 1, 2);
            _initialized = true;
            SyncBatchPreset();
            await _batch.InitializeAsync(cancellationToken);
            ProcessCommand.NotifyCanExecuteChanged();
            SetStatus("Status.Ready");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            LogUiFailure(_logger, "UI_INITIALIZE", exception);
            SetStatus("Status.InitializationFailed");
        }
    }

    /// <summary>Routes dropped PNG files to the active workspace without dispatcher decoding.</summary>
    public async Task<bool> AcceptDroppedFilesAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!IsIdle)
        {
            return false;
        }

        if (IsBatchMode)
        {
            return await _batch.AcceptDroppedFilesAsync(paths, cancellationToken);
        }

        string[] pngFiles = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => File.Exists(path)
                && Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (pngFiles.Length != 1)
        {
            SetStatus("Status.InvalidDrop");
            return false;
        }

        return await LoadInputAsync(pngFiles[0], ownedCapture: null, cancellationToken);
    }

    /// <summary>Updates drag-target visual state.</summary>
    public void SetDropActive(bool active) => IsDropActive = active && IsIdle;

    /// <summary>Flushes the latest preferences before the window completes shutdown.</summary>
    public async Task ShutdownAsync()
    {
        if (_disposed)
        {
            return;
        }

        _settingsSaveTimer.Stop();
        await _batch.ShutdownAsync();
        if (_initialized)
        {
            try
            {
                await _settingsStore.SaveAsync(CreateSettingsSnapshot(), CancellationToken.None);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ObjectDisposedException)
            {
                LogUiFailure(_logger, "UI_SETTINGS_SHUTDOWN", exception);
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
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Tick -= OnPreviewDebounceElapsed;
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Tick -= OnSettingsSaveElapsed;
        _localization.CultureChanged -= OnCultureChanged;
        _batch.PropertyChanged -= OnBatchPropertyChanged;
        _processingCancellation?.Cancel();
        _processingCancellation?.Dispose();
        _inputPreviewCancellation?.Cancel();
        _inputPreviewCancellation?.Dispose();
        ReleaseOwnedClipboardCapture();
        _batch.Dispose();
        GC.SuppressFinalize(this);
    }

    [RelayCommand(CanExecute = nameof(CanSwitchMode))]
    private void ShowSingleMode() => IsBatchMode = false;

    [RelayCommand(CanExecute = nameof(CanSwitchMode))]
    private void ShowBatchMode() => IsBatchMode = true;

    [RelayCommand(CanExecute = nameof(CanSwitchMode))]
    private async Task OpenModelManagerAsync()
    {
        if (_modelManagerDialog is null)
        {
            return;
        }

        await _modelManagerDialog.ShowAsync(CancellationToken.None);
        string? selectedId = SelectedModel?.Id;
        IReadOnlyList<ModelDescriptor> models = await _modelCatalog.GetAllAsync(CancellationToken.None);
        Models.Clear();
        foreach (ModelDescriptor model in models.OrderBy(
                     model => model.DisplayName,
                     StringComparer.CurrentCulture))
        {
            Models.Add(new ModelOption(
                model.Id,
                model.DisplayName,
                $"{model.Tier} · {model.Input.Width}×{model.Input.Height}",
                model));
        }

        SelectedModel = Models.FirstOrDefault(model => model.Id.Equals(
            selectedId,
            StringComparison.OrdinalIgnoreCase))
            ?? Models.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanSelectInput))]
    private async Task SelectInputAsync()
    {
        string? selected = _fileDialog.SelectPng();
        if (selected is not null)
        {
            await LoadInputAsync(selected, ownedCapture: null, CancellationToken.None);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelectInput))]
    private async Task PasteAsync()
    {
        try
        {
            ClipboardCapture? capture = _clipboard.CapturePng();
            if (capture is null)
            {
                SetStatus("Status.ClipboardEmpty");
                return;
            }

            bool loaded = await LoadInputAsync(capture.Path, capture, CancellationToken.None);
            if (!loaded && capture.IsTemporary)
            {
                _clipboard.Release(capture);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or System.Runtime.InteropServices.COMException)
        {
            LogUiFailure(_logger, "UI_CLIPBOARD", exception);
            SetStatus("Status.ClipboardEmpty");
        }
    }

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private Task ProcessAsync() => ProcessCoreAsync(isLivePreview: false);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        SetStatus("Status.WaitingCancel");
        _processingCancellation?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanClearInput))]
    private void ClearInput()
    {
        _inputPreviewCancellation?.Cancel();
        ReleaseOwnedClipboardCapture();
        InputPath = null;
        OutputPath = null;
        BeforePreview = null;
        AfterPreview = null;
        MaskPreview = null;
        ProgressValue = 0;
        LastProviderText = "—";
        LastElapsedText = "—";
        SetStatus("Status.Ready");
    }

    [RelayCommand(CanExecute = nameof(CanBrowseOutput))]
    private void BrowseOutputFolder()
    {
        string? selected = _fileDialog.SelectOutputFolder(OutputDirectory);
        if (selected is not null)
        {
            OutputDirectory = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenOutputFolder))]
    private void OpenOutputFolder()
    {
        try
        {
            _fileLauncher.OpenContainingFolder(OutputPath!);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception
            or System.Runtime.InteropServices.COMException)
        {
            LogUiFailure(_logger, "UI_OPEN_OUTPUT", exception);
            SetStatus("Status.OpenFolderFailed");
        }
    }

    private async Task<bool> LoadInputAsync(
        string path,
        ClipboardCapture? ownedCapture,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous = Interlocked.Exchange(ref _inputPreviewCancellation, current);
        previous?.Cancel();
        previous?.Dispose();
        try
        {
            SetStatus("Status.LoadingPreview");
            BitmapSource proxy = await _previewBitmaps.LoadColorAsync(
                path,
                PreviewMaximumEdge,
                current.Token);
            ReleaseOwnedClipboardCapture();
            _ownedClipboardCapture = ownedCapture;
            InputPath = Path.GetFullPath(path);
            OutputDirectory ??= Path.GetDirectoryName(InputPath);
            OutputPath = null;
            BeforePreview = proxy;
            AfterPreview = null;
            MaskPreview = null;
            ProgressValue = 0;
            LastProviderText = "—";
            LastElapsedText = "—";
            SetStatus("Status.Selected");
            return true;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            LogUiFailure(_logger, "UI_PREVIEW_LOAD", exception);
            SetStatus("Status.InvalidDrop");
            return false;
        }
        finally
        {
            if (ReferenceEquals(_inputPreviewCancellation, current))
            {
                _inputPreviewCancellation = null;
            }

            current.Dispose();
        }
    }

    private async Task ProcessCoreAsync(bool isLivePreview)
    {
        if (!CanProcess())
        {
            return;
        }

        _previewDebounceTimer.Stop();
        _processingCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _processingCancellation = cancellation;
        _isLivePreviewRun = isLivePreview;
        IsBusy = true;
        if (!isLivePreview)
        {
            ProgressValue = 0;
        }

        SetStatus(isLivePreview ? "Status.LivePreview" : "Status.PreparingModel");
        Stopwatch elapsed = Stopwatch.StartNew();
        object progressGate = new();
        bool acceptProgress = true;
        try
        {
            string requestedOutput = isLivePreview && OutputPath is not null
                ? OutputPath
                : OutputPathPolicy.CreatePngPath(InputPath!, OutputDirectory!, FileNameSuffix);
            SingleImageProcessingOptions options = new()
            {
                ModelId = SelectedModel!.Id,
                Provider = SelectedProvider!.Kind,
                DirectMlAdapterIndex = SelectedProvider.AdapterIndex,
                ExistingOutputBehavior = isLivePreview
                    ? ExistingOutputBehavior.Overwrite
                    : ExistingOutputBehavior,
                MaskOptions = new MaskRefinementOptions
                {
                    Threshold = Threshold,
                    FeatherRadius = FeatherRadius,
                    HardCut = HardCut,
                    Invert = InvertMask,
                },
            };
            Progress<ProcessingProgress> progress = new(update =>
            {
                lock (progressGate)
                {
                    if (!acceptProgress)
                    {
                        return;
                    }

                    ProgressValue = update.Value;
                    SetStatus(update.MessageKey);
                }
            });
            ProcessingResult result = await _removeBackground.ExecuteAsync(
                InputPath!,
                requestedOutput,
                options,
                progress,
                cancellation.Token);
            if (result.Outcome == ProcessingOutcome.Succeeded && result.OutputPath is not null)
            {
                Task<BitmapSource> afterTask = _previewBitmaps.LoadColorAsync(
                        result.OutputPath,
                        PreviewMaximumEdge,
                        cancellation.Token)
                    .AsTask();
                Task<BitmapSource> maskTask = _previewBitmaps.LoadAlphaMaskAsync(
                        result.OutputPath,
                        PreviewMaximumEdge,
                        cancellation.Token)
                    .AsTask();
                await Task.WhenAll(afterTask, maskTask);
                OutputPath = result.OutputPath;
                AfterPreview = afterTask.Result;
                MaskPreview = maskTask.Result;
                LastProviderText = ResolveProviderDisplayName(result.ProviderId);
                LastElapsedText = elapsed.Elapsed.ToString("m\\:ss\\.fff", CultureInfo.CurrentCulture);
                lock (progressGate)
                {
                    acceptProgress = false;
                    ProgressValue = 1;
                    SetStatus("Status.Completed");
                }
            }
            else if (result.Outcome == ProcessingOutcome.Cancelled)
            {
                lock (progressGate)
                {
                    acceptProgress = false;
                    if (!isLivePreview)
                    {
                        SetStatus("Status.Cancelled");
                    }
                }
            }
            else
            {
                lock (progressGate)
                {
                    acceptProgress = false;
                    StatusText = CurrentErrorMessage(result.Error);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            lock (progressGate)
            {
                acceptProgress = false;
                if (!isLivePreview)
                {
                    SetStatus("Status.Cancelled");
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException)
        {
            LogUiFailure(_logger, "UI_PROCESS", exception);
            lock (progressGate)
            {
                acceptProgress = false;
                StatusText = exception.Message;
            }
        }
        finally
        {
            lock (progressGate)
            {
                acceptProgress = false;
            }

            elapsed.Stop();
            if (ReferenceEquals(_processingCancellation, cancellation))
            {
                _processingCancellation = null;
            }

            cancellation.Dispose();
            _isLivePreviewRun = false;
            IsBusy = false;
        }
    }

    private bool CanSelectInput() => IsIdle && !IsBatchMode;

    private bool CanProcess() => !IsBusy
        && !_batch.IsActive
        && !_batch.IsDiscovering
        && !IsBatchMode
        && _initialized
        && InputPath is not null
        && OutputDirectory is not null
        && SelectedModel is not null
        && SelectedProvider is not null;

    private bool CanCancel() => IsBusy;

    private bool CanClearInput() => IsIdle && !IsBatchMode && InputPath is not null;

    private bool CanOpenOutputFolder() => OutputPath is not null;

    private bool CanBrowseOutput() => IsIdle;

    private bool CanSwitchMode() => IsIdle;

    private void ScheduleLivePreview()
    {
        if (!IsLivePreviewEnabled || AfterPreview is null || InputPath is null)
        {
            return;
        }

        _previewDebounceTimer.Stop();
        if (_isLivePreviewRun)
        {
            _processingCancellation?.Cancel();
        }

        _previewDebounceTimer.Start();
    }

    private async void OnPreviewDebounceElapsed(object? sender, EventArgs e)
    {
        _previewDebounceTimer.Stop();
        if (IsBusy)
        {
            _previewDebounceTimer.Start();
            return;
        }

        try
        {
            await ProcessCoreAsync(isLivePreview: true);
        }
        catch (Exception exception)
        {
            LogUiFailure(_logger, "UI_LIVE_PREVIEW", exception);
            SetStatus("Status.Ready");
        }
    }

    private void ScheduleSettingsSave()
    {
        if (!_initialized || _disposed)
        {
            return;
        }

        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private async void OnSettingsSaveElapsed(object? sender, EventArgs e)
    {
        _settingsSaveTimer.Stop();
        if (_disposed)
        {
            return;
        }

        try
        {
            await _settingsStore.SaveAsync(CreateSettingsSnapshot(), CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ObjectDisposedException)
        {
            LogUiFailure(_logger, "UI_SETTINGS_SAVE", exception);
        }
    }

    private ApplicationSettings CreateSettingsSnapshot() => new()
    {
        Culture = _localization.CurrentCulture,
        ModelId = SelectedModel?.Id ?? "u2netp",
        Provider = SelectedProvider?.Kind ?? InferenceProviderKind.Auto,
        DirectMlAdapterIndex = SelectedProvider?.AdapterIndex,
        OutputDirectory = OutputDirectory,
        FileNameSuffix = FileNameSuffix,
        ExistingOutputBehavior = ExistingOutputBehavior,
        Threshold = Threshold,
        FeatherRadius = FeatherRadius,
        HardCut = HardCut,
        InvertMask = InvertMask,
        IsLivePreviewEnabled = IsLivePreviewEnabled,
        Concurrency = BatchConcurrency,
    };

    private void SyncBatchPreset()
    {
        if (SelectedModel is null
            || SelectedProvider is null
            || string.IsNullOrWhiteSpace(OutputDirectory))
        {
            _batch.ConfigurePreset(null);
            return;
        }

        _batch.ConfigurePreset(new ProcessingPreset
        {
            Name = "desktop-batch",
            ModelId = SelectedModel.Id,
            Provider = SelectedProvider.Kind,
            DirectMlAdapterIndex = SelectedProvider.AdapterIndex,
            Concurrency = BatchConcurrency,
            Mask = new MaskRefinementOptions
            {
                Threshold = Threshold,
                FeatherRadius = FeatherRadius,
                HardCut = HardCut,
                Invert = InvertMask,
            },
            Output = new OutputConfiguration
            {
                Format = OutputFormat.Png,
                OutputDirectory = Path.GetFullPath(OutputDirectory),
                FileNameSuffix = FileNameSuffix,
                ExistingOutputBehavior = ExistingOutputBehavior,
                PreserveDimensions = true,
            },
        });
    }

    private void OnBatchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BatchWorkspaceViewModel.IsActive)
            or nameof(BatchWorkspaceViewModel.IsDiscovering)
            or nameof(BatchWorkspaceViewModel.IsExternalBusy))
        {
            OnPropertyChanged(nameof(IsIdle));
            SelectInputCommand.NotifyCanExecuteChanged();
            PasteCommand.NotifyCanExecuteChanged();
            ProcessCommand.NotifyCanExecuteChanged();
            ClearInputCommand.NotifyCanExecuteChanged();
            BrowseOutputFolderCommand.NotifyCanExecuteChanged();
            ShowSingleModeCommand.NotifyCanExecuteChanged();
            ShowBatchModeCommand.NotifyCanExecuteChanged();
            OpenModelManagerCommand.NotifyCanExecuteChanged();
        }
    }

    private static bool IsSafeSuffix(string? suffix) =>
        !string.IsNullOrWhiteSpace(suffix)
        && suffix.Length <= 64
        && !suffix.Contains("..", StringComparison.Ordinal)
        && suffix.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !suffix.Contains(Path.DirectorySeparatorChar)
        && !suffix.Contains(Path.AltDirectorySeparatorChar);

    private void SetStatus(string resourceKey)
    {
        _lastStatusKey = resourceKey;
        StatusText = _localization.GetString(resourceKey);
    }

    private string CurrentErrorMessage(ProcessingError? error)
    {
        if (error is null)
        {
            return _localization.GetString("Status.InitializationFailed");
        }

        return _localization.CurrentCulture.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            ? error.MessageTr
            : error.MessageEn;
    }

    private string ResolveProviderDisplayName(string? providerId) =>
        Providers.FirstOrDefault(provider => provider.Id.Equals(
            providerId,
            StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? providerId
        ?? "—";

    private ProviderOption CreateAutoProviderOption() => new(
        InferenceProviderKind.Auto,
        "auto",
        _localization.CurrentCulture.StartsWith("tr", StringComparison.OrdinalIgnoreCase)
            ? "Otomatik · En uygun yerel GPU, sonra CPU"
            : "Auto · Best local GPU, then CPU",
        AdapterIndex: null,
        Descriptor: null);

    private static ProviderOption CreateProviderOption(InferenceProviderDescriptor provider)
    {
        string memory = provider.DedicatedVideoMemoryBytes > 0
            ? $" · {provider.DedicatedVideoMemoryBytes / 1024d / 1024d / 1024d:F1} GB"
            : string.Empty;
        return new ProviderOption(
            provider.Kind,
            provider.Id,
            $"{provider.Kind} · {provider.DisplayName}{memory}",
            provider.DeviceIndex,
            provider);
    }

    private void RebuildExistingOutputOptions()
    {
        ExistingOutputBehavior selected = ExistingOutputBehavior;
        bool turkish = _localization.CurrentCulture.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
        ExistingOutputOptions.Clear();
        ExistingOutputOptions.Add(new ExistingOutputOption(
            ExistingOutputBehavior.Rename,
            turkish ? "Yeni ad üret" : "Create a unique name"));
        ExistingOutputOptions.Add(new ExistingOutputOption(
            ExistingOutputBehavior.Overwrite,
            turkish ? "Üzerine yaz" : "Overwrite"));
        ExistingOutputOptions.Add(new ExistingOutputOption(
            ExistingOutputBehavior.Skip,
            turkish ? "Atla" : "Skip"));
        ExistingOutputBehavior = selected;
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        SetStatus(_lastStatusKey);
        OnPropertyChanged(nameof(InputFileName));
        RebuildExistingOutputOptions();
        if (Providers.Count > 0)
        {
            ProviderOption? selected = SelectedProvider;
            Providers[0] = CreateAutoProviderOption();
            if (selected?.Kind == InferenceProviderKind.Auto)
            {
                SelectedProvider = Providers[0];
            }
        }
    }

    partial void OnSelectedModelChanged(ModelOption? value)
    {
        SyncBatchPreset();
        ScheduleLivePreview();
        ScheduleSettingsSave();
    }

    partial void OnSelectedProviderChanged(ProviderOption? value)
    {
        SyncBatchPreset();
        ScheduleLivePreview();
        ScheduleSettingsSave();
    }

    partial void OnSelectedCultureChanged(CultureOption? value)
    {
        if (value is not null
            && !value.Name.Equals(_localization.CurrentCulture, StringComparison.OrdinalIgnoreCase))
        {
            _localization.SetCulture(value.Name);
        }

        ScheduleSettingsSave();
    }

    partial void OnOutputDirectoryChanged(string? value)
    {
        SyncBatchPreset();
        ScheduleSettingsSave();
    }

    partial void OnFileNameSuffixChanged(string value)
    {
        SyncBatchPreset();
        ScheduleSettingsSave();
    }

    partial void OnExistingOutputBehaviorChanged(ExistingOutputBehavior value)
    {
        SyncBatchPreset();
        ScheduleSettingsSave();
    }

    partial void OnThresholdChanged(double value)
    {
        SyncBatchPreset();
        ScheduleLivePreview();
        ScheduleSettingsSave();
    }

    partial void OnFeatherRadiusChanged(double value)
    {
        SyncBatchPreset();
        ScheduleLivePreview();
        ScheduleSettingsSave();
    }

    partial void OnHardCutChanged(bool value)
    {
        SyncBatchPreset();
        ScheduleLivePreview();
        ScheduleSettingsSave();
    }

    partial void OnInvertMaskChanged(bool value)
    {
        SyncBatchPreset();
        ScheduleLivePreview();
        ScheduleSettingsSave();
    }

    partial void OnIsBusyChanged(bool value)
    {
        _batch.IsExternalBusy = value;
        ShowSingleModeCommand.NotifyCanExecuteChanged();
        ShowBatchModeCommand.NotifyCanExecuteChanged();
        OpenModelManagerCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBatchModeChanged(bool value)
    {
        SetDropActive(false);
        SelectInputCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        ProcessCommand.NotifyCanExecuteChanged();
        ClearInputCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLivePreviewEnabledChanged(bool value)
    {
        if (!value)
        {
            _previewDebounceTimer.Stop();
            if (_isLivePreviewRun)
            {
                _processingCancellation?.Cancel();
            }
        }

        ScheduleSettingsSave();
    }

    private void ReleaseOwnedClipboardCapture()
    {
        ClipboardCapture? capture = Interlocked.Exchange(ref _ownedClipboardCapture, null);
        if (capture is not null)
        {
            try
            {
                _clipboard.Release(capture);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogUiFailure(_logger, "UI_CLIPBOARD_RELEASE", exception);
            }
        }
    }
}

/// <summary>Displays one manifest-backed model choice.</summary>
public sealed record ModelOption(
    string Id,
    string DisplayName,
    string Detail,
    ModelDescriptor Descriptor);

/// <summary>Displays Auto or one concrete provider/device choice.</summary>
public sealed record ProviderOption(
    InferenceProviderKind Kind,
    string Id,
    string DisplayName,
    int? AdapterIndex,
    InferenceProviderDescriptor? Descriptor);

/// <summary>Displays a localized existing-output behavior.</summary>
public sealed record ExistingOutputOption(
    ExistingOutputBehavior Behavior,
    string DisplayName);
