namespace DeepDroidChanger.Services;

public interface IUiDispatcherService
{
    bool CheckAccess();

    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}
