using CutLocal.Domain;
using CutLocal.Imaging;
using SkiaSharp;

namespace CutLocal.IntegrationTests;

public sealed class AtomicPngWriterTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void WritePng_RenameBehaviorPreservesExistingOutputAndLeavesNoPartialFile()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string requested = Path.Combine(_temporaryDirectory, "output.png");
        File.WriteAllText(requested, "existing");
        using SKBitmap bitmap = new(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Red);

        string actual = new AtomicPngWriter().WritePng(
            bitmap,
            requested,
            ExistingOutputBehavior.Rename,
            CancellationToken.None);

        Assert.NotEqual(requested, actual);
        Assert.Equal("existing", File.ReadAllText(requested));
        Assert.True(File.Exists(actual));
        Assert.Empty(Directory.GetFiles(_temporaryDirectory, "*.partial"));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070070))]
    [InlineData(unchecked((int)0x80070027))]
    public void IsDiskFull_RecognizesWindowsDiskFullCodes(int hresult)
    {
        Assert.True(AtomicPngWriter.IsDiskFull(new HResultIOException(hresult)));
    }

    [Fact]
    public void WritePng_LockedOutputReturnsTypedFailureAndLeavesNoPartialFile()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        string requested = Path.Combine(_temporaryDirectory, "locked-output.png");
        File.WriteAllText(requested, "locked");
        using SKBitmap bitmap = new(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Blue);
        using FileStream locked = new(
            requested,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        ImagingException exception = Assert.Throws<ImagingException>(() =>
            new AtomicPngWriter().WritePng(
                bitmap,
                requested,
                ExistingOutputBehavior.Overwrite,
                CancellationToken.None));

        Assert.Equal(ProcessingErrorCategory.FileLocked, exception.Category);
        Assert.Equal("ENC_OUTPUT_IO", exception.LogCode);
        Assert.Empty(Directory.GetFiles(_temporaryDirectory, "*.partial"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private sealed class HResultIOException : IOException
    {
        public HResultIOException(int hresult)
        {
            HResult = hresult;
        }
    }
}
