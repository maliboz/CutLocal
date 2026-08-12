using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Inference;

/// <summary>Builds the deterministic, offline provider attempt order.</summary>
public sealed class ProviderSelectionService
{
    private readonly IInferenceProviderCatalog _catalog;

    /// <summary>Initializes the provider policy.</summary>
    public ProviderSelectionService(IInferenceProviderCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <summary>Resolves concrete provider/device candidates in fallback order.</summary>
    public async ValueTask<IReadOnlyList<InferenceProviderDescriptor>> GetCandidatesAsync(
        ModelDescriptor model,
        InferenceProviderKind requested,
        int? directMlAdapterIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<InferenceProviderDescriptor> available = await _catalog.GetAllAsync(
            cancellationToken);
        HashSet<string> supported = new(model.SupportedProviders, StringComparer.OrdinalIgnoreCase);
        List<InferenceProviderDescriptor> result = [];

        if (requested is InferenceProviderKind.Auto
            or InferenceProviderKind.WindowsMl
            or InferenceProviderKind.DirectMl)
        {
            IEnumerable<InferenceProviderDescriptor> directMl = available
                .Where(provider => provider.Kind == InferenceProviderKind.DirectMl)
                .Where(provider => directMlAdapterIndex is null
                    || provider.DeviceIndex == directMlAdapterIndex)
                .OrderByDescending(provider => provider.DedicatedVideoMemoryBytes)
                .ThenBy(provider => provider.DeviceIndex);
            if (supported.Contains("directml"))
            {
                result.AddRange(directMl);
            }
        }

        if (supported.Contains("cpu")
            && available.FirstOrDefault(provider => provider.Kind == InferenceProviderKind.Cpu) is { } cpu)
        {
            result.Add(cpu);
        }

        if (result.Count == 0)
        {
            throw new InferenceException(
                ProcessingErrorCategory.ProviderUnavailable,
                "PROVIDER_NO_CANDIDATE",
                "No offline-ready provider supports the selected model.");
        }

        return result;
    }
}
