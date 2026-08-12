using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.UnitTests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProcessCommand_PropagatesUiSelectionsAndLoadsComparisonProxies()
    {
        Directory.CreateDirectory(_root);
        string input = Path.Combine(_root, "ürün.png");
        await File.WriteAllBytesAsync(input, [0x89]);
        Phase3TestContext context = new(new ApplicationSettings
        {
            Culture = "en-US",
            Provider = InferenceProviderKind.DirectMl,
            DirectMlAdapterIndex = 1,
            Threshold = 0.61,
            FeatherRadius = 2.5,
            HardCut = true,
            FileNameSuffix = ".clean",
            ExistingOutputBehavior = ExistingOutputBehavior.Overwrite,
        });
        using (context.ViewModel)
        {
            await context.ViewModel.InitializeAsync(CancellationToken.None);
            Assert.True(await context.ViewModel.AcceptDroppedFilesAsync([input]));

            await context.ViewModel.ProcessCommand.ExecuteAsync(null);

            RemoveBackgroundRequest request = Assert.IsType<RemoveBackgroundRequest>(
                context.Processor.LastRequest);
            Assert.Equal(InferenceProviderKind.DirectMl, request.Provider);
            Assert.Equal(1, request.DirectMlAdapterIndex);
            Assert.Equal(0.61, request.MaskOptions.Threshold);
            Assert.Equal(2.5, request.MaskOptions.FeatherRadius);
            Assert.True(request.MaskOptions.HardCut);
            Assert.Equal(ExistingOutputBehavior.Overwrite, request.ExistingOutputBehavior);
            Assert.EndsWith("ürün.clean.png", request.OutputPath, StringComparison.Ordinal);
            Assert.NotNull(context.ViewModel.BeforePreview);
            Assert.NotNull(context.ViewModel.AfterPreview);
            Assert.NotNull(context.ViewModel.MaskPreview);
            Assert.Equal(1, context.ViewModel.ProgressValue);
            Assert.False(context.ViewModel.IsBusy);
        }
    }

    [Fact]
    public async Task CancelCommand_CancelsTheActiveProcessor()
    {
        Directory.CreateDirectory(_root);
        string input = Path.Combine(_root, "input.png");
        await File.WriteAllBytesAsync(input, [0x89]);
        Phase3TestContext context = new(blockProcessorUntilCancellation: true);
        using (context.ViewModel)
        {
            await context.ViewModel.InitializeAsync(CancellationToken.None);
            Assert.True(await context.ViewModel.AcceptDroppedFilesAsync([input]));

            Task processing = context.ViewModel.ProcessCommand.ExecuteAsync(null);
            await context.Processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.ViewModel.CancelCommand.Execute(null);
            await processing.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(context.ViewModel.IsBusy);
            Assert.Equal("Status.Cancelled", context.ViewModel.StatusText);
        }
    }

    [Fact]
    public async Task AcceptDroppedFilesAsync_RejectsMultiplePngFilesWithoutDecoding()
    {
        Directory.CreateDirectory(_root);
        string first = Path.Combine(_root, "first.png");
        string second = Path.Combine(_root, "second.png");
        await File.WriteAllBytesAsync(first, [0x89]);
        await File.WriteAllBytesAsync(second, [0x89]);
        Phase3TestContext context = new();
        using (context.ViewModel)
        {
            await context.ViewModel.InitializeAsync(CancellationToken.None);

            bool accepted = await context.ViewModel.AcceptDroppedFilesAsync([first, second]);

            Assert.False(accepted);
            Assert.Equal(0, context.Preview.ColorLoadCount);
            Assert.Equal("Status.InvalidDrop", context.ViewModel.StatusText);
        }
    }

    [Fact]
    public async Task ShutdownAsync_FlushesTheLatestSettingsWithoutWaitingForDebounce()
    {
        Phase3TestContext context = new();
        await context.ViewModel.InitializeAsync(CancellationToken.None);
        context.ViewModel.Threshold = 0.73;
        context.ViewModel.FeatherRadius = 4.5;
        context.ViewModel.FileNameSuffix = ".son";

        await context.ViewModel.ShutdownAsync();

        Assert.Equal(0.73, context.SettingsStore.Settings.Threshold);
        Assert.Equal(4.5, context.SettingsStore.Settings.FeatherRadius);
        Assert.Equal(".son", context.SettingsStore.Settings.FileNameSuffix);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
