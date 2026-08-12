using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CutLocal.App;

/// <summary>Opens folders through the Windows Shell API without starting a child process.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class FileLauncher : IFileLauncher
{
    /// <inheritdoc />
    public void OpenContainingFolder(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The output folder no longer exists.");
        }

        nint folderIdentifier = 0;
        try
        {
            int parseResult = SHParseDisplayName(
                directory,
                bindingContext: 0,
                out folderIdentifier,
                attributesIn: 0,
                out _);
            Marshal.ThrowExceptionForHR(parseResult);
            int openResult = SHOpenFolderAndSelectItems(
                folderIdentifier,
                childCount: 0,
                childIdentifiers: 0,
                flags: 0);
            Marshal.ThrowExceptionForHR(openResult);
        }
        finally
        {
            if (folderIdentifier != 0)
            {
                Marshal.FreeCoTaskMem(folderIdentifier);
            }
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(
        string name,
        nint bindingContext,
        out nint itemIdentifier,
        uint attributesIn,
        out uint attributesOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenFolderAndSelectItems(
        nint folderIdentifier,
        uint childCount,
        nint childIdentifiers,
        uint flags);
}
