using System.Windows.Media.Imaging;

namespace CutLocal.App;

/// <summary>Loads bounded, frozen WPF proxy bitmaps for the preview surface.</summary>
public interface IPreviewBitmapService
{
    /// <summary>Loads a color proxy whose longest edge does not exceed the requested bound.</summary>
    ValueTask<BitmapSource> LoadColorAsync(
        string path,
        int maximumEdge,
        CancellationToken cancellationToken);

    /// <summary>Loads output alpha as an opaque grayscale proxy.</summary>
    ValueTask<BitmapSource> LoadAlphaMaskAsync(
        string path,
        int maximumEdge,
        CancellationToken cancellationToken);
}
