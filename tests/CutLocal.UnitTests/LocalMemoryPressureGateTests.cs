using CutLocal.Infrastructure;

namespace CutLocal.UnitTests;

public sealed class LocalMemoryPressureGateTests
{
    [Theory]
    [InlineData(800, 800, 2_000, true)]
    [InlineData(1_900, 2_000, 2_000, true)]
    [InlineData(1_000, 1_500, 2_000, false)]
    [InlineData(-1, 1_500, 2_000, false)]
    [InlineData(1_000, 1_500, 0, false)]
    public void IsCritical_EvaluatesRuntimeThresholdAndReserve(
        long memoryLoadMiB,
        long highLoadThresholdMiB,
        long totalAvailableMiB,
        bool expected)
    {
        const long oneMiB = 1024 * 1024;
        long memoryLoadBytes = memoryLoadMiB < 0 ? memoryLoadMiB : memoryLoadMiB * oneMiB;
        long highLoadThresholdBytes = highLoadThresholdMiB * oneMiB;
        long totalAvailableBytes = totalAvailableMiB * oneMiB;
        bool actual = LocalMemoryPressureGate.IsCritical(
            memoryLoadBytes,
            highLoadThresholdBytes,
            totalAvailableBytes);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsCritical_PausesWhenReserveFallsBelowConfiguredMinimum()
    {
        long total = 2L * 1024 * 1024 * 1024;
        long load = total - LocalMemoryPressureGate.MinimumReserveBytes + 1;

        Assert.True(LocalMemoryPressureGate.IsCritical(load, total, total));
        Assert.False(LocalMemoryPressureGate.IsCritical(
            total - LocalMemoryPressureGate.MinimumReserveBytes,
            total,
            total));
    }
}
