using System.Windows;

namespace DeepDroidChanger.Services;

public sealed class UiDispatcherService : IUiDispatcherService
{
    public bool CheckAccess()
    {
        return Application.Current?.Dispatcher.CheckAccess() ?? true;
    }

    public async Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (CheckAccess())
        {
            action();
            return;
        }

        await Application.Current.Dispatcher
            .InvokeAsync(action, System.Windows.Threading.DispatcherPriority.DataBind, cancellationToken)
            .Task
            .ConfigureAwait(false);
    }
}
