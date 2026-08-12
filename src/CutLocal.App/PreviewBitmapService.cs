using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CutLocal.App;

/// <summary>Decodes bounded preview proxies away from the WPF dispatcher.</summary>
public sealed class PreviewBitmapService : IPreviewBitmapService
{
    private const int MaximumAllowedPreviewEdge = 2048;

    /// <inheritdoc />
    public ValueTask<BitmapSource> LoadColorAsync(
        string path,
        int maximumEdge,
        CancellationToken cancellationToken) => new(Task.Run<BitmapSource>(
            () => LoadColor(path, ValidateMaximumEdge(maximumEdge), cancellationToken),
            cancellationToken));

    /// <inheritdoc />
    public async ValueTask<BitmapSource> LoadAlphaMaskAsync(
        string path,
        int maximumEdge,
        CancellationToken cancellationToken)
    {
        BitmapSource source = await LoadColorAsync(path, maximumEdge, cancellationToken)
            .ConfigureAwait(false);
        return await Task.Run(() => CreateAlphaMask(source, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private static BitmapImage LoadColor(
        string path,
        int maximumEdge,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The preview image does not exist.", fullPath);
        }

        (int width, int height) = ReadDimensions(fullPath);
        int decodeWidth = width >= height
            ? Math.Min(width, maximumEdge)
            : Math.Max(1, (int)Math.Round(width * (double)Math.Min(height, maximumEdge) / height));
        using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        cancellationToken.ThrowIfCancellationRequested();
        return bitmap;
    }

    private static (int Width, int Height) ReadDimensions(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.None);
        BitmapFrame frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static BitmapSource CreateAlphaMask(
        BitmapSource source,
        CancellationToken cancellationToken)
    {
        FormatConvertedBitmap converted = new(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(converted.PixelWidth * 4);
        byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(stride * converted.PixelHeight));
        converted.CopyPixels(pixels, stride, 0);
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if ((offset & 0x3FFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            byte alpha = pixels[offset + 3];
            pixels[offset] = alpha;
            pixels[offset + 1] = alpha;
            pixels[offset + 2] = alpha;
            pixels[offset + 3] = byte.MaxValue;
        }

        BitmapSource mask = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
        mask.Freeze();
        return mask;
    }

    private static int ValidateMaximumEdge(int maximumEdge)
    {
        if (maximumEdge is < 64 or > MaximumAllowedPreviewEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEdge));
        }

        return maximumEdge;
    }
}
