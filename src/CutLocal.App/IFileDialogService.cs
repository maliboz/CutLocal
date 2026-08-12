using CutLocal.Domain;

namespace CutLocal.App;

/// <summary>Abstracts the WPF file picker from the view model.</summary>
public interface IFileDialogService
{
    /// <summary>Shows a PNG picker and returns the selected local path.</summary>
    string? SelectPng();
    /// <summary>Shows a multi-select PNG picker.</summary>
    IReadOnlyList<string> SelectPngFiles();
    /// <summary>Shows a folder picker for batch input discovery.</summary>
    string? SelectInputFolder();
    /// <summary>Shows a folder picker and returns the selected local directory.</summary>
    string? SelectOutputFolder(string? initialDirectory);
    /// <summary>Shows a local ONNX picker for an advanced custom import.</summary>
    string? SelectOnnxModel() => null;
    /// <summary>Shows the companion model-manifest JSON picker.</summary>
    string? SelectModelManifest() => null;
    /// <summary>Requests explicit acknowledgement of the user-supplied model license.</summary>
    bool ConfirmCustomModelLicense() => false;
    /// <summary>Requests explicit acknowledgement before downloading restricted model weights.</summary>
    bool ConfirmRestrictedModelLicense(ModelDescriptor descriptor) => false;
}
