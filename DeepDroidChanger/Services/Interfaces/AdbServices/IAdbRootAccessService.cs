namespace DeepDroidChanger.Services;

public interface IAdbRootAccessService
{
    Task ExecuteAsRootAsync(
        string serial,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);

    Task<T> ExecuteAsRootAsync<T>(
        string serial,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}
