using CutLocal.Contracts;
using Microsoft.Extensions.Logging;

namespace CutLocal.Infrastructure;

/// <summary>Uses runtime memory-load telemetry to pause new batch admission under critical pressure.</summary>
public sealed class LocalMemoryPressureGate : IMemoryPressureGate
{
    /// <summary>Gets the minimum free-memory reserve used by the admission gate.</summary>
    public const long MinimumReserveBytes = 256L * 1024 * 1024;

    private static readonly Action<ILogger, Exception?> LogWaiting = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(4301, nameof(LogWaiting)),
        "Critical local memory pressure detected; pausing admission of new batch items");
    private static readonly Action<ILogger, Exception?> LogResumed = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(4302, nameof(LogResumed)),
        "Local memory pressure recovered; resuming batch admission");

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LocalMemoryPressureGate> _logger;

    /// <summary>Initializes the local memory admission gate.</summary>
    public LocalMemoryPressureGate(
        TimeProvider timeProvider,
        ILogger<LocalMemoryPressureGate> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask WaitForCapacityAsync(CancellationToken cancellationToken)
    {
        bool logged = false;
        while (true)
        {
            GCMemoryInfo information = GC.GetGCMemoryInfo();
            if (!IsCritical(
                    information.MemoryLoadBytes,
                    information.HighMemoryLoadThresholdBytes,
                    information.TotalAvailableMemoryBytes))
            {
                if (logged)
                {
                    LogResumed(_logger, null);
                }

                return;
            }

            if (!logged)
            {
                LogWaiting(_logger, null);
                logged = true;
            }

            await Task.Delay(
                    TimeSpan.FromMilliseconds(250),
                    _timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Evaluates one runtime memory snapshot without allocating or changing process state.</summary>
    public static bool IsCritical(
        long memoryLoadBytes,
        long highLoadThresholdBytes,
        long totalAvailableBytes)
    {
        if (memoryLoadBytes < 0 || highLoadThresholdBytes < 0 || totalAvailableBytes <= 0)
        {
            return false;
        }

        bool crossedRuntimeThreshold = highLoadThresholdBytes > 0
            && memoryLoadBytes >= highLoadThresholdBytes;
        long reserve = Math.Max(0, totalAvailableBytes - memoryLoadBytes);
        return crossedRuntimeThreshold || reserve < MinimumReserveBytes;
    }
}
