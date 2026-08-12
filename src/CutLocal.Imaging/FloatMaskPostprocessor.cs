using System.Buffers;
using CutLocal.Domain;

namespace CutLocal.Imaging;

/// <summary>Normalizes raw model output while retaining float alpha precision.</summary>
public sealed class FloatMaskPostprocessor
{
    private readonly MemoryPool<float> _memoryPool;

    /// <summary>Initializes the postprocessor.</summary>
    public FloatMaskPostprocessor(MemoryPool<float>? memoryPool = null)
    {
        _memoryPool = memoryPool ?? MemoryPool<float>.Shared;
    }

    /// <summary>Applies min/max normalization, inversion, and optional hard threshold.</summary>
    public RefinedMask Normalize(
        ReadOnlySpan<float> rawValues,
        int width,
        int height,
        MaskRefinementOptions options)
        => Normalize(rawValues, width, height, "minmax", options, width, height);

    /// <summary>Applies the manifest activation, min/max normalization, inversion, and threshold policy.</summary>
    public RefinedMask Normalize(
        ReadOnlySpan<float> rawValues,
        int width,
        int height,
        string activation,
        MaskRefinementOptions options)
        => Normalize(rawValues, width, height, activation, options, width, height);

    /// <summary>
    /// Applies activation, normalization, threshold bias, and output-pixel-aware edge feathering.
    /// </summary>
    public RefinedMask Normalize(
        ReadOnlySpan<float> rawValues,
        int width,
        int height,
        string activation,
        MaskRefinementOptions options,
        int outputWidth,
        int outputHeight)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(activation);
        int length = checked(width * height);
        if (rawValues.Length < length)
        {
            throw new ArgumentException("The output tensor is smaller than the declared mask.", nameof(rawValues));
        }

        if (!double.IsFinite(options.Threshold) || options.Threshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Threshold must be between zero and one.");
        }

        if (!double.IsFinite(options.FeatherRadius) || options.FeatherRadius < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Feather radius cannot be negative.");
        }

        if (outputWidth <= 0 || outputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputWidth), "Output dimensions must be positive.");
        }

        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        for (int index = 0; index < length; index++)
        {
            float value = ApplyActivation(rawValues[index], activation);
            if (!float.IsFinite(value))
            {
                continue;
            }

            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        IMemoryOwner<float> owner = _memoryPool.Rent(length);
        Span<float> destination = owner.Memory.Span[..length];
        float range = maximum - minimum;

        for (int index = 0; index < length; index++)
        {
            float raw = ApplyActivation(rawValues[index], activation);
            float alpha = float.IsFinite(raw) && float.IsFinite(range) && range > 1e-7f
                ? Math.Clamp((raw - minimum) / range, 0f, 1f)
                : 0f;

            if (options.Invert)
            {
                alpha = 1f - alpha;
            }

            destination[index] = options.HardCut
                ? alpha >= options.Threshold ? 1f : 0f
                : RecenterSoftAlpha(alpha, (float)options.Threshold);
        }

        if (options.FeatherRadius > 0)
        {
            double radiusX = options.FeatherRadius * width / outputWidth;
            double radiusY = options.FeatherRadius * height / outputHeight;
            ApplyFeather(destination, width, height, radiusX, radiusY);
        }

        return new RefinedMask(owner, width, height);
    }

    private void ApplyFeather(
        Span<float> values,
        int width,
        int height,
        double radiusX,
        double radiusY)
    {
        int length = checked(width * height);
        using IMemoryOwner<float> temporaryOwner = _memoryPool.Rent(length);
        Span<float> temporary = temporaryOwner.Memory.Span[..length];

        if (radiusX > 1e-3)
        {
            values.CopyTo(temporary);
            BlurHorizontal(temporary, values, width, height, CreateGaussianKernel(radiusX));
        }

        if (radiusY > 1e-3)
        {
            values.CopyTo(temporary);
            BlurVertical(temporary, values, width, height, CreateGaussianKernel(radiusY));
        }
    }

    private static float RecenterSoftAlpha(float alpha, float threshold)
    {
        const float epsilon = 1e-6f;
        float boundedThreshold = Math.Clamp(threshold, epsilon, 1f - epsilon);
        return alpha <= boundedThreshold
            ? 0.5f * alpha / boundedThreshold
            : 0.5f + (0.5f * (alpha - boundedThreshold) / (1f - boundedThreshold));
    }

    private static float[] CreateGaussianKernel(double radius)
    {
        int halfWidth = Math.Max(1, (int)Math.Ceiling(radius));
        double sigma = Math.Max(radius / 2d, 0.35d);
        float[] kernel = new float[(2 * halfWidth) + 1];
        double sum = 0;
        for (int offset = -halfWidth; offset <= halfWidth; offset++)
        {
            double weight = Math.Exp(-(offset * offset) / (2d * sigma * sigma));
            kernel[offset + halfWidth] = (float)weight;
            sum += weight;
        }

        for (int index = 0; index < kernel.Length; index++)
        {
            kernel[index] = (float)(kernel[index] / sum);
        }

        return kernel;
    }

    private static void BlurHorizontal(
        ReadOnlySpan<float> source,
        Span<float> destination,
        int width,
        int height,
        ReadOnlySpan<float> kernel)
    {
        int halfWidth = kernel.Length / 2;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int offset = -halfWidth; offset <= halfWidth; offset++)
                {
                    int sampleX = Math.Clamp(x + offset, 0, width - 1);
                    sum += source[row + sampleX] * kernel[offset + halfWidth];
                }

                destination[row + x] = Math.Clamp(sum, 0f, 1f);
            }
        }
    }

    private static void BlurVertical(
        ReadOnlySpan<float> source,
        Span<float> destination,
        int width,
        int height,
        ReadOnlySpan<float> kernel)
    {
        int halfWidth = kernel.Length / 2;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int offset = -halfWidth; offset <= halfWidth; offset++)
                {
                    int sampleY = Math.Clamp(y + offset, 0, height - 1);
                    sum += source[(sampleY * width) + x] * kernel[offset + halfWidth];
                }

                destination[(y * width) + x] = Math.Clamp(sum, 0f, 1f);
            }
        }
    }

    private static float ApplyActivation(float value, string activation)
    {
        if (activation.Equals("minmax", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (activation.Equals("sigmoid-minmax", StringComparison.OrdinalIgnoreCase))
        {
            if (!float.IsFinite(value))
            {
                return value;
            }

            return value >= 0
                ? 1f / (1f + MathF.Exp(-value))
                : MathF.Exp(value) / (1f + MathF.Exp(value));
        }

        throw new ArgumentOutOfRangeException(
            nameof(activation),
            activation,
            "Unsupported model output activation.");
    }
}
