using CutLocal.Contracts;
using CutLocal.Domain;
using CutLocal.Inference;
using Microsoft.ML.OnnxRuntime;

namespace CutLocal.UnitTests;

public sealed class ProviderPolicyTests
{
    private static readonly InferenceProviderDescriptor DirectMl = new()
    {
        Kind = InferenceProviderKind.DirectMl,
        Id = "directml:luid:test",
        DisplayName = "Test GPU",
        IsReadyOffline = true,
        MaxRecommendedConcurrency = 1,
        DeviceIndex = 2,
        DeviceIdentity = "luid:test",
        DedicatedVideoMemoryBytes = 8L * 1024 * 1024 * 1024,
    };

    [Fact]
    public void DirectMlPolicy_DisablesMemoryPatternsAndParallelExecution()
    {
        ProviderSessionPolicy policy = ProviderSessionOptions.GetPolicy(DirectMl);

        Assert.False(policy.EnableMemoryPattern);
        Assert.Equal(ExecutionMode.ORT_SEQUENTIAL, policy.ExecutionMode);
        Assert.Equal(1, policy.IntraOpNumThreads);
        Assert.Equal(1, policy.InterOpNumThreads);
    }

    [Fact]
    public void GpuFallbackPolicy_AllowsOnlyOneEligibleDirectMlRecovery()
    {
        InferenceException eligible = new(
            ProcessingErrorCategory.ProviderUnavailable,
            "DML_DEVICE_LOST",
            "device lost",
            isGpuFallbackEligible: true);

        Assert.True(GpuFallbackPolicy.ShouldRetryOnCpu(DirectMl, eligible, false));
        Assert.False(GpuFallbackPolicy.ShouldRetryOnCpu(DirectMl, eligible, true));
        Assert.False(GpuFallbackPolicy.ShouldRetryOnCpu(
            WindowsInferenceProviderCatalog.Cpu,
            eligible,
            false));
    }

    [Theory]
    [InlineData("DirectML failed: E_OUTOFMEMORY", ProcessingErrorCategory.GpuOutOfMemory, "DML_GPU_OOM")]
    [InlineData("DXGI_ERROR_DEVICE_REMOVED", ProcessingErrorCategory.ProviderUnavailable, "DML_DEVICE_LOST")]
    [InlineData("operator failed", ProcessingErrorCategory.InferenceFailed, "ORT_DML_RUN_FAILED")]
    public void FailureClassifier_MapsGpuFailures(
        string message,
        ProcessingErrorCategory category,
        string logCode)
    {
        InferenceException classified = InferenceFailureClassifier.ClassifyRunFailure(
            new InvalidOperationException(message),
            DirectMl);

        Assert.Equal(category, classified.Category);
        Assert.Equal(logCode, classified.LogCode);
        Assert.Equal(category is not ProcessingErrorCategory.InferenceFailed, classified.IsGpuFallbackEligible);
    }

    [Fact]
    public async Task AutoSelection_OrdersGpusByMemoryThenCpu()
    {
        InferenceProviderDescriptor smallGpu = DirectMl with
        {
            Id = "directml:small",
            DeviceIndex = 0,
            DedicatedVideoMemoryBytes = 2L * 1024 * 1024 * 1024,
        };
        InferenceProviderDescriptor largeGpu = DirectMl with
        {
            Id = "directml:large",
            DeviceIndex = 1,
        };
        ProviderSelectionService selection = new(new StubCatalog(
            [smallGpu, largeGpu, WindowsInferenceProviderCatalog.Cpu]));

        IReadOnlyList<InferenceProviderDescriptor> candidates =
            await selection.GetCandidatesAsync(
                CreateDescriptor(),
                InferenceProviderKind.Auto,
                directMlAdapterIndex: null,
                CancellationToken.None);

        Assert.Equal(["directml:large", "directml:small", "cpu"], candidates.Select(item => item.Id));
    }

    [Fact]
    public async Task ExplicitAdapterSelection_FiltersOtherGpusAndRetainsCpuFallback()
    {
        InferenceProviderDescriptor otherGpu = DirectMl with { Id = "directml:other", DeviceIndex = 0 };
        ProviderSelectionService selection = new(new StubCatalog(
            [otherGpu, DirectMl, WindowsInferenceProviderCatalog.Cpu]));

        IReadOnlyList<InferenceProviderDescriptor> candidates =
            await selection.GetCandidatesAsync(
                CreateDescriptor(),
                InferenceProviderKind.DirectMl,
                directMlAdapterIndex: 2,
                CancellationToken.None);

        Assert.Equal([DirectMl.Id, "cpu"], candidates.Select(item => item.Id));
    }

    private static ModelDescriptor CreateDescriptor() => new()
    {
        Id = "u2netp",
        DisplayName = "test",
        Version = "1",
        FileName = "model.onnx",
        Sha256 = new string('A', 64),
        DownloadUrl = "https://example.test/model.onnx",
        License = new ModelLicenseDescriptor
        {
            Spdx = "Apache-2.0",
            CommercialUseAllowed = true,
            AttributionRequired = true,
            Source = "https://example.test/license",
        },
        Input = new ModelInputDescriptor
        {
            Width = 320,
            Height = 320,
            Layout = "NCHW",
            ColorOrder = "RGB",
            Mean = [0.485, 0.456, 0.406],
            Std = [0.229, 0.224, 0.225],
            ResizeMode = "stretch",
        },
        Output = new ModelOutputDescriptor { Activation = "minmax", Type = "alpha-mask" },
        RecommendedMemoryMb = 1024,
        Tier = "test",
        SupportedProviders = ["cpu", "directml"],
    };

    private sealed class StubCatalog(IReadOnlyList<InferenceProviderDescriptor> providers)
        : IInferenceProviderCatalog
    {
        public ValueTask<IReadOnlyList<InferenceProviderDescriptor>> GetAllAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(providers);
    }
}
