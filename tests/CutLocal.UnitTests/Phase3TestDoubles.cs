using System.Windows.Media;
using System.Windows.Media.Imaging;
using CutLocal.App;
using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.UnitTests;

internal sealed class Phase3TestContext
{
    public Phase3TestContext(
        ApplicationSettings? settings = null,
        bool blockProcessorUntilCancellation = false)
    {
        Processor = new TestProcessor(blockProcessorUntilCancellation);
        SettingsStore = new TestSettingsStore(settings ?? new ApplicationSettings());
        Preview = new TestPreviewBitmapService();
        TestLocalizationService localization = new();
        TestFileDialogService dialogs = new();
        TestFileLauncher launcher = new();
        TestProcessingJobStore jobStore = new();
        JobStore = jobStore;
        TimeProvider timeProvider = TimeProvider.System;
        RemoveBackgroundUseCase removeBackground = new(
            Processor,
            NullLogger<RemoveBackgroundUseCase>.Instance);
        Batch = new BatchWorkspaceViewModel(
            new AddImagesUseCase(timeProvider),
            new AddFolderUseCase(new AddImagesUseCase(timeProvider)),
            new RetryFailedItemsUseCase(timeProvider),
            new RemoveBatchItemsUseCase(timeProvider),
            new ReconfigureBatchUseCase(timeProvider),
            new RecoverInterruptedJobUseCase(jobStore, timeProvider),
            new ProcessBatchUseCase(
                removeBackground,
                jobStore,
                timeProvider,
                NullLogger<ProcessBatchUseCase>.Instance),
            jobStore,
            dialogs,
            launcher,
            localization,
            NullLogger<BatchWorkspaceViewModel>.Instance);
        ViewModel = new MainWindowViewModel(
            removeBackground,
            Batch,
            dialogs,
            new TestClipboardService(),
            Preview,
            launcher,
            new TestModelCatalog(),
            new TestProviderCatalog(),
            SettingsStore,
            localization,
            NullLogger<MainWindowViewModel>.Instance);
    }

    public MainWindowViewModel ViewModel { get; }

    public BatchWorkspaceViewModel Batch { get; }

    public TestProcessor Processor { get; }

    public TestSettingsStore SettingsStore { get; }

    public TestPreviewBitmapService Preview { get; }

    public TestProcessingJobStore JobStore { get; }
}

internal sealed class TestProcessor(bool blockUntilCancellation) : IRemoveBackgroundProcessor
{
    private readonly bool _blockUntilCancellation = blockUntilCancellation;

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RemoveBackgroundRequest? LastRequest { get; private set; }

    public async ValueTask<ProcessingResult> ProcessAsync(
        RemoveBackgroundRequest request,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        Started.TrySetResult();
        progress?.Report(new ProcessingProgress
        {
            Status = ItemStatus.Inferring,
            Value = 0.6,
            MessageKey = "Status.Inferring",
        });
        if (_blockUntilCancellation)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new ProcessingResult { Outcome = ProcessingOutcome.Cancelled };
            }
        }

        return new ProcessingResult
        {
            Outcome = ProcessingOutcome.Succeeded,
            OutputPath = request.OutputPath,
            ProviderId = request.Provider == InferenceProviderKind.DirectMl ? "dml-1" : "cpu",
        };
    }
}

internal sealed class TestModelCatalog : IModelCatalog
{
    private static readonly ModelDescriptor Model = new()
    {
        Id = "u2netp",
        DisplayName = "U2NetP Fast",
        Version = "1",
        FileName = "u2netp.onnx",
        Sha256 = new string('A', 64),
        DownloadUrl = "https://example.invalid/u2netp.onnx",
        License = new ModelLicenseDescriptor
        {
            Spdx = "Apache-2.0",
            CommercialUseAllowed = true,
            AttributionRequired = true,
            Source = "https://example.invalid/license",
        },
        Input = new ModelInputDescriptor
        {
            Width = 320,
            Height = 320,
            Layout = "NCHW",
            ColorOrder = "RGB",
            Mean = [0.485, 0.456, 0.406],
            Std = [0.229, 0.224, 0.225],
            ResizeMode = "stretch",
        },
        Output = new ModelOutputDescriptor { Activation = "minmax", Type = "alpha-mask" },
        RecommendedMemoryMb = 1024,
        Tier = "fast",
        SupportedProviders = ["cpu", "directml"],
    };

