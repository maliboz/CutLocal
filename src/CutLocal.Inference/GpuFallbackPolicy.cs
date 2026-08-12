using CutLocal.Domain;

namespace CutLocal.Inference;

/// <summary>Centralizes the one-attempt GPU-to-CPU recovery rule.</summary>
public static class GpuFallbackPolicy
{
    /// <summary>Returns whether the item may consume its single CPU fallback attempt.</summary>
    public static bool ShouldRetryOnCpu(
        InferenceProviderDescriptor provider,
        InferenceException exception,
        bool cpuFallbackAlreadyUsed)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(exception);
        return !cpuFallbackAlreadyUsed
            && provider.Kind == InferenceProviderKind.DirectMl
            && exception.IsGpuFallbackEligible;
    }
}
