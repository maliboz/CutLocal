using CutLocal.Contracts;
using CutLocal.Domain;

namespace CutLocal.Application;

/// <summary>Discovers PNG files without following directory reparse points.</summary>
public sealed class AddFolderUseCase(AddImagesUseCase addImages)
{
    /// <summary>Enumerates one folder and appends its supported files to the job.</summary>
    public AddInputsResult Execute(
        ProcessingJob? existingJob,
        ProcessingPreset preset,
        string folderPath,
        bool includeSubfolders,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        string root = Path.GetFullPath(folderPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("The selected input folder does not exist.");
        }

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = includeSubfolders,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        List<string> files = [];
        foreach (string file in Directory.EnumerateFiles(root, "*.png", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(file);
            if (files.Count > AddImagesUseCase.MaximumBatchItems)
            {
                break;
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return addImages.Execute(existingJob, preset, files, cancellationToken);
    }
}
