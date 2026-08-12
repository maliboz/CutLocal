using CutLocal.Domain;
using CutLocal.Persistence;

namespace CutLocal.UnitTests;

public sealed class ModelManifestValidatorTests
{
    private readonly ModelManifestValidator _validator = new();

    [Fact]
    public void Validate_AcceptsCompleteCommercialU2NetManifest()
    {
        ModelDescriptor descriptor = CreateDescriptor();

        IReadOnlyList<string> errors = _validator.Validate(descriptor, commercialBuild: true);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsMissingHashAndLicense()
    {
        ModelDescriptor descriptor = CreateDescriptor() with
        {
            Sha256 = "unknown",
            License = CreateDescriptor().License with { Spdx = string.Empty },
        };

        IReadOnlyList<string> errors = _validator.Validate(descriptor, commercialBuild: true);

        Assert.Contains(errors, message => message.Contains("sha256", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, message => message.Contains("SPDX", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_BlocksBriaFromCommercialBuildEvenWhenManifestClaimIsPermissive()
    {
        ModelDescriptor descriptor = CreateDescriptor() with { Id = "bria-rmbg-2.0" };

        IReadOnlyList<string> errors = _validator.Validate(descriptor, commercialBuild: true);

        Assert.Contains(errors, message => message.Contains("commercial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AllowsBriaForExplicitNonCommercialCatalog()
    {
        ModelDescriptor descriptor = CreateDescriptor() with
        {
            Id = "bria-rmbg-2.0",
            License = CreateDescriptor().License with
            {
                Spdx = "CC-BY-NC-4.0",
                CommercialUseAllowed = false,
            },
        };

        IReadOnlyList<string> errors = _validator.Validate(descriptor, commercialBuild: false);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsMissingSizeAndUnknownActivation()
    {
        ModelDescriptor descriptor = CreateDescriptor() with
        {
            FileSizeBytes = 0,
            Output = CreateDescriptor().Output with { Activation = "guess" },
        };

        IReadOnlyList<string> errors = _validator.Validate(descriptor, commercialBuild: true);

        Assert.Contains(errors, message => message.Contains("fileSizeBytes", StringComparison.Ordinal));
        Assert.Contains(errors, message => message.Contains("activation", StringComparison.OrdinalIgnoreCase));
    }

    internal static ModelDescriptor CreateDescriptor(int width = 320, int height = 320) => new()
    {
        Id = "u2netp",
        DisplayName = "U2NetP Fast",
        Version = "1",
        FileName = "u2netp.onnx",
        Sha256 = new string('a', 64),
        FileSizeBytes = 1024,
        DownloadUrl = "https://example.test/u2netp.onnx",
        License = new ModelLicenseDescriptor
        {
            Spdx = "Apache-2.0",
            CommercialUseAllowed = true,
            AttributionRequired = true,
            Source = "https://example.test/license",
        },
        Input = new ModelInputDescriptor
        {
            Width = width,
            Height = height,
            Layout = "NCHW",
            ColorOrder = "RGB",
            Mean = [0.485, 0.456, 0.406],
            Std = [0.229, 0.224, 0.225],
            ResizeMode = "stretch",
        },
        Output = new ModelOutputDescriptor
        {
            Activation = "minmax",
            Type = "alpha-mask",
        },
        RecommendedMemoryMb = 1024,
        Tier = "fast",
        SupportedProviders = ["cpu"],
    };
}
