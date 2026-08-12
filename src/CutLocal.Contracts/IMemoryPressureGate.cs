namespace CutLocal.Contracts;

/// <summary>Delays admission of new batch items while local memory pressure is critical.</summary>
public interface IMemoryPressureGate
{
    /// <summary>Completes when a new full-resolution item may safely enter the pipeline.</summary>
    ValueTask WaitForCapacityAsync(CancellationToken cancellationToken);
}
