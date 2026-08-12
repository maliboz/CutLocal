namespace CutLocal.App;

/// <summary>Shows the isolated, explicit-network model manager surface.</summary>
public interface IModelManagerDialog
{
    /// <summary>Shows the model manager modally on the UI dispatcher.</summary>
    Task ShowAsync(CancellationToken cancellationToken);
}

/// <summary>Creates a fresh model-manager window while retaining its injected view model.</summary>
public sealed class ModelManagerDialog(ModelManagerViewModel viewModel) : IModelManagerDialog
{
    /// <inheritdoc />
    public Task ShowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ModelManagerWindow window = new(viewModel)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        window.ShowDialog();
        return Task.CompletedTask;
    }
}
