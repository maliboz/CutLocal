using CutLocal.Domain;
using Microsoft.ML.OnnxRuntime;

namespace CutLocal.Inference;

/// <summary>Describes provider constraints independently of a native session lifetime.</summary>
public sealed record ProviderSessionPolicy
{
    /// <summary>Gets whether ORT memory-pattern optimization is enabled.</summary>
    public required bool EnableMemoryPattern { get; init; }
    /// <summary>Gets the session execution mode.</summary>
    public required ExecutionMode ExecutionMode { get; init; }
    /// <summary>Gets CPU intra-op worker threads.</summary>
    public required int IntraOpNumThreads { get; init; }
    /// <summary>Gets inter-op worker threads.</summary>
    public required int InterOpNumThreads { get; init; }
}

/// <summary>Creates ONNX Runtime options that enforce provider-specific safety rules.</summary>
public static class ProviderSessionOptions
{
    /// <summary>Gets the effective session policy for tests and diagnostics.</summary>
    public static ProviderSessionPolicy GetPolicy(InferenceProviderDescriptor provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.Kind switch
        {
            InferenceProviderKind.DirectMl => new ProviderSessionPolicy
            {
                EnableMemoryPattern = false,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
            },
            InferenceProviderKind.Cpu => new ProviderSessionPolicy
            {
                EnableMemoryPattern = true,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                IntraOpNumThreads = CpuTopology.GetRecommendedInferenceThreadCount(),
                InterOpNumThreads = 1,
            },
            _ => throw new InferenceException(
                ProcessingErrorCategory.ProviderUnavailable,
                "PROVIDER_NOT_IMPLEMENTED",
                $"Provider '{provider.Kind}' has no local session implementation."),
        };
    }

    /// <summary>Creates disposable native session options for a concrete provider.</summary>
    public static SessionOptions Create(InferenceProviderDescriptor provider)
    {
        ProviderSessionPolicy policy = GetPolicy(provider);
        SessionOptions options = new()
        {
            EnableMemoryPattern = policy.EnableMemoryPattern,
            ExecutionMode = policy.ExecutionMode,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = policy.IntraOpNumThreads,
            InterOpNumThreads = policy.InterOpNumThreads,
        };

        if (provider.Kind == InferenceProviderKind.DirectMl)
        {
            if (provider.DeviceIndex is not int deviceIndex || deviceIndex < 0)
            {
                options.Dispose();
                throw new InferenceException(
                    ProcessingErrorCategory.ProviderUnavailable,
                    "DML_DEVICE_INDEX_INVALID",
                    "DirectML requires a non-negative DXGI adapter index.",
                    isGpuFallbackEligible: true);
            }

            options.AppendExecutionProvider_DML(deviceIndex);
        }

        return options;
    }
}
