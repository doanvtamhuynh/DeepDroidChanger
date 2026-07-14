using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class PollingServiceTests
{
    [TestMethod]
    public async Task RunAsync_Cancellation_StopsLoopDeterministically()
    {
        var service = new PollingService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reachedTwoCalls = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        Task pollingTask = service.RunAsync(
            TimeSpan.FromMilliseconds(10),
            _ =>
            {
                if (Interlocked.Increment(ref callCount) == 2)
                    reachedTwoCalls.TrySetResult();
                return Task.CompletedTask;
            },
            cancellation.Token);

        await reachedTwoCalls.Task;
        await cancellation.CancelAsync();
        await pollingTask;
        int countAfterStop = callCount;
        await Task.Delay(30);

        Assert.AreEqual(countAfterStop, callCount);
    }
}
