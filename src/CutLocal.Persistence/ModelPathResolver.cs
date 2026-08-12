using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Persistence;

/// <summary>Resolves models inside the controlled per-user model directory.</summary>
public sealed class ModelPathResolver : IModelPathResolver
{
    private readonly string _modelRoot;

    /// <summary>Initializes the resolver with application paths.</summary>
    public ModelPathResolver(ApplicationPaths paths)
    {
        _modelRoot = Path.GetFullPath(paths.ModelRoot);
    }

    /// <inheritdoc />
    public string GetModelPath(ModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        string candidate = Path.GetFullPath(
            Path.Combine(_modelRoot, descriptor.Id, descriptor.Version, descriptor.FileName));
        string rootedPrefix = _modelRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _modelRoot
            : _modelRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The model manifest resolves outside the controlled model root.");
        }

        return candidate;
    }
}
