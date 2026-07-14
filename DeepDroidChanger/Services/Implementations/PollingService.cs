namespace DeepDroidChanger.Services;

public sealed class PollingService : IPollingService
{
    public async Task RunAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
