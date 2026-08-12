using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Persistence;

/// <summary>Applies schema, safety, and commercial-distribution checks to model metadata.</summary>
public sealed class ModelManifestValidator : IModelManifestValidator
{
    private static readonly char[] InvalidIdCharacters = Path.GetInvalidFileNameChars();

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(ModelDescriptor descriptor, bool commercialBuild)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        List<string> errors = [];
        ValidateIdentity(descriptor, errors);
        ValidateHashAndUris(descriptor, errors);
        ValidateLicense(descriptor, commercialBuild, errors);
        ValidateTensor(descriptor, errors);
        return errors;
    }

    private static void ValidateIdentity(ModelDescriptor descriptor, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id)
            || descriptor.Id.IndexOfAny(InvalidIdCharacters) >= 0
            || descriptor.Id.Contains("..", StringComparison.Ordinal))
        {
            errors.Add("Model id is missing or is not a safe directory segment.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.Version)
            || descriptor.Version.IndexOfAny(InvalidIdCharacters) >= 0
            || descriptor.Version.Contains("..", StringComparison.Ordinal))
        {
            errors.Add("Model version is missing or is not a safe directory segment.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.FileName)
            || !string.Equals(descriptor.FileName, Path.GetFileName(descriptor.FileName), StringComparison.Ordinal)
            || !descriptor.FileName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Model fileName must be a leaf .onnx file name.");
        }
    }

    private static void ValidateHashAndUris(ModelDescriptor descriptor, List<string> errors)
    {
        if (descriptor.Sha256.Length != 64 || !descriptor.Sha256.All(Uri.IsHexDigit))
        {
            errors.Add("Model sha256 must contain exactly 64 hexadecimal characters.");
        }

        if (descriptor.FileSizeBytes <= 0 || descriptor.FileSizeBytes > 4L * 1024 * 1024 * 1024)
        {
            errors.Add("Model fileSizeBytes must be between 1 byte and 4 GiB.");
        }

        if (!IsHttpsUri(descriptor.DownloadUrl))
        {
            errors.Add("Model downloadUrl must be an absolute HTTPS URI.");
        }

        if (!IsHttpsUri(descriptor.License.Source))
        {
            errors.Add("Model license source must be an absolute HTTPS URI.");
        }
    }

    private static void ValidateLicense(
        ModelDescriptor descriptor,
        bool commercialBuild,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(descriptor.License.Spdx))
        {
            errors.Add("Model license SPDX expression is required.");
        }

        bool isBria = descriptor.Id.Contains("bria", StringComparison.OrdinalIgnoreCase);
        bool isNonCommercial = descriptor.License.Spdx.Contains("NC", StringComparison.OrdinalIgnoreCase);
        if (commercialBuild && (!descriptor.License.CommercialUseAllowed || isBria || isNonCommercial))
        {
            errors.Add("Model is not permitted in a commercial build.");
        }
    }

    private static void ValidateTensor(ModelDescriptor descriptor, List<string> errors)
    {
        if (descriptor.Input.Width <= 0 || descriptor.Input.Height <= 0)
        {
            errors.Add("Model input dimensions must be positive.");
        }

        if (!string.Equals(descriptor.Input.Layout, "NCHW", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The current adapters support only NCHW input layout.");
        }

        if (!string.Equals(descriptor.Input.ColorOrder, "RGB", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The current adapters support only RGB input order.");
        }

        if (descriptor.Input.Mean.Count != 3
            || descriptor.Input.Std.Count != 3
            || descriptor.Input.Std.Any(value => value <= 0))
        {
            errors.Add("Model input mean/std must each contain three channels with positive standard deviations.");
        }

        if (!string.Equals(descriptor.Output.Type, "alpha-mask", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The current adapters support only alpha-mask output.");
        }


        if (!descriptor.Output.Activation.Equals("minmax", StringComparison.OrdinalIgnoreCase)
            && !descriptor.Output.Activation.Equals("sigmoid-minmax", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Model output activation must be minmax or sigmoid-minmax.");
        }

        if (descriptor.RecommendedMemoryMb <= 0 || descriptor.SupportedProviders.Count == 0)
        {
            errors.Add("Model memory recommendation and supported providers are required.");
        }

        if (descriptor.SupportedProviders.Any(provider =>
                !provider.Equals("cpu", StringComparison.OrdinalIgnoreCase)
                && !provider.Equals("directml", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Model supportedProviders may contain only cpu or directml.");
        }

        if (!descriptor.Input.ResizeMode.Equals("stretch", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The current adapters support only stretch resize mode.");
        }
    }

    private static bool IsHttpsUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
