using CutLocal.Domain;

namespace CutLocal.Inference;

/// <summary>Maps native provider failures to stable public categories.</summary>
public static class InferenceFailureClassifier
{
    /// <summary>Classifies an ONNX Runtime failure for the active provider.</summary>
    public static InferenceException ClassifyRunFailure(
        Exception exception,
        InferenceProviderDescriptor provider)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(provider);

        if (provider.Kind != InferenceProviderKind.DirectMl)
        {
            return new InferenceException(
                ProcessingErrorCategory.InferenceFailed,
                "ORT_CPU_RUN_FAILED",
                "CPU inference failed.",
                exception);
        }

        string detail = exception.ToString();
        if (ContainsAny(
            detail,
            "out of memory",
            "e_outofmemory",
            "0x8007000e",
            "not enough memory",
            "failed to allocate"))
        {
            return new InferenceException(
                ProcessingErrorCategory.GpuOutOfMemory,
                "DML_GPU_OOM",
                "DirectML could not allocate enough GPU memory.",
                exception,
                isGpuFallbackEligible: true);
        }

        if (ContainsAny(
            detail,
            "dxgi_error_device_removed",
            "dxgi_error_device_hung",
            "dxgi_error_device_reset",
            "0x887a0005",
            "0x887a0006",
            "0x887a0007",
            "device removed",
            "device lost"))
        {
            return new InferenceException(
                ProcessingErrorCategory.ProviderUnavailable,
                "DML_DEVICE_LOST",
                "The DirectML device became unavailable.",
                exception,
                isGpuFallbackEligible: true);
        }

        return new InferenceException(
            ProcessingErrorCategory.InferenceFailed,
            "ORT_DML_RUN_FAILED",
            "DirectML inference failed.",
            exception);
    }

    /// <summary>Classifies a provider-specific session creation or warm-up failure.</summary>
    public static InferenceException ClassifyInitializationFailure(
        Exception exception,
        InferenceProviderDescriptor provider)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.Kind == InferenceProviderKind.DirectMl)
        {
            return new InferenceException(
                ProcessingErrorCategory.ProviderUnavailable,
                "DML_SESSION_CREATE",
                "DirectML could not initialize this model on the selected adapter.",
                exception,
                isGpuFallbackEligible: true);
        }

        return new InferenceException(
            ProcessingErrorCategory.ModelIncompatible,
            "MODEL_SESSION_CREATE",
            "ONNX Runtime could not create a CPU session for the model.",
            exception);
    }

    private static bool ContainsAny(string value, params ReadOnlySpan<string> fragments)
    {
        foreach (string fragment in fragments)
        {
            if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
