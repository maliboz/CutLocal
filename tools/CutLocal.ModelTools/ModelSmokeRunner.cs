using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CutLocal.Domain;
using CutLocal.Imaging;
using CutLocal.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

internal static class ModelSmokeRunner
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (!TryParseArguments(args, out SmokeOptions options))
        {
            Console.Error.WriteLine(
                "Usage: CutLocal.ModelTools smoke <model.onnx> <manifest.json> [output.png] "
                + "[--provider auto|cpu|directml] [--adapter <index>] [--iterations <1..50>]");
            return 2;
        }

        ModelDescriptor descriptor;
        await using (FileStream stream = File.OpenRead(options.ManifestPath))
        {
            descriptor = await JsonSerializer.DeserializeAsync<ModelDescriptor>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                ?? throw new InvalidDataException("The model manifest is empty.");
        }

        await using (FileStream stream = File.OpenRead(options.ModelPath))
        {
            string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!actualHash.Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"FAIL SHA-256 mismatch. Expected {descriptor.Sha256}, observed {actualHash}.");
                return 3;
            }
        }

        WindowsInferenceProviderCatalog catalog = new();
        ProviderSelectionService selection = new(catalog);
        IReadOnlyList<InferenceProviderDescriptor> candidates =
            await selection.GetCandidatesAsync(
                descriptor,
                options.Provider,
                options.AdapterIndex,
                cancellationToken);
        using U2NetModelAdapterFactory factory = new(NullLoggerFactory.Instance);
        U2NetModelAdapterFactory.ModelAdapterLease? lease = null;
        bool usedCpuFallback = false;
        try
        {
            lease = await AcquirePreferredAsync(
                factory,
                descriptor,
                options.ModelPath,
                candidates,
                cancellationToken);
            using DecodedImage image = CreateSyntheticImage();
            int tensorLength = checked(descriptor.Input.Width * descriptor.Input.Height * 3);
            using IMemoryOwner<float> owner = MemoryPool<float>.Shared.Rent(tensorLength);
            PreprocessedInput input = lease.Adapter.Preprocess(image, owner);
            List<double> inferenceSamples = new(options.Iterations);
            MaskResult? finalMask = null;
            Stopwatch total = Stopwatch.StartNew();
            for (int iteration = 0; iteration < options.Iterations; iteration++)
            {
                Stopwatch inference = Stopwatch.StartNew();
                try
                {
                    MaskResult current = await lease.Adapter.RunAsync(input, cancellationToken);
                    inference.Stop();
                    inferenceSamples.Add(inference.Elapsed.TotalMilliseconds);
                    finalMask?.Dispose();
                    finalMask = current;
                }
                catch (InferenceException exception) when (GpuFallbackPolicy.ShouldRetryOnCpu(
                    lease.Provider,
                    exception,
                    usedCpuFallback))
                {
                    lease.Invalidate();
                    await lease.DisposeAsync();
                    lease = null;
                    InferenceProviderDescriptor cpu = candidates.Single(
                        provider => provider.Kind == InferenceProviderKind.Cpu);
                    lease = await factory.AcquireAsync(
                        descriptor,
                        options.ModelPath,
                        cpu,
                        cancellationToken);
                    input = lease.Adapter.Preprocess(image, owner);
                    usedCpuFallback = true;
                    iteration--;
                }
            }

            MaskResult completedMask = finalMask
                ?? throw new InvalidOperationException("Smoke inference produced no mask.");
            using (completedMask)
            {
                using RefinedMask mask = lease.Adapter.Postprocess(
                    completedMask,
                    image.Metadata,
                    new MaskRefinementOptions());
                using SKBitmap composed = new BilinearAlphaCompositor().Compose(
                    image,
                    mask,
                    cancellationToken);
                string committedPath = new AtomicPngWriter().WritePng(
                    composed,
                    options.OutputPath,
                    ExistingOutputBehavior.Overwrite,
                    cancellationToken);
                total.Stop();
                inferenceSamples.Sort();
                double median = inferenceSamples[inferenceSamples.Count / 2];
                Console.WriteLine(
                    $"PASS {descriptor.Id} {descriptor.Version}; provider={lease.Provider.Id}; "
                    + $"fallback={usedCpuFallback}; iterations={options.Iterations}; "
                    + $"medianInferenceMs={median:F1}; totalMs={total.Elapsed.TotalMilliseconds:F1}; "
                    + committedPath);
            }

            return 0;
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }
        }
    }

    private static async ValueTask<U2NetModelAdapterFactory.ModelAdapterLease> AcquirePreferredAsync(
        U2NetModelAdapterFactory factory,
        ModelDescriptor descriptor,
        string modelPath,
        IReadOnlyList<InferenceProviderDescriptor> candidates,
        CancellationToken cancellationToken)
    {
        InferenceException? last = null;
        foreach (InferenceProviderDescriptor candidate in candidates)
        {
            try
            {
                return await factory.AcquireAsync(
                    descriptor,
                    modelPath,
                    candidate,
                    cancellationToken);
            }
            catch (InferenceException exception) when (candidate.Kind != InferenceProviderKind.Cpu)
            {
                last = exception;
                Console.Error.WriteLine(
                    $"WARN provider={candidate.Id}; code={exception.LogCode}; trying next local provider.");
            }
        }

        throw last ?? new InferenceException(
            ProcessingErrorCategory.ProviderUnavailable,
            "SMOKE_PROVIDER_UNAVAILABLE",
            "No smoke-test provider could initialize the model.");
    }

    private static bool TryParseArguments(string[] args, out SmokeOptions options)
    {
        options = null!;
        if (args.Length < 2)
        {
            return false;
        }

        string modelPath = Path.GetFullPath(args[0]);
        string manifestPath = Path.GetFullPath(args[1]);
        string outputPath = Path.Combine(Path.GetDirectoryName(modelPath)!, "cutlocal-smoke-output.png");
        InferenceProviderKind provider = InferenceProviderKind.Auto;
        int? adapterIndex = null;
        int iterations = 1;
        bool outputSeen = false;
        for (int index = 2; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument.Equals("--provider", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
                && Enum.TryParse(args[++index], ignoreCase: true, out InferenceProviderKind parsedProvider)
                && parsedProvider is InferenceProviderKind.Auto
                    or InferenceProviderKind.Cpu
                    or InferenceProviderKind.DirectMl)
            {
                provider = parsedProvider;
            }
            else if (argument.Equals("--adapter", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
                && int.TryParse(
                    args[++index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int parsedAdapter)
                && parsedAdapter >= 0)
            {
                adapterIndex = parsedAdapter;
            }
            else if (argument.Equals("--iterations", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
                && int.TryParse(
                    args[++index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int parsedIterations)
                && parsedIterations is >= 1 and <= 50)
            {
                iterations = parsedIterations;
            }
            else if (!argument.StartsWith("--", StringComparison.Ordinal) && !outputSeen)
            {
                outputPath = Path.GetFullPath(argument);
                outputSeen = true;
            }
            else
            {
                return false;
            }
        }

        options = new SmokeOptions(
            modelPath,
            manifestPath,
            outputPath,
            provider,
            adapterIndex,
            iterations);
        return true;
    }

    private static DecodedImage CreateSyntheticImage()
    {
        const int Width = 96;
        const int Height = 64;
        SKBitmap bitmap = new(new SKImageInfo(
            Width,
            Height,
            SKColorType.Bgra8888,
            SKAlphaType.Unpremul));
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte red = (byte)Math.Round(x * 255d / (Width - 1));
                byte green = (byte)Math.Round(y * 255d / (Height - 1));
                bitmap.SetPixel(x, y, new SKColor(red, green, 96, byte.MaxValue));
            }
        }

        return new DecodedImage(
            bitmap,
            new OriginalImageMetadata { Width = Width, Height = Height });
    }

    private sealed record SmokeOptions(
        string ModelPath,
        string ManifestPath,
        string OutputPath,
        InferenceProviderKind Provider,
        int? AdapterIndex,
        int Iterations);
}
