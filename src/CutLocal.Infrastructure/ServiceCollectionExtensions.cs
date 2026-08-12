using System.Net;
using CutLocal.Application;
using CutLocal.Contracts;
using CutLocal.Imaging;
using CutLocal.Inference;
using CutLocal.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CutLocal.Infrastructure;

/// <summary>Registers the local, network-free CutLocal processing graph.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds application, persistence, imaging, and provider-aware inference services.</summary>
    public static IServiceCollection AddCutLocalCore(
        this IServiceCollection services,
        ApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);

        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IModelManifestValidator, ModelManifestValidator>();
        services.AddSingleton<IModelCatalog, JsonModelCatalog>();
        services.AddSingleton<IModelPathResolver, ModelPathResolver>();
        services.AddSingleton<IBundledModelSeeder, BundledModelSeeder>();
        services.AddSingleton<IModelCompatibilityValidator, OnnxModelCompatibilityValidator>();
        services.AddSingleton<IApplicationSettingsStore, JsonApplicationSettingsStore>();
        services.AddSingleton<IProcessingJobStore, JsonProcessingJobStore>();
        services.AddSingleton<IMemoryPressureGate, LocalMemoryPressureGate>();
        services.AddSingleton<IImageDecoder, SafePngDecoder>();
        services.AddSingleton<IMaskCompositor, BilinearAlphaCompositor>();
        services.AddSingleton<IAtomicImageWriter, AtomicPngWriter>();
        services.AddSingleton<IInferenceProviderCatalog, WindowsInferenceProviderCatalog>();
        services.AddSingleton<ProviderSelectionService>();
        services.AddSingleton<U2NetModelAdapterFactory>();
        services.AddSingleton<IModelAdapterSessionCache>(provider =>
            provider.GetRequiredService<U2NetModelAdapterFactory>());
        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            MaxConnectionsPerServer = 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        });
        services.AddSingleton<IModelPackageManager, ModelPackageManager>();
        services.AddSingleton<IRemoveBackgroundProcessor, LocalBackgroundRemovalProcessor>();
        services.AddSingleton<IHardwareBenchmarkService, LocalHardwareBenchmarkService>();
        services.AddTransient<RemoveBackgroundUseCase>();
        services.AddTransient<AddImagesUseCase>();
        services.AddTransient<AddFolderUseCase>();
        services.AddTransient<RetryFailedItemsUseCase>();
        services.AddTransient<RemoveBatchItemsUseCase>();
        services.AddTransient<ReconfigureBatchUseCase>();
        services.AddTransient<RecoverInterruptedJobUseCase>();
        services.AddTransient<ProcessBatchUseCase>();
        services.AddTransient<BenchmarkHardwareUseCase>();
        services.AddTransient<ModelManagementUseCase>();
        return services;
    }
}
