using System.Text.Json;
using CutLocal.Domain;
using CutLocal.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.UnitTests;

public sealed class JsonModelCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetAllAsync_LoadsReviewedNonCommercialManifestWithoutPretendingItIsCommercial()
    {
        ApplicationPaths paths = CreatePaths();
        Directory.CreateDirectory(paths.ManifestRoot);
        ModelDescriptor descriptor = ModelManifestValidatorTests.CreateDescriptor() with
        {
            Id = "bria-rmbg-2.0",
            FileName = "bria-rmbg-2.0.onnx",
            License = ModelManifestValidatorTests.CreateDescriptor().License with
            {
                Spdx = "CC-BY-NC-4.0",
                CommercialUseAllowed = false,
            },
        };
        await File.WriteAllTextAsync(
            Path.Combine(paths.ManifestRoot, "bria-rmbg-2.0.json"),
            JsonSerializer.Serialize(descriptor),
            CancellationToken.None);
        JsonModelCatalog sut = new(
            paths,
            new ModelManifestValidator(),
            NullLogger<JsonModelCatalog>.Instance);

        ModelDescriptor loaded = Assert.Single(await sut.GetAllAsync(CancellationToken.None));

        Assert.False(loaded.License.CommercialUseAllowed);
        Assert.Equal("CC-BY-NC-4.0", loaded.License.Spdx);
    }

    [Fact]
    public async Task GetAllAsync_LoadsCustomManifestOnlyWithAcceptanceReceipt()
    {
        ApplicationPaths paths = CreatePaths();
        Directory.CreateDirectory(paths.ManifestRoot);
        Directory.CreateDirectory(paths.CustomManifestRoot);
        ModelDescriptor descriptor = ModelManifestValidatorTests.CreateDescriptor() with
        {
            Id = "bria-user-supplied",
            FileName = "bria-user-supplied.onnx",
            License = ModelManifestValidatorTests.CreateDescriptor().License with
            {
                Spdx = "LicenseRef-BRIA-NC",
                CommercialUseAllowed = false,
            },
        };
        string manifestPath = Path.Combine(paths.CustomManifestRoot, "bria-user-supplied.1.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(descriptor),
            CancellationToken.None);
        JsonModelCatalog sut = new(
            paths,
            new ModelManifestValidator(),
            NullLogger<JsonModelCatalog>.Instance);

        Assert.Empty(await sut.GetAllAsync(CancellationToken.None));

        await File.WriteAllTextAsync(
            Path.ChangeExtension(manifestPath, ".accepted"),
            "accepted",
            CancellationToken.None);
        ModelDescriptor loaded = Assert.Single(await sut.GetAllAsync(CancellationToken.None));
        Assert.Equal(descriptor.Id, loaded.Id);
        Assert.False(loaded.License.CommercialUseAllowed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ApplicationPaths CreatePaths() => new()
    {
        DataRoot = _root,
        ModelRoot = Path.Combine(_root, "models"),
        LogRoot = Path.Combine(_root, "logs"),
        ManifestRoot = Path.Combine(_root, "manifests"),
        CustomManifestRoot = Path.Combine(_root, "custom-manifests"),
        ModelQuarantineRoot = Path.Combine(_root, "quarantine"),
    };
}
