using CutLocal.Domain;
using SkiaSharp;

namespace CutLocal.Imaging;

/// <summary>Encodes PNG data to a temporary file and commits by rename.</summary>
public sealed class AtomicPngWriter : IAtomicImageWriter
{
    /// <inheritdoc />
    public string WritePng(
        SKBitmap bitmap,
        string outputPath,
        ExistingOutputBehavior existingOutputBehavior,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();

        string fullOutputPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ImagingException(
                ProcessingErrorCategory.EncodeFailed,
                "ENC_NO_DIRECTORY",
                "The output path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string finalPath = ResolveFinalPath(fullOutputPath, existingOutputBehavior);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.partial");

        try
        {
            EnsureExistingOutputIsReplaceable(finalPath, existingOutputBehavior);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
                ?? throw new ImagingException(
                    ProcessingErrorCategory.EncodeFailed,
                    "ENC_PNG_NULL",
                    "Skia could not encode the output PNG.");

            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 64 * 1024,
                       FileOptions.WriteThrough))
            {
                data.SaveTo(stream);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(
                temporaryPath,
                finalPath,
                overwrite: existingOutputBehavior == ExistingOutputBehavior.Overwrite);
            return finalPath;
        }
        catch (ImagingException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ImagingException(
                ProcessingErrorCategory.PermissionDenied,
                "ENC_ACCESS_DENIED",
                "The output folder denied access.",
                exception);
        }
        catch (IOException exception) when (IsDiskFull(exception))
        {
            throw new ImagingException(
                ProcessingErrorCategory.DiskFull,
                "ENC_DISK_FULL",
                "The destination volume does not have enough free space.",
                exception);
        }
        catch (IOException exception)
        {
            throw new ImagingException(
                ProcessingErrorCategory.FileLocked,
                "ENC_OUTPUT_IO",
                "The output file is locked or unavailable.",
                exception);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static string ResolveFinalPath(
        string requestedPath,
        ExistingOutputBehavior behavior)
    {
        if (!File.Exists(requestedPath) || behavior == ExistingOutputBehavior.Overwrite)
        {
            return requestedPath;
        }

        if (behavior == ExistingOutputBehavior.Skip)
        {
            throw new ImagingException(
                ProcessingErrorCategory.EncodeFailed,
                "ENC_OUTPUT_EXISTS",
                "The output already exists and skip behavior was selected.");
        }

        string? directory = Path.GetDirectoryName(requestedPath);
        string stem = Path.GetFileNameWithoutExtension(requestedPath);
        string extension = Path.GetExtension(requestedPath);
        for (int suffix = 1; suffix <= 9999; suffix++)
        {
            string candidate = Path.Combine(directory!, $"{stem} ({suffix}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new ImagingException(
            ProcessingErrorCategory.EncodeFailed,
            "ENC_NO_UNIQUE_NAME",
            "A unique output name could not be allocated.");
    }

    private static void EnsureExistingOutputIsReplaceable(
        string finalPath,
        ExistingOutputBehavior behavior)
    {
        if (behavior != ExistingOutputBehavior.Overwrite || !File.Exists(finalPath))
        {
            return;
        }

        using FileStream probe = new(
            finalPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.Read);
    }

    /// <summary>Returns whether an I/O failure contains a Windows disk-full error code.</summary>
    public static bool IsDiskFull(IOException exception)
    {
        const int ErrorDiskFull = 0x70;
        const int ErrorHandleDiskFull = 0x27;
        int errorCode = exception.HResult & 0xFFFF;
        return errorCode is ErrorDiskFull or ErrorHandleDiskFull;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The unique partial name cannot be committed by a later operation.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup must not replace the original typed write failure.
        }
    }
}
