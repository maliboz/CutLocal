using CutLocal.Domain;
using SkiaSharp;

namespace CutLocal.Imaging;

/// <summary>Decodes PNG files after applying dimension and decompression-bomb limits.</summary>
public sealed class SafePngDecoder : IImageDecoder
{
    /// <summary>Gets the default maximum decoded pixel count.</summary>
    public const long DefaultMaximumPixels = 100_000_000;

    /// <summary>Gets the default maximum width or height.</summary>
    public const int DefaultMaximumDimension = 32_768;

    private readonly long _maximumPixels;
    private readonly int _maximumDimension;

    /// <summary>Initializes the decoder with optional safety limits.</summary>
    public SafePngDecoder(
        long maximumPixels = DefaultMaximumPixels,
        int maximumDimension = DefaultMaximumDimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDimension);

        _maximumPixels = maximumPixels;
        _maximumDimension = maximumDimension;
    }

    /// <inheritdoc />
    public DecodedImage Decode(string inputPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        cancellationToken.ThrowIfCancellationRequested();
        SKBitmap? bitmap = null;

        if (!Path.GetExtension(inputPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImagingException(
                ProcessingErrorCategory.UnsupportedFormat,
                "IMG_UNSUPPORTED_PHASE1",
                "The current vertical slice accepts PNG input only.");
        }

        try
        {
            using FileStream stream = new(
                inputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            using SKCodec? codec = SKCodec.Create(stream);
            if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Png)
            {
                throw new ImagingException(
                    ProcessingErrorCategory.DecodeFailed,
                    "IMG_INVALID_PNG",
                    "The file is not a valid PNG image.");
            }

            SKImageInfo encodedInfo = codec.Info;
            ValidateDimensions(encodedInfo.Width, encodedInfo.Height);
            cancellationToken.ThrowIfCancellationRequested();

            SKImageInfo decodedInfo = new(
                encodedInfo.Width,
                encodedInfo.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Unpremul);
            bitmap = new SKBitmap(decodedInfo);
            SKCodecResult result = codec.GetPixels(decodedInfo, bitmap.GetPixels());
            if (result != SKCodecResult.Success)
            {
                throw new ImagingException(
                    ProcessingErrorCategory.DecodeFailed,
                    "IMG_DECODE_FAILED",
                    $"PNG decoding failed with codec result {result}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            DecodedImage decoded = new(
                bitmap,
                new OriginalImageMetadata
                {
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                });
            bitmap = null;
            return decoded;
        }
        catch (ImagingException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ImagingException(
                ProcessingErrorCategory.PermissionDenied,
                "IMG_READ_DENIED",
                "The input file cannot be read because access was denied.",
                exception);
        }
        catch (IOException exception)
        {
            throw new ImagingException(
                ProcessingErrorCategory.FileLocked,
                "IMG_READ_IO",
                "The input file is unavailable or locked.",
                exception);
        }
        catch (OutOfMemoryException exception)
        {
            throw new ImagingException(
                ProcessingErrorCategory.ImageTooLarge,
                "IMG_MEMORY_PRESSURE",
                "The decoded image could not fit within safe local memory limits.",
                exception);
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ImagingException(
                ProcessingErrorCategory.DecodeFailed,
                "IMG_INVALID_DIMENSIONS",
                "The image has invalid dimensions.");
        }

        long pixelCount;
        try
        {
            pixelCount = checked((long)width * height);
            _ = checked(pixelCount * 4L);
        }
        catch (OverflowException exception)
        {
            throw new ImagingException(
                ProcessingErrorCategory.ImageTooLarge,
                "IMG_DIMENSION_OVERFLOW",
                "The image dimensions exceed safe arithmetic limits.",
                exception);
        }

        if (width > _maximumDimension || height > _maximumDimension || pixelCount > _maximumPixels)
        {
            throw new ImagingException(
                ProcessingErrorCategory.ImageTooLarge,
                "IMG_DECOMPRESSION_BOMB",
                "The decoded image would exceed configured safety limits.");
        }
    }
}
