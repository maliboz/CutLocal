using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace CutLocal.App;

/// <summary>Hosts the explicit-network model management workflow.</summary>
public partial class ModelManagerWindow : Window
{
    private bool _initialized;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;

    /// <summary>Initializes the window with its injected view model.</summary>
    public ModelManagerWindow(ModelManagerViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Gets the strongly typed data context.</summary>
    public ModelManagerViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await ViewModel.InitializeAsync(CancellationToken.None);
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

        // Close must run after this Closing event has returned. ShutdownAsync can
        // complete synchronously when no model transfer is active; calling Close
        // again from the same Closing event would make WPF throw VerifyNotClosing.
        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Close));
        }
    }
}
