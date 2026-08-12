using CutLocal.Domain;
using SkiaSharp;

namespace CutLocal.Imaging;

/// <summary>Resamples a float mask while retaining the source RGB pixels.</summary>
public sealed class BilinearAlphaCompositor : IMaskCompositor
{
    /// <inheritdoc />
    public unsafe SKBitmap Compose(
        DecodedImage image,
        RefinedMask mask,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mask);

        SKBitmap source = image.Bitmap;
        SKImageInfo outputInfo = new(
            source.Width,
            source.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul);
        SKBitmap output = new(outputInfo);

        try
        {
            byte* sourceBase = (byte*)source.GetPixels().ToPointer();
            byte* outputBase = (byte*)output.GetPixels().ToPointer();
            ReadOnlySpan<float> maskValues = mask.Values.Span;

            for (int y = 0; y < source.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte* sourceRow = sourceBase + (y * source.RowBytes);
                byte* outputRow = outputBase + (y * output.RowBytes);

                for (int x = 0; x < source.Width; x++)
                {
                    int offset = x * 4;
                    float maskAlpha = SampleBilinear(
                        maskValues,
                        mask.Width,
                        mask.Height,
                        x,
                        y,
                        source.Width,
                        source.Height);
                    byte sourceAlpha = sourceRow[offset + 3];

                    outputRow[offset] = sourceRow[offset];
                    outputRow[offset + 1] = sourceRow[offset + 1];
                    outputRow[offset + 2] = sourceRow[offset + 2];
                    outputRow[offset + 3] = (byte)Math.Clamp(
                        (int)MathF.Round(sourceAlpha * maskAlpha),
                        byte.MinValue,
                        byte.MaxValue);
                }
            }

            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    /// <summary>Samples a row-major mask at an output pixel center.</summary>
    public static float SampleBilinear(
        ReadOnlySpan<float> values,
        int maskWidth,
        int maskHeight,
        int outputX,
        int outputY,
        int outputWidth,
        int outputHeight)
    {
        float sourceX = Math.Clamp(
            ((outputX + 0.5f) * maskWidth / outputWidth) - 0.5f,
            0f,
            maskWidth - 1f);
        float sourceY = Math.Clamp(
            ((outputY + 0.5f) * maskHeight / outputHeight) - 0.5f,
            0f,
            maskHeight - 1f);
        int x0 = (int)MathF.Floor(sourceX);
        int y0 = (int)MathF.Floor(sourceY);
        int x1 = Math.Min(x0 + 1, maskWidth - 1);
        int y1 = Math.Min(y0 + 1, maskHeight - 1);
        float tx = sourceX - x0;
        float ty = sourceY - y0;

        float top = Lerp(values[(y0 * maskWidth) + x0], values[(y0 * maskWidth) + x1], tx);
        float bottom = Lerp(values[(y1 * maskWidth) + x0], values[(y1 * maskWidth) + x1], tx);
        return Math.Clamp(Lerp(top, bottom, ty), 0f, 1f);
    }

    private static float Lerp(float left, float right, float amount) =>
        left + ((right - left) * amount);
}
