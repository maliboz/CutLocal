using SkiaSharp;

namespace CutLocal.Imaging;

/// <summary>Owns one decoded full-resolution image for the lifetime of an item.</summary>
public sealed class DecodedImage : IDisposable
{
    private bool _disposed;

    /// <summary>Initializes an owned decoded bitmap.</summary>
    public DecodedImage(SKBitmap bitmap, OriginalImageMetadata metadata)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>Gets the owned unpremultiplied BGRA bitmap.</summary>
    public SKBitmap Bitmap { get; }

    /// <summary>Gets source dimensions and safe metadata.</summary>
    public OriginalImageMetadata Metadata { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Bitmap.Dispose();
        _disposed = true;
    }
}

/// <summary>Contains metadata needed to map a mask back to the source image.</summary>
public sealed record OriginalImageMetadata
{
    /// <summary>Gets decoded width.</summary>
    public required int Width { get; init; }
    /// <summary>Gets decoded height.</summary>
    public required int Height { get; init; }
    /// <summary>Gets source DPI on the horizontal axis when available.</summary>
    public double? DpiX { get; init; }
    /// <summary>Gets source DPI on the vertical axis when available.</summary>
    public double? DpiY { get; init; }
}
