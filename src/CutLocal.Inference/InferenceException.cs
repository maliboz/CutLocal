using CutLocal.Domain;

namespace CutLocal.Inference;

/// <summary>Represents an expected model or provider failure.</summary>
public sealed class InferenceException : Exception
{
    /// <summary>Initializes a typed inference failure.</summary>
    public InferenceException(
        ProcessingErrorCategory category,
        string logCode,
        string message,
        Exception? innerException = null,
        bool isGpuFallbackEligible = false)
        : base(message, innerException)
    {
        Category = category;
        LogCode = logCode;
        IsGpuFallbackEligible = isGpuFallbackEligible;
    }

    /// <summary>Gets the typed category.</summary>
    public ProcessingErrorCategory Category { get; }
    /// <summary>Gets the stable diagnostic code.</summary>
    public string LogCode { get; }
    /// <summary>Gets whether this failure may consume the item's one CPU fallback attempt.</summary>
    public bool IsGpuFallbackEligible { get; }
}
