using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.UnitTests;

public sealed class RemoveBackgroundUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsIncompleteTaskWhileProcessorRuns()
    {
        BlockingProcessor processor = new();
        RemoveBackgroundUseCase useCase = new(
            processor,
            NullLogger<RemoveBackgroundUseCase>.Instance);

        Task<ProcessingResult> processingTask = useCase.ExecuteAsync(
                Path.Combine(Path.GetTempPath(), "cutlocal-input.png"),
                outputPath: null,
                progress: null,
                CancellationToken.None)
            .AsTask();

        Assert.False(processingTask.IsCompleted);
        processor.Complete();
        ProcessingResult result = await processingTask;
        Assert.Equal(ProcessingOutcome.Succeeded, result.Outcome);
    }

    private sealed class BlockingProcessor : IRemoveBackgroundProcessor
    {
        private readonly TaskCompletionSource<ProcessingResult> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ProcessingResult> ProcessAsync(
            RemoveBackgroundRequest request,
            IProgress<ProcessingProgress>? progress,
            CancellationToken cancellationToken) => new(_completion.Task);

        public void Complete() => _completion.SetResult(new ProcessingResult
        {
            Outcome = ProcessingOutcome.Succeeded,
            OutputPath = "cutlocal-output.png",
        });
    }
}
