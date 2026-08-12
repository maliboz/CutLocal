using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Application;

/// <summary>Coordinates explicit model-manager operations outside the inference path.</summary>
public sealed class ModelManagementUseCase(IModelPackageManager packages)
{
    /// <summary>Inspects local state without performing network access.</summary>
    public ValueTask<IReadOnlyList<ModelInstallationInfo>> InspectAsync(
        CancellationToken cancellationToken) => packages.InspectAllAsync(cancellationToken);

    /// <summary>Downloads and verifies one reviewed catalog model.</summary>
    public ValueTask<ModelPackageOperationResult> DownloadAsync(
        ModelDescriptor descriptor,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken,
        bool licenseAcknowledged = false) =>
        packages.DownloadAsync(descriptor, progress, cancellationToken, licenseAcknowledged);

    /// <summary>Repairs one reviewed catalog model.</summary>
    public ValueTask<ModelPackageOperationResult> RepairAsync(
        ModelDescriptor descriptor,
        IProgress<ModelTransferProgress>? progress,
        CancellationToken cancellationToken,
        bool licenseAcknowledged = false) =>
        packages.RepairAsync(descriptor, progress, cancellationToken, licenseAcknowledged);

    /// <summary>Deletes one controlled local model package.</summary>
    public ValueTask<ModelPackageOperationResult> DeleteAsync(
        ModelDescriptor descriptor,
        CancellationToken cancellationToken) =>
        packages.DeleteAsync(descriptor, cancellationToken);

    /// <summary>Imports one user-supplied ONNX graph and companion manifest.</summary>
    public ValueTask<ModelPackageOperationResult> ImportAsync(
        ModelImportRequest request,
        CancellationToken cancellationToken) =>
        packages.ImportAsync(request, cancellationToken);
}
