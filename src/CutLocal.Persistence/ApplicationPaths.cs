namespace CutLocal.Persistence;

/// <summary>Contains controlled application data and content roots.</summary>
public sealed record ApplicationPaths
{
    /// <summary>Gets the per-user application data root.</summary>
    public required string DataRoot { get; init; }
    /// <summary>Gets the per-user model root.</summary>
    public required string ModelRoot { get; init; }
    /// <summary>Gets the per-user log root.</summary>
    public required string LogRoot { get; init; }
    /// <summary>Gets the installed manifest directory.</summary>
    public required string ManifestRoot { get; init; }
    /// <summary>Gets the controlled per-user custom manifest directory.</summary>
    public string CustomManifestRoot { get; init; } = string.Empty;
    /// <summary>Gets the directory that isolates failed model downloads.</summary>
    public string ModelQuarantineRoot { get; init; } = string.Empty;
    /// <summary>Gets the read-only model packs shipped beside the application.</summary>
    public string BundledModelRoot { get; init; } = string.Empty;

    /// <summary>Creates production paths from the current process environment.</summary>
    public static ApplicationPaths CreateDefault(string? contentRoot = null)
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dataRoot = Path.Combine(localData, "CutLocal");
        string applicationContentRoot = string.IsNullOrWhiteSpace(contentRoot)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(contentRoot);

        return new ApplicationPaths
        {
            DataRoot = dataRoot,
            ModelRoot = Path.Combine(dataRoot, "models"),
            LogRoot = Path.Combine(dataRoot, "logs"),
            ManifestRoot = Path.Combine(applicationContentRoot, "assets", "models", "manifests"),
            CustomManifestRoot = Path.Combine(dataRoot, "model-manifests"),
            ModelQuarantineRoot = Path.Combine(dataRoot, "model-quarantine"),
            BundledModelRoot = Path.Combine(applicationContentRoot, "models"),
        };
    }
}
