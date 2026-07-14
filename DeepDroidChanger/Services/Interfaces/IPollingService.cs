namespace DeepDroidChanger.Services;

public interface IPollingService
{
    Task RunAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);
}
