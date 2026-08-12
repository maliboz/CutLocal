namespace CutLocal.Contracts;

/// <summary>Activates hash-verified model packs shipped beside the application.</summary>
public interface IBundledModelSeeder
{
    /// <summary>Copies valid bundled models into controlled per-user storage and returns the activation count.</summary>
    ValueTask<int> SeedAsync(CancellationToken cancellationToken);
}
