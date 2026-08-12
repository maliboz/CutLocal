namespace CutLocal.App;

/// <summary>Opens user-requested local destinations through the Windows shell.</summary>
public interface IFileLauncher
{
    /// <summary>Opens the folder containing the specified output file.</summary>
    void OpenContainingFolder(string filePath);
}
