using CutLocal.Domain;

namespace CutLocal.Inference;

/// <summary>Leases warmed model sessions from a bounded provider-aware cache.</summary>
public interface IModelAdapterSessionCache
{
    /// <summary>Acquires a session lease for one model/provider/device key.</summary>
    ValueTask<IModelAdapterLease> AcquireAsync(
        ModelDescriptor descriptor,
        string modelPath,
        InferenceProviderDescriptor provider,
        CancellationToken cancellationToken);

    /// <summary>Invalidates cached sessions that use one local model path.</summary>
    ValueTask InvalidateAsync(string modelPath, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

/// <summary>Protects one cached native session from eviction while it is in use.</summary>
public interface IModelAdapterLease : IAsyncDisposable
{
    /// <summary>Gets the leased model adapter.</summary>
    IBackgroundRemovalModelAdapter Adapter { get; }
    /// <summary>Gets the concrete provider/device.</summary>
    InferenceProviderDescriptor Provider { get; }
    /// <summary>Marks the session unusable after a provider failure.</summary>
    void Invalidate();
}
