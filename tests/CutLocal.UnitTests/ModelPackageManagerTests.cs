using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Inference;
using CutLocal.Infrastructure;
using CutLocal.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace CutLocal.UnitTests;

public sealed class ModelPackageManagerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CutLocal.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadAsync_StreamsAndInstallsOnlyVerifiedPackage()
    {
        byte[] payload = "verified-model"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        RecordingHandler handler = new((request, _) =>
        {
            Assert.Null(request.Headers.Range);
            return Response(HttpStatusCode.OK, payload);
        });
        ModelPackageManager sut = CreateManager(descriptor, handler);

        ModelPackageOperationResult result = await sut.DownloadAsync(
            descriptor,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(ModelInstallationState.Installed, result.State);
        Assert.Equal(payload, await File.ReadAllBytesAsync(
            GetModelPath(descriptor),
            CancellationToken.None));
        Assert.False(File.Exists(GetModelPath(descriptor) + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_NonCommercialModelRequiresAcknowledgementBeforeNetworkAccess()
    {
        byte[] payload = "restricted-model"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload) with
        {
            Id = "bria-rmbg-2.0",
            FileName = "bria-rmbg-2.0.onnx",
            License = CreateDescriptor(payload).License with
            {
                Spdx = "CC-BY-NC-4.0",
                CommercialUseAllowed = false,
            },
        };
        RecordingHandler handler = new((_, _) => Response(HttpStatusCode.OK, payload));
        ModelPackageManager sut = CreateManager(descriptor, handler);

        ModelPackageOperationResult declined = await sut.DownloadAsync(
            descriptor,
            progress: null,
            CancellationToken.None);

        Assert.False(declined.Succeeded);
        Assert.Equal("MODEL_LICENSE_ACK_REQUIRED", declined.Code);
        Assert.Equal(0, handler.CallCount);

        ModelPackageOperationResult accepted = await sut.DownloadAsync(
            descriptor,
            progress: null,
            CancellationToken.None,
            licenseAcknowledged: true);

        Assert.True(accepted.Succeeded);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_ResumesOnlyFromValidatedContentRange()
    {
        byte[] payload = "range-resume-model"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        string partialPath = GetModelPath(descriptor) + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(
            partialPath,
            payload[..6],
            CancellationToken.None);
        RecordingHandler handler = new((request, _) =>
        {
            Assert.Equal(6, request.Headers.Range?.Ranges.Single().From);
            HttpResponseMessage response = Response(HttpStatusCode.PartialContent, payload[6..]);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                6,
                payload.Length - 1,
                payload.Length);
            return response;
        });
        ModelPackageManager sut = CreateManager(descriptor, handler);

        ModelPackageOperationResult result = await sut.DownloadAsync(
            descriptor,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(payload, await File.ReadAllBytesAsync(
            GetModelPath(descriptor),
            CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_WhenRangeIsIgnored_RestartsInsteadOfAppending()
    {
        byte[] payload = "server-ignores-range"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        string partialPath = GetModelPath(descriptor) + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(
            partialPath,
            payload[..5],
            CancellationToken.None);
        int call = 0;
        RecordingHandler handler = new((request, _) =>
        {
            call++;
            if (call == 1)
            {
                Assert.NotNull(request.Headers.Range);
            }
            else
            {
                Assert.Null(request.Headers.Range);
            }

            return Response(HttpStatusCode.OK, payload);
        });
        ModelPackageManager sut = CreateManager(descriptor, handler);

        ModelPackageOperationResult result = await sut.DownloadAsync(
            descriptor,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(payload, await File.ReadAllBytesAsync(
            GetModelPath(descriptor),
            CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_OnHashMismatch_QuarantinesContent()
    {
        byte[] expected = "expected-model"u8.ToArray();
        byte[] corrupt = "corrupted-mode"u8.ToArray();
        Assert.Equal(expected.Length, corrupt.Length);
        ModelDescriptor descriptor = CreateDescriptor(expected);
        ModelPackageManager sut = CreateManager(
            descriptor,
            new RecordingHandler((_, _) => Response(HttpStatusCode.OK, corrupt)));

        ModelPackageOperationResult result = await sut.DownloadAsync(
            descriptor,
            progress: null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ModelInstallationState.Corrupted, result.State);
        Assert.False(File.Exists(GetModelPath(descriptor)));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "quarantine")));
    }

    [Fact]
    public async Task DownloadAsync_RepeatedHashMismatchRetainsOnlyThreeNewestQuarantineFiles()
    {
        byte[] expected = "expected-model"u8.ToArray();
        byte[] corrupt = "corrupted-mode"u8.ToArray();
        Assert.Equal(expected.Length, corrupt.Length);
        ModelDescriptor descriptor = CreateDescriptor(expected);
        ModelPackageManager sut = CreateManager(
            descriptor,
            new RecordingHandler((_, _) => Response(HttpStatusCode.OK, corrupt)));

        for (int attempt = 0; attempt < 4; attempt++)
        {
            ModelPackageOperationResult result = await sut.DownloadAsync(
                descriptor,
                progress: null,
                CancellationToken.None);
            Assert.False(result.Succeeded);
            Assert.Equal(ModelInstallationState.Corrupted, result.State);
        }

        string[] quarantined = Directory.GetFiles(Path.Combine(_root, "quarantine"), "*.onnx");
        Assert.Equal(3, quarantined.Length);
        Assert.All(quarantined, path => Assert.StartsWith(
            $"{descriptor.Id}-{descriptor.Version}-",
            Path.GetFileName(path),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InspectAllAsync_IsOfflineAndDetectsPartialPackage()
    {
        byte[] payload = "offline-inspect"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        string partialPath = GetModelPath(descriptor) + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(
            partialPath,
            payload[..4],
            CancellationToken.None);
        RecordingHandler handler = new((_, _) => throw new InvalidOperationException("Network must stay idle."));
        ModelPackageManager sut = CreateManager(descriptor, handler);

        IReadOnlyList<ModelInstallationInfo> result = await sut.InspectAllAsync(
            CancellationToken.None);

        ModelInstallationInfo item = Assert.Single(result);
        Assert.Equal(ModelInstallationState.Partial, item.State);
        Assert.Equal(4, item.LocalBytes);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFinalAndPartialPackage()
    {
        byte[] payload = "delete-model"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        string finalPath = GetModelPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        await File.WriteAllBytesAsync(finalPath, payload, CancellationToken.None);
        await File.WriteAllBytesAsync(
            finalPath + ".partial",
            payload[..3],
            CancellationToken.None);
        ModelPackageManager sut = CreateManager(
            descriptor,
            new RecordingHandler((_, _) => throw new InvalidOperationException()));

        ModelPackageOperationResult result = await sut.DeleteAsync(
            descriptor,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(finalPath));
        Assert.False(File.Exists(finalPath + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_WhenCancelled_PreservesResumablePartialFile()
    {
        byte[] payload = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        ModelPackageManager sut = CreateManager(
            descriptor,
            new RecordingHandler((_, _) => Response(HttpStatusCode.OK, payload)));
        using CancellationTokenSource cancellation = new();
        InlineProgress progress = new(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.DownloadAsync(
            descriptor,
            progress,
            cancellation.Token).AsTask());

        Assert.True(File.Exists(GetModelPath(descriptor) + ".partial"));
        Assert.False(File.Exists(GetModelPath(descriptor)));
    }

    [Fact]
    public async Task RepairAsync_QuarantinesCorruptFinalAndInstallsVerifiedPackage()
    {
        byte[] payload = "repair-model"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        string finalPath = GetModelPath(descriptor);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        await File.WriteAllBytesAsync(finalPath, "broken-model"u8.ToArray(), CancellationToken.None);
        ModelPackageManager sut = CreateManager(
            descriptor,
            new RecordingHandler((_, _) => Response(HttpStatusCode.OK, payload)));

        ModelPackageOperationResult result = await sut.RepairAsync(
            descriptor,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(payload, await File.ReadAllBytesAsync(finalPath, CancellationToken.None));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "quarantine")));
    }

    [Fact]
    public async Task ImportAsync_WithAcknowledgedValidManifest_AtomicallyInstallsCustomModel()
    {
        Directory.CreateDirectory(_root);
        byte[] payload = "custom-onnx-placeholder"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload) with
        {
            Id = "custom-test-model",
            FileName = "custom-test-model.onnx",
        };
        string onnxPath = Path.Combine(_root, "source.onnx");
        string manifestPath = Path.Combine(_root, "source.json");
        await File.WriteAllBytesAsync(onnxPath, payload, CancellationToken.None);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(descriptor),
            CancellationToken.None);
        ModelPackageManager sut = CreateManager(
            descriptor,
            new RecordingHandler((_, _) => throw new InvalidOperationException()),
            catalogContainsDescriptor: false);

        ModelPackageOperationResult result = await sut.ImportAsync(
            new ModelImportRequest
            {
                OnnxPath = onnxPath,
                ManifestPath = manifestPath,
                LicenseAcknowledged = true,
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(payload, await File.ReadAllBytesAsync(
            GetModelPath(descriptor),
            CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(
            _root,
            "custom-manifests",
            "custom-test-model.1.json")));
        Assert.True(File.Exists(Path.Combine(
            _root,
            "custom-manifests",
            "custom-test-model.1.accepted")));
    }

    [Fact]
    public async Task ImportAsync_WithoutLicenseAcknowledgement_FailsBeforeReadingFiles()
    {
        byte[] payload = "license-model"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        ModelPackageManager sut = CreateManager(
            descriptor,
            new RecordingHandler((_, _) => throw new InvalidOperationException()),
            catalogContainsDescriptor: false);

        ModelPackageOperationResult result = await sut.ImportAsync(
            new ModelImportRequest
            {
                OnnxPath = Path.Combine(_root, "missing.onnx"),
                ManifestPath = Path.Combine(_root, "missing.json"),
                LicenseAcknowledged = false,
            },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("MODEL_LICENSE_ACK_REQUIRED", result.Code);
    }

    [Fact]
    public async Task DownloadAsync_WhenCompletePartialGets416_VerifiesAndInstallsIt()
    {
        byte[] payload = "complete-partial"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        string partialPath = GetModelPath(descriptor) + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, payload, CancellationToken.None);
        RecordingHandler handler = new((request, _) =>
        {
            Assert.Equal(payload.Length, request.Headers.Range?.Ranges.Single().From);
            HttpResponseMessage response = Response(
                HttpStatusCode.RequestedRangeNotSatisfiable,
                []);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(payload.Length);
            return response;
        });
        ModelPackageManager sut = CreateManager(descriptor, handler);

        ModelPackageOperationResult result = await sut.DownloadAsync(
            descriptor,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(GetModelPath(descriptor)));
        Assert.False(File.Exists(partialPath));
    }

    [Fact]
    public async Task DownloadAsync_FollowsOnlyHttpsRedirects()
    {
        byte[] payload = "redirect-model"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        int call = 0;
        RecordingHandler handler = new((request, _) =>
        {
            call++;
            if (call == 1)
            {
                HttpResponseMessage redirect = Response(HttpStatusCode.Found, []);
                redirect.Headers.Location = new Uri("https://cdn.example.test/model.onnx");
                return redirect;
            }

            Assert.Equal("cdn.example.test", request.RequestUri?.Host);
            return Response(HttpStatusCode.OK, payload);
        });
        ModelPackageManager sut = CreateManager(descriptor, handler);

        ModelPackageOperationResult result = await sut.DownloadAsync(
            descriptor,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_RejectsRedirectToPlainHttp()
    {
        byte[] payload = "redirect-block"u8.ToArray();
        ModelDescriptor descriptor = CreateDescriptor(payload);
        RecordingHandler handler = new((_, _) =>
        {
            HttpResponseMessage redirect = Response(HttpStatusCode.Found, []);
            redirect.Headers.Location = new Uri("http://cdn.example.test/model.onnx");
            return redirect;
        });
        ModelPackageManager sut = CreateManager(descriptor, handler);

        ModelPackageOperationResult result = await sut.DownloadAsync(
            descriptor,
            progress: null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("MODEL_DOWNLOAD_FAILED", result.Code);
        Assert.False(File.Exists(GetModelPath(descriptor)));
    }

    [Fact]
    public async Task DownloadAsync_RejectsDescriptorBehaviorThatDiffersFromReviewedCatalog()
    {
        byte[] payload = "catalog-bound"u8.ToArray();
        ModelDescriptor catalogDescriptor = CreateDescriptor(payload);
        ModelDescriptor tampered = catalogDescriptor with
        {
            Input = catalogDescriptor.Input with { Width = 2 },
        };
        RecordingHandler handler = new((_, _) => throw new InvalidOperationException());
        ModelPackageManager sut = CreateManager(catalogDescriptor, handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => sut.DownloadAsync(
            tampered,
            progress: null,
            CancellationToken.None).AsTask());

        Assert.Equal(0, handler.CallCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ModelPackageManager CreateManager(
        ModelDescriptor descriptor,
        HttpMessageHandler handler,
        bool catalogContainsDescriptor = true)
    {
        ApplicationPaths paths = Paths();
        return new ModelPackageManager(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new StaticCatalog(catalogContainsDescriptor ? descriptor : null),
            new ModelManifestValidator(),
            new AcceptCompatibilityValidator(),
            new ModelPathResolver(paths),
            new NoOpSessionCache(),
            paths,
            TimeProvider.System,
            NullLogger<ModelPackageManager>.Instance);
    }

    private ApplicationPaths Paths() => new()
    {
        DataRoot = _root,
        ModelRoot = Path.Combine(_root, "models"),
        LogRoot = Path.Combine(_root, "logs"),
        ManifestRoot = Path.Combine(_root, "manifests"),
        CustomManifestRoot = Path.Combine(_root, "custom-manifests"),
        ModelQuarantineRoot = Path.Combine(_root, "quarantine"),
    };

    private string GetModelPath(ModelDescriptor descriptor) =>
        new ModelPathResolver(Paths()).GetModelPath(descriptor);

    private static ModelDescriptor CreateDescriptor(byte[] payload) => new()
    {
        Id = "test-model",
        DisplayName = "Test Model",
        Version = "1",
        FileName = "test-model.onnx",
        Sha256 = Convert.ToHexString(SHA256.HashData(payload)),
        FileSizeBytes = payload.Length,
        DownloadUrl = "https://example.test/model.onnx",
        License = new ModelLicenseDescriptor
        {
            Spdx = "MIT",
            CommercialUseAllowed = true,
            AttributionRequired = true,
            Source = "https://example.test/license",
        },
        Input = new ModelInputDescriptor
        {
            Width = 1,
            Height = 1,
            Layout = "NCHW",
            ColorOrder = "RGB",
            Mean = [0, 0, 0],
            Std = [1, 1, 1],
            ResizeMode = "stretch",
        },
        Output = new ModelOutputDescriptor
        {
            Activation = "minmax",
            Type = "alpha-mask",
        },
        RecommendedMemoryMb = 64,
        Tier = "test",
        SupportedProviders = ["cpu"],
    };

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] payload) => new(status)
    {
        Content = new ByteArrayContent(payload),
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            HttpResponseMessage response = responseFactory(request, cancellationToken);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private sealed class StaticCatalog(ModelDescriptor? descriptor) : IModelCatalog
    {
        public ValueTask<ModelDescriptor?> GetByIdAsync(
            string modelId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<ModelDescriptor?>(
                descriptor?.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase) == true
                    ? descriptor
                    : null);

        public ValueTask<IReadOnlyList<ModelDescriptor>> GetAllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ModelDescriptor>>(
                descriptor is null ? [] : [descriptor]);
    }

    private sealed class AcceptCompatibilityValidator : IModelCompatibilityValidator
    {
        public ValueTask ValidateAsync(
            ModelDescriptor descriptor,
            string modelPath,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class NoOpSessionCache : IModelAdapterSessionCache
    {
        public ValueTask<IModelAdapterLease> AcquireAsync(
            ModelDescriptor descriptor,
            string modelPath,
            InferenceProviderDescriptor provider,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class InlineProgress(Action<ModelTransferProgress> report)
        : IProgress<ModelTransferProgress>
    {
        public void Report(ModelTransferProgress value) => report(value);
    }
}
