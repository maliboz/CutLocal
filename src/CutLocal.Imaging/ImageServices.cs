using CutLocal.Domain;
using SkiaSharp;

namespace CutLocal.Imaging;

/// <summary>Decodes one input after inspecting its dimensions.</summary>
public interface IImageDecoder
{
    /// <summary>Decodes an image with safety limits and caller-owned lifetime.</summary>
    DecodedImage Decode(string inputPath, CancellationToken cancellationToken);
}

/// <summary>Composes a float alpha mask over the original RGB image.</summary>
public interface IMaskCompositor
{
    /// <summary>Creates an owned RGBA bitmap at source dimensions.</summary>
    SKBitmap Compose(DecodedImage image, RefinedMask mask, CancellationToken cancellationToken);
}

/// <summary>Writes images through a same-directory temporary file.</summary>
public interface IAtomicImageWriter
{
    /// <summary>Encodes and commits a PNG, returning the actual destination path.</summary>
    string WritePng(
        SKBitmap bitmap,
        string outputPath,
        ExistingOutputBehavior existingOutputBehavior,
        CancellationToken cancellationToken);
}
