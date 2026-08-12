namespace CutLocal.Application;

/// <summary>Builds deterministic output names without touching the file system.</summary>
public static class OutputPathPolicy
{
    /// <summary>Creates a sibling PNG path with the CutLocal suffix.</summary>
    public static string CreateSiblingPngPath(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        string fullInputPath = Path.GetFullPath(inputPath);
        string? directory = Path.GetDirectoryName(fullInputPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("The input path must have a parent directory.", nameof(inputPath));
        }

        string stem = Path.GetFileNameWithoutExtension(fullInputPath);
        return Path.Combine(directory, $"{stem}.cutlocal.png");
    }

    /// <summary>Creates a PNG path in the selected directory with a validated suffix.</summary>
    public static string CreatePngPath(
        string inputPath,
        string outputDirectory,
        string? fileNameSuffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string suffix = string.IsNullOrWhiteSpace(fileNameSuffix)
            ? ".cutlocal"
            : fileNameSuffix.Trim();
        if (!suffix.StartsWith('.'))
        {
            suffix = $".{suffix}";
        }

        if (suffix.Length > 64
            || suffix.Contains("..", StringComparison.Ordinal)
            || suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || suffix.Contains(Path.DirectorySeparatorChar)
            || suffix.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "The output filename suffix contains unsafe characters.",
                nameof(fileNameSuffix));
        }

        string stem = Path.GetFileNameWithoutExtension(Path.GetFullPath(inputPath));
        return Path.Combine(Path.GetFullPath(outputDirectory), $"{stem}{suffix}.png");
    }
}
