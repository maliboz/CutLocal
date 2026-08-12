namespace CutLocal.App;

/// <summary>Captures a clipboard file or bitmap as a controlled local PNG input.</summary>
public interface IClipboardService
{
    /// <summary>Returns a PNG input when the clipboard contains a supported image.</summary>
    ClipboardCapture? CapturePng();
    /// <summary>Releases a temporary capture previously created by this service.</summary>
    void Release(ClipboardCapture capture);
}

/// <summary>Describes a clipboard input and whether CutLocal owns its temporary file.</summary>
public sealed record ClipboardCapture
{
    /// <summary>Gets the local PNG path.</summary>
    public required string Path { get; init; }
    /// <summary>Gets whether the clipboard service owns and may delete the file.</summary>
    public required bool IsTemporary { get; init; }
}
