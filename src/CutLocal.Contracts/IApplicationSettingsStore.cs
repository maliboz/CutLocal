using CutLocal.Domain;

namespace CutLocal.Contracts;

/// <summary>Loads and atomically saves non-secret per-user application preferences.</summary>
public interface IApplicationSettingsStore
{
    /// <summary>Loads validated settings or defaults when no readable settings exist.</summary>
    ValueTask<ApplicationSettings> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Atomically saves the supplied settings.</summary>
    ValueTask SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken);
}
