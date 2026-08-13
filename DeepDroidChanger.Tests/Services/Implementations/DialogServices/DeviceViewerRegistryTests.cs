using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeviceViewerRegistryTests
{
    [TestMethod]
    public async Task ConcurrentRequestsForOneSerial_CreateAndActivateOneEntry()
    {
        var registry = new DeviceViewerRegistry<Entry>();
        var createCount = 0;
        var activateCount = 0;

        var requests = Enumerable.Range(0, 12)
            .Select(_ => registry.GetOrCreateAsync(
                "A",
                entry => entry.IsLive,
                () =>
                {
                    Interlocked.Increment(ref createCount);
                    return Task.FromResult(new Entry());
                },
                _ =>
                {
                    Interlocked.Increment(ref activateCount);
                    return Task.CompletedTask;
                },
                CancellationToken.None))
            .ToArray();

        var entries = await Task.WhenAll(requests);

        Assert.AreEqual(1, createCount);
        Assert.AreEqual(11, activateCount);
        Assert.IsTrue(entries.All(entry => ReferenceEquals(entry, entries[0])));
        Assert.AreEqual(1, registry.Count);
    }

    [TestMethod]
    public async Task DifferentSerialsCoexist_AndClosingOneDoesNotRemoveTheOther()
    {
        var registry = new DeviceViewerRegistry<Entry>();
        var entryA = await CreateAsync(registry, "A");
        var entryB = await CreateAsync(registry, "B");

        Assert.AreEqual(2, registry.Count);
        Assert.IsTrue(registry.Remove("A", entryA));
        Assert.IsFalse(registry.Contains("A", entryA));
        Assert.IsTrue(registry.Contains("B", entryB));
    }

    [TestMethod]
    public async Task AfterRemoval_OpeningSerialCreatesNewEntry()
    {
        var registry = new DeviceViewerRegistry<Entry>();
        var first = await CreateAsync(registry, "A");
        registry.Remove("A", first);

        var second = await CreateAsync(registry, "A");

        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public async Task CreatedEntryThatClosesBeforeRegistration_IsNotStored()
    {
        var registry = new DeviceViewerRegistry<Entry>();
        Entry? created = null;

        Entry result = await registry.GetOrCreateAsync(
            "A",
            entry => entry.IsLive,
            () =>
            {
                created = new Entry();
                created.IsLive = false;
                return Task.FromResult(created);
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.AreSame(created, result);
        Assert.AreEqual(0, registry.Count);
    }

    private static Task<Entry> CreateAsync(DeviceViewerRegistry<Entry> registry, string serial)
    {
        return registry.GetOrCreateAsync(
            serial,
            entry => entry.IsLive,
            () => Task.FromResult(new Entry()),
            _ => Task.CompletedTask,
            CancellationToken.None);
    }

    private sealed class Entry
    {
        public bool IsLive { get; set; } = true;
    }
}
