using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CutLocal.Persistence;

namespace CutLocal.App;

/// <summary>Reads the STA clipboard and atomically persists copied bitmaps as PNG.</summary>
public sealed class ClipboardService : IClipboardService
{
    private const long MaximumClipboardPixels = 50_000_000;
    private readonly string _clipboardRoot;

    /// <summary>Initializes the controlled clipboard cache.</summary>
    public ClipboardService(ApplicationPaths paths)
    {
        _clipboardRoot = Path.Combine(paths.DataRoot, "clipboard");
    }

    /// <inheritdoc />
    public ClipboardCapture? CapturePng()
    {
        if (Clipboard.ContainsFileDropList())
        {
            StringCollection files = Clipboard.GetFileDropList();
            string? png = files.Cast<string>().FirstOrDefault(path =>
                File.Exists(path) && Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase));
            return png is null
                ? null
                : new ClipboardCapture { Path = Path.GetFullPath(png), IsTemporary = false };
        }

        if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not BitmapSource image)
        {
            return null;
        }

        long pixels = checked((long)image.PixelWidth * image.PixelHeight);
        if (pixels <= 0 || pixels > MaximumClipboardPixels)
        {
            throw new InvalidDataException("The clipboard image exceeds CutLocal safety limits.");
        }

        Directory.CreateDirectory(_clipboardRoot);
        string finalPath = Path.Combine(_clipboardRoot, $"clipboard-{Guid.NewGuid():N}.png");
        string temporaryPath = $"{finalPath}.partial";
        try
        {
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.WriteThrough))
            {
                encoder.Save(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath);
            return new ClipboardCapture { Path = finalPath, IsTemporary = true };
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <inheritdoc />
    public void Release(ClipboardCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!capture.IsTemporary)
        {
            return;
        }

        string root = Path.GetFullPath(_clipboardRoot)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(capture.Path);
        if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(candidate);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The unique partial file cannot be mistaken for a completed clipboard capture.
        }
        catch (UnauthorizedAccessException)
        {
            // The unique partial file cannot be mistaken for a completed clipboard capture.
        }
    }
}
