using CutLocal.Contracts;
using CutLocal.Domain;
using Microsoft.ML.OnnxRuntime;

namespace CutLocal.Inference;

/// <summary>Loads a supplied graph with the CPU provider and validates manifest metadata.</summary>
public sealed class OnnxModelCompatibilityValidator : IModelCompatibilityValidator
{
    /// <inheritdoc />
    public async ValueTask ValidateAsync(
        ModelDescriptor descriptor,
        string modelPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using SessionOptions options = ProviderSessionOptions.Create(
                    WindowsInferenceProviderCatalog.Cpu);
                using InferenceSession session = new(modelPath, options);
                U2NetModelAdapter.ValidateMetadata(session, descriptor);
            },
            cancellationToken).ConfigureAwait(false);
    }
}
