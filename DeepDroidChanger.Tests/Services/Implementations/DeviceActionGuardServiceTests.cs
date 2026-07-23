using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class DeviceActionGuardServiceTests
{
    [TestMethod]
    public void TryAcquire_GuardsOnlyMatchingSerialUntilLeaseIsDisposed()
    {
        var service = new DeviceActionGuardService();
        var stateChanges = new List<(string Serial, bool IsBusy)>();
        service.BusyStateChanged += (serial, isBusy) => stateChanges.Add((serial, isBusy));

        using IDisposable? firstDeviceLease = service.TryAcquire(" SERIAL-A ");
        using IDisposable? secondAttempt = service.TryAcquire("serial-a");
        using IDisposable? otherDeviceLease = service.TryAcquire("SERIAL-B");

        Assert.IsNotNull(firstDeviceLease);
        Assert.IsNull(secondAttempt);
        Assert.IsNotNull(otherDeviceLease);
        Assert.IsTrue(service.IsBusy("serial-a"));
        Assert.IsTrue(service.IsBusy("serial-b"));

        firstDeviceLease.Dispose();
        firstDeviceLease.Dispose();

        Assert.IsFalse(service.IsBusy("SERIAL-A"));
        Assert.IsTrue(service.IsBusy("SERIAL-B"));
        CollectionAssert.AreEqual(
            new[]
            {
                ("SERIAL-A", true),
                ("SERIAL-B", true),
                ("SERIAL-A", false)
            },
            stateChanges);
    }

    [TestMethod]
    public async Task TryAcquire_ConcurrentAttemptsForSameSerial_AllowExactlyOneLeaseAsync()
    {
        var service = new DeviceActionGuardService();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<IDisposable?>[] attempts = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task;
                return service.TryAcquire("SERIAL-A");
            }))
            .ToArray();

        start.SetResult();
        IDisposable?[] leases = await Task.WhenAll(attempts);

        Assert.AreEqual(1, leases.Count(lease => lease != null));
        Assert.IsTrue(service.IsBusy("SERIAL-A"));

        leases.Single(lease => lease != null)!.Dispose();
        Assert.IsFalse(service.IsBusy("SERIAL-A"));
    }
}
