using System.Formats.Tar;
using System.IO.Compression;

return ArchiveProgram.Run(args);

internal static class ArchiveProgram
{
    private const UnixFileMode ReadOnlyFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode ExecutableFileMode = ReadOnlyFileMode |
        UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    public static int Run(string[] args)
    {
        try
        {
            (string source, string output, string executable) = ParseArguments(args);
            CreateArchive(source, output, executable);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void CreateArchive(string source, string output, string executable)
    {
        string sourceRoot = Path.GetFullPath(source).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string outputPath = Path.GetFullPath(output);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"Archive source does not exist: {sourceRoot}");
        }

        string sourcePrefix = sourceRoot + Path.DirectorySeparatorChar;
        if (outputPath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Archive output cannot be located inside its source directory.");
        }

        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Archive output has no parent directory.");
        }

        Directory.CreateDirectory(outputDirectory);
        File.Delete(outputPath);
        using FileStream file = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using GZipStream gzip = new(file, CompressionLevel.SmallestSize, leaveOpen: false);
        using TarWriter writer = new(gzip, TarEntryFormat.Pax, leaveOpen: false);

        IEnumerable<string> entries = Directory
            .EnumerateFileSystemEntries(sourceRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => GetDepth(sourceRoot, path))
            .ThenBy(path => path, StringComparer.Ordinal);
        foreach (string path in entries)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Archive staging cannot contain reparse points: {path}");
            }

            string relativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            if ((attributes & FileAttributes.Directory) != 0)
            {
                PaxTarEntry directoryEntry = new(TarEntryType.Directory, relativePath + "/")
                {
                    Mode = ExecutableFileMode,
                    ModificationTime = File.GetLastWriteTimeUtc(path),
                };
                writer.WriteEntry(directoryEntry);
                continue;
            }

            bool isExecutable = relativePath.Equals(executable, StringComparison.Ordinal)
                || relativePath.EndsWith(".command", StringComparison.OrdinalIgnoreCase)
                || relativePath.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
                || relativePath.EndsWith("/createdump", StringComparison.Ordinal);
            using FileStream content = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            PaxTarEntry fileEntry = new(TarEntryType.RegularFile, relativePath)
            {
                DataStream = content,
                Mode = isExecutable ? ExecutableFileMode : ReadOnlyFileMode,
                ModificationTime = File.GetLastWriteTimeUtc(path),
            };
            writer.WriteEntry(fileEntry);
        }
    }

    private static int GetDepth(string root, string path) => Path
        .GetRelativePath(root, path)
        .Count(character => character is '\\' or '/');

    private static (string Source, string Output, string Executable) ParseArguments(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Usage: CutLocal.MacArchive --source <directory> --output <archive.tar.gz> "
                    + "--executable <relative/path>");
            }

            values[args[index]] = args[index + 1];
        }

        return (
            GetRequired(values, "--source"),
            GetRequired(values, "--output"),
            GetRequired(values, "--executable").Replace('\\', '/'));
    }

    private static string GetRequired(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument: {key}");
}
