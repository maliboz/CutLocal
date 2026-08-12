using CutLocal.Domain;

namespace CutLocal.Imaging;

/// <summary>Represents an expected image decode, composition, or encode failure.</summary>
public sealed class ImagingException : Exception
{
    /// <summary>Initializes an expected imaging failure.</summary>
    public ImagingException(
        ProcessingErrorCategory category,
        string logCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Category = category;
        LogCode = logCode;
    }

    /// <summary>Gets the typed processing category.</summary>
    public ProcessingErrorCategory Category { get; }

    /// <summary>Gets the stable diagnostic code.</summary>
    public string LogCode { get; }
}
