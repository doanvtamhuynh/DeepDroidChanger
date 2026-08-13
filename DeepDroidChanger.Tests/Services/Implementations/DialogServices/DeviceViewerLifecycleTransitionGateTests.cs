using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeviceViewerLifecycleTransitionGateTests
{
    [TestMethod]
    public async Task Close_DrainsInFlightTransition_AndPreventsQueuedMutation()
    {
        var gate = new DeviceViewerLifecycleTransitionGate();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutations = new List<string>();

        Task first = gate.RunAsync(async () =>
        {
            mutations.Add("first-started");
            entered.SetResult();
            await release.Task;
            mutations.Add("first-finished");
        });
        await entered.Task;

        Task queued = gate.RunAsync(() =>
        {
            mutations.Add("queued-mutated-after-close");
            return Task.CompletedTask;
        });

        gate.Close();
        release.SetResult();
        await gate.DrainAsync();
        await Task.WhenAll(first, queued);

        CollectionAssert.AreEqual(
            new[] { "first-started", "first-finished" },
            mutations);
    }
}
