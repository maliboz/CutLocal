using CutLocal.Domain;

namespace CutLocal.UnitTests;

public sealed class BatchWorkspaceViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task BatchMode_AcceptsMultipleDroppedPngsPersistsQueueAndConcurrency()
    {
        Directory.CreateDirectory(_root);
        string first = await CreatePngAsync("first.png");
        string second = await CreatePngAsync("second.png");
        Phase3TestContext context = new();
        await context.ViewModel.InitializeAsync(CancellationToken.None);
        context.ViewModel.OutputDirectory = _root;
        context.ViewModel.BatchConcurrency = 2;
        context.ViewModel.ShowBatchModeCommand.Execute(null);

        bool accepted = await context.ViewModel.AcceptDroppedFilesAsync([first, second, first]);

        Assert.True(accepted);
        Assert.True(context.ViewModel.IsBatchMode);
        Assert.Equal(2, context.Batch.Items.Count);
        Assert.Equal("0 / 2", context.Batch.SummaryText);
        Assert.Equal(2, context.Batch.CurrentJob?.Items.Count);
        Assert.Equal(2, context.Batch.CurrentJob?.Preset.Concurrency);
        Assert.True(context.JobStore.SaveCount >= 1);

        await context.ViewModel.ShutdownAsync();
        Assert.Equal(2, context.SettingsStore.Settings.Concurrency);
    }

    [Fact]
    public async Task ActiveBatch_LocksModeSwitchAndCancellationProducesDurableTerminalSnapshot()
    {
        Directory.CreateDirectory(_root);
        string input = await CreatePngAsync("blocking.png");
        Phase3TestContext context = new(blockProcessorUntilCancellation: true);
        using (context.ViewModel)
        {
            await context.ViewModel.InitializeAsync(CancellationToken.None);
            context.ViewModel.OutputDirectory = _root;
            context.ViewModel.ShowBatchModeCommand.Execute(null);
            Assert.True(await context.ViewModel.AcceptDroppedFilesAsync([input]));

            Task execution = context.Batch.StartBatchCommand.ExecuteAsync(null);
            await context.Processor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(context.Batch.IsActive);
            Assert.False(context.ViewModel.ShowSingleModeCommand.CanExecute(null));
            Assert.False(context.ViewModel.IsIdle);
            context.Batch.CancelBatchCommand.Execute(null);
            await execution.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(context.Batch.IsActive);
            Assert.Equal(JobStatus.Cancelled, context.Batch.CurrentJob?.Status);
            Assert.Equal(ItemStatus.Cancelled, context.Batch.CurrentJob?.Items[0].Status);
            Assert.Equal(context.Batch.CurrentJob, context.JobStore.Job);
            Assert.True(context.ViewModel.ShowSingleModeCommand.CanExecute(null));
        }
    }

    private async Task<string> CreatePngAsync(string fileName)
    {
        string path = Path.Combine(_root, fileName);
        await File.WriteAllBytesAsync(path, [0x89]);
        return path;
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
