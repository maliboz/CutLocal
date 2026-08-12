using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CutLocal.App;

/// <summary>Hosts the single-image desktop workflow and native drag/drop boundary.</summary>
public partial class MainWindow : Window
{
    private bool _initialized;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;

    /// <summary>Initializes the window with its injected view model.</summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Gets the strongly typed window data context.</summary>
    public MainWindowViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ViewModel.InitializeAsync(CancellationToken.None);
    }

    private void OnDragEnter(object sender, DragEventArgs e) => UpdateDropState(e);

    private void OnDragOver(object sender, DragEventArgs e) => UpdateDropState(e);

    private void OnDragLeave(object sender, DragEventArgs e) => ViewModel.SetDropActive(false);

    private async void OnDrop(object sender, DragEventArgs e)
    {
        ViewModel.SetDropActive(false);
        e.Effects = DragDropEffects.None;
        if (!TryGetPngFiles(e.Data, ViewModel.IsBatchMode, out string[] paths))
        {
            await ViewModel.AcceptDroppedFilesAsync([], CancellationToken.None);
            e.Handled = true;
            return;
        }

        if (await ViewModel.AcceptDroppedFilesAsync(paths, CancellationToken.None))
        {
            e.Effects = DragDropEffects.Copy;
        }

        e.Handled = true;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        await ViewModel.ShutdownAsync();
        _shutdownCompleted = true;

        // Defer the final Close until the current Closing event has unwound.
        // This is required when ShutdownAsync completes synchronously.
        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Close));
        }
    }

    private void UpdateDropState(DragEventArgs e)
    {
        bool valid = TryGetPngFiles(e.Data, ViewModel.IsBatchMode, out _);
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        ViewModel.SetDropActive(valid);
        e.Handled = true;
    }

    private static bool TryGetPngFiles(IDataObject data, bool batchMode, out string[] paths)
    {
        paths = data.GetDataPresent(DataFormats.FileDrop)
            ? data.GetData(DataFormats.FileDrop) as string[] ?? []
            : [];
        return paths.Length > 0
            && (batchMode || paths.Length == 1)
            && paths.All(path => File.Exists(path)
                && Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase));
    }
}