    public ValueTask<ModelDescriptor?> GetByIdAsync(
        string modelId,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<ModelDescriptor?>(
            modelId.Equals(Model.Id, StringComparison.OrdinalIgnoreCase) ? Model : null);

    public ValueTask<IReadOnlyList<ModelDescriptor>> GetAllAsync(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ModelDescriptor>>([Model]);
}

internal sealed class TestProviderCatalog : IInferenceProviderCatalog
{
    private static readonly IReadOnlyList<InferenceProviderDescriptor> Providers =
    [
        new()
        {
            Kind = InferenceProviderKind.DirectMl,
            Id = "dml-1",
            DisplayName = "Test GPU",
            IsReadyOffline = true,
            MaxRecommendedConcurrency = 1,
            DeviceIndex = 1,
            DedicatedVideoMemoryBytes = 4L * 1024 * 1024 * 1024,
        },
        new()
        {
            Kind = InferenceProviderKind.Cpu,
            Id = "cpu",
            DisplayName = "Bundled CPU",
            IsReadyOffline = true,
            MaxRecommendedConcurrency = 1,
        },
    ];

    public ValueTask<IReadOnlyList<InferenceProviderDescriptor>> GetAllAsync(
        CancellationToken cancellationToken) => ValueTask.FromResult(Providers);
}

internal sealed class TestSettingsStore(ApplicationSettings settings) : IApplicationSettingsStore
{
    public ApplicationSettings Settings { get; private set; } = settings;

    public ValueTask<ApplicationSettings> LoadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Settings);

    public ValueTask SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        Settings = settings;
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestProcessingJobStore : IProcessingJobStore
{
    public ProcessingJob? Job { get; private set; }

    public int SaveCount { get; private set; }

    public ValueTask<ProcessingJob?> LoadLastAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Job);

    public ValueTask SaveAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        Job = job;
        SaveCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken)
    {
        Job = null;
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestPreviewBitmapService : IPreviewBitmapService
{
    private static readonly BitmapSource Preview = CreatePreview();

    public int ColorLoadCount { get; private set; }

    public ValueTask<BitmapSource> LoadColorAsync(
        string path,
        int maximumEdge,
        CancellationToken cancellationToken)
    {
        ColorLoadCount++;
        return ValueTask.FromResult(Preview);
    }

    public ValueTask<BitmapSource> LoadAlphaMaskAsync(
        string path,
        int maximumEdge,
        CancellationToken cancellationToken) => ValueTask.FromResult(Preview);

    private static BitmapSource CreatePreview()
    {
        byte[] pixels =
        [
            0x60, 0x70, 0x80, 0xFF, 0x60, 0x70, 0x80, 0xFF,
            0x60, 0x70, 0x80, 0xFF, 0x60, 0x70, 0x80, 0xFF,
        ];
        BitmapSource preview = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride: 8);
        preview.Freeze();
        return preview;
    }
}

internal sealed class TestLocalizationService : ILocalizationService
{
    public IReadOnlyList<CultureOption> Cultures { get; } =
    [new("tr-TR", "Türkçe"), new("en-US", "English")];

    public string CurrentCulture { get; private set; } = "tr-TR";

    public event EventHandler? CultureChanged;

    public string GetString(string key) => key;

    public void SetCulture(string cultureName)
    {
        CurrentCulture = cultureName;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class TestFileDialogService : IFileDialogService
{
    public string? SelectPng() => null;

    public IReadOnlyList<string> SelectPngFiles() => [];

    public string? SelectInputFolder() => null;

    public string? SelectOutputFolder(string? initialDirectory) => null;
}

internal sealed class TestClipboardService : IClipboardService
{
    public ClipboardCapture? CapturePng() => null;

    public void Release(ClipboardCapture capture)
    {
    }
}

internal sealed class TestFileLauncher : IFileLauncher
{
    public void OpenContainingFolder(string filePath)
    {
    }
}
