using System.Windows.Media;
using System.Windows.Media.Imaging;
using CutLocal.App;
using CutLocal.Persistence;

namespace CutLocal.UnitTests;

public sealed class PreviewAndClipboardServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreviewService_BoundsLongestEdgeAndBuildsOpaqueGrayscaleAlphaProxy()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "large.png");
        WritePng(path, width: 400, height: 200, alpha: 127);
        PreviewBitmapService service = new();

        BitmapSource color = await service.LoadColorAsync(path, 128, CancellationToken.None);
        BitmapSource mask = await service.LoadAlphaMaskAsync(path, 128, CancellationToken.None);

        Assert.Equal(128, color.PixelWidth);
        Assert.Equal(64, color.PixelHeight);
        Assert.True(color.IsFrozen);
        Assert.True(mask.IsFrozen);
        byte[] firstPixel = new byte[4];
        mask.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), firstPixel, 4, 0);
        Assert.InRange(firstPixel[0], 126, 128);
        Assert.Equal(firstPixel[0], firstPixel[1]);
        Assert.Equal(firstPixel[1], firstPixel[2]);
        Assert.Equal(byte.MaxValue, firstPixel[3]);
    }

    [Fact]
    public void ClipboardRelease_DeletesOnlyOwnedFilesUnderControlledRoot()
    {
        string clipboardRoot = Path.Combine(_root, "clipboard");
        string outsideRoot = $"{_root}-outside";
        Directory.CreateDirectory(clipboardRoot);
        Directory.CreateDirectory(outsideRoot);
        string owned = Path.Combine(clipboardRoot, "owned.png");
        string outside = Path.Combine(outsideRoot, "outside.png");
        File.WriteAllBytes(owned, [1]);
        File.WriteAllBytes(outside, [1]);
        ClipboardService service = new(CreatePaths());

        service.Release(new ClipboardCapture { Path = owned, IsTemporary = true });
        service.Release(new ClipboardCapture { Path = outside, IsTemporary = true });

        Assert.False(File.Exists(owned));
        Assert.True(File.Exists(outside));
        Directory.Delete(outsideRoot, recursive: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private ApplicationPaths CreatePaths() => new()
    {
        DataRoot = _root,
        LogRoot = Path.Combine(_root, "logs"),
        ManifestRoot = Path.Combine(_root, "manifests"),
        ModelRoot = Path.Combine(_root, "models"),
    };

    private static void WritePng(string path, int width, int height, byte alpha)
    {
        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 30;
            pixels[offset + 1] = 90;
            pixels[offset + 2] = 180;
            pixels[offset + 3] = alpha;
        }

        BitmapSource source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }
}
