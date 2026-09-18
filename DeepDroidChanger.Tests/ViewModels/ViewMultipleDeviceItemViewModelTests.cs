using System.Windows.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class ViewMultipleDeviceItemViewModelTests
{
    private const string Serial = "SERIAL-1";

    [TestMethod]
    public async Task StartSuccess_PublishesOnline()
    {
        FakeTracker tracker = CreateOnlineTracker();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory());

        await item.StartAsync(CancellationToken.None);

        Assert.IsTrue(item.IsRunning);
        Assert.AreEqual(ViewDeviceSessionState.Running, item.State);
        Assert.AreEqual("Online", item.StatusText);
    }

    [TestMethod]
    public async Task StartSuccess_PublishesScrcpyClient()
    {
        FakeTracker tracker = CreateOnlineTracker();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory());

        await item.StartAsync(CancellationToken.None);

        Assert.IsNotNull(item.ScrcpyClient);
    }

    [TestMethod]
    public async Task StartSuccess_PublishesFirstFrameSize()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeSessionFactory factory = new(new FakeViewDeviceSession(Serial, 1080, 2220));
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);

        Assert.AreEqual(1080, item.ContentWidth);
        Assert.AreEqual(2220, item.ContentHeight);
        Assert.AreEqual(1080d / 2220d, item.DeviceAspectRatio, 0.000001);
    }

    [TestMethod]
    public async Task StartFailure_ClearsScrcpyClient()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial) { FailStart = true };
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(session));

        await item.StartAsync(CancellationToken.None);

        Assert.IsNull(item.ScrcpyClient);
        Assert.IsFalse(item.IsRunning);
        Assert.IsTrue(item.IsFailed);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task Stop_ClearsScrcpyClientBeforeDispose()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeSessionFactory factory = new(new FakeViewDeviceSession(Serial));
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewMultipleDeviceItemViewModel.ScrcpyClient) &&
                item.ScrcpyClient is null)
            {
                factory.LifecycleEvents.Add("client-cleared");
            }
        };

        await item.StartAsync(CancellationToken.None);
        await item.StopSessionAsync();

        int clearIndex = factory.LifecycleEvents.IndexOf("client-cleared");
        int disposeIndex = factory.LifecycleEvents.IndexOf($"dispose:{Serial}");
        Assert.IsTrue(clearIndex >= 0);
        Assert.IsTrue(disposeIndex >= 0);
        Assert.IsTrue(clearIndex < disposeIndex);
    }

    [TestMethod]
    public async Task Stop_ClearsContentSize()
    {
        FakeTracker tracker = CreateOnlineTracker();
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory());

        await item.StartAsync(CancellationToken.None);
        await item.StopSessionAsync();

        Assert.AreEqual(0, item.ContentWidth);
        Assert.AreEqual(0, item.ContentHeight);
        Assert.AreEqual(9d / 16d, item.DeviceAspectRatio, 0.000001);
    }

    [TestMethod]
    public async Task Exited_CleansCurrentSession()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(session));

        await item.StartAsync(CancellationToken.None);
        session.Exit();
        await WaitForConditionAsync(() => session.DisposeCount == 1);

        Assert.IsNull(item.ScrcpyClient);
        Assert.IsFalse(item.IsRunning);
        Assert.IsTrue(item.IsFailed);
    }

    [TestMethod]
    public async Task OldSessionExit_DoesNotStopReplacement()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession first = new(Serial, 1080, 2220);
        FakeViewDeviceSession second = new(Serial, 720, 1280);
        FakeSessionFactory factory = new(first, second);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);
        await item.StopSessionAsync();
        await item.StartAsync(CancellationToken.None);
        int secondStopCount = second.StopCount;

        first.Exit();
        await Task.Yield();

        Assert.IsTrue(item.IsRunning);
        Assert.AreEqual(secondStopCount, second.StopCount);
        Assert.AreEqual(720, item.ContentWidth);
        Assert.AreEqual(1280, item.ContentHeight);
    }

    [TestMethod]
    public async Task ContentSize_RotationUpdatesAspect()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial, 1080, 2220);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(session));

        await item.StartAsync(CancellationToken.None);
        session.RaiseContentSize(2220, 1080);

        Assert.AreEqual(2220d / 1080d, item.DeviceAspectRatio, 0.000001);
        Assert.AreEqual(1, session.StartCount);
    }

    [TestMethod]
    public async Task OldSessionFrameSize_DoesNotResizeReplacement()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession first = new(Serial, 1080, 2220);
        FakeViewDeviceSession second = new(Serial, 720, 1280);
        FakeSessionFactory factory = new(first, second);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);
        await item.StopSessionAsync();
        await item.StartAsync(CancellationToken.None);
        first.RaiseContentSize(2220, 1080);

        Assert.AreEqual(720, item.ContentWidth);
        Assert.AreEqual(1280, item.ContentHeight);
        Assert.AreEqual(720d / 1280d, item.DeviceAspectRatio, 0.000001);
    }

    [TestMethod]
    public async Task OldSessionStateChange_DoesNotChangeReplacementStatus()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession first = new(Serial);
        FakeViewDeviceSession second = new(Serial);
        FakeSessionFactory factory = new(first, second);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);

        await item.StartAsync(CancellationToken.None);
        await item.StopSessionAsync();
        await item.StartAsync(CancellationToken.None);
        first.RaiseState(SingleViewDeviceSessionState.Failed);

        Assert.IsTrue(item.IsRunning);
        Assert.AreEqual("Online", item.StatusText);
    }

    [TestMethod]
    public async Task OldStartCompletion_AfterPageChange_DoesNotPublishClient()
    {
        FakeTracker tracker = CreateOnlineTracker();
        TaskCompletionSource startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeViewDeviceSession first = new(Serial) { StartGate = startGate };
        FakeViewDeviceSession second = new(Serial);
        FakeSessionFactory factory = new(first, second);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(tracker, factory);
        using CancellationTokenSource cancellation = new();

        Task staleStart = item.StartAsync(cancellation.Token);
        await WaitForConditionAsync(() => first.StartCount == 1);
        cancellation.Cancel();
        startGate.TrySetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => staleStart);

        await item.StartAsync(CancellationToken.None);

        Assert.IsTrue(item.IsRunning);
        Assert.AreSame(second.Client, item.ScrcpyClient);
    }

    [TestMethod]
    public async Task OldRetry_AfterPageChange_DoesNotStartStaleItem()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession failed = new(Serial) { FailStart = true };
        List<int> attempts = [];
        TaskCompletionSource retryCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(failed),
            retryRequest: (_, attempt) => attempts.Add(attempt),
            retryDelay: async (_, cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    retryCancelled.TrySetResult();
                    throw;
                }
            });

        await item.StartAsync(CancellationToken.None);
        await WaitForConditionAsync(() => item.RetryAttempt == 1);
        await item.StopSessionAsync();
        await retryCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsEmpty(attempts);
        Assert.AreEqual(1, failed.StartCount);
    }

    [TestMethod]
    public async Task RetrySuccess_ResetsRetryAttempt()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession first = new(Serial) { FailStart = true };
        FakeViewDeviceSession second = new(Serial);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(first, second),
            retryRequest: (_, _) => { },
            retryDelay: static (_, cancellationToken) =>
                Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));

        await item.StartAsync(CancellationToken.None);
        Assert.AreEqual(1, item.RetryAttempt);

        await item.StartAsync(CancellationToken.None);

        Assert.IsTrue(item.IsRunning);
        Assert.AreEqual(0, item.RetryAttempt);
    }

    [TestMethod]
    public async Task BusySession_UsesBusyState()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(session));

        await item.StartAsync(CancellationToken.None);

        Assert.IsTrue(item.IsBusy);
        Assert.IsFalse(item.IsRunning);
        Assert.AreEqual("Already open in another viewer", item.StatusText);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task BusySession_DoesNotKillExistingLeaseOwner()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession ownerSession = new(Serial);
        await using ViewMultipleDeviceItemViewModel owner = CreateItem(
            tracker,
            new FakeSessionFactory(ownerSession));
        FakeViewDeviceSession busySession = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        await using ViewMultipleDeviceItemViewModel contender = CreateItem(
            tracker,
            new FakeSessionFactory(busySession));

        await owner.StartAsync(CancellationToken.None);
        await contender.StartAsync(CancellationToken.None);

        Assert.IsTrue(owner.IsRunning);
        Assert.AreEqual(0, ownerSession.StopCount);
        Assert.IsTrue(contender.IsBusy);
    }

    [TestMethod]
    public async Task DedicatedView_SkipsMultiSessionAndRetry()
    {
        ViewDevicePresentationCoordinator coordinator = new();
        IAsyncDisposable lease = await coordinator.AcquireDedicatedViewAsync(Serial);
        try
        {
            FakeTracker tracker = CreateOnlineTracker();
            FakeViewDeviceSession session = new(Serial)
            {
                StartException = new ScrcpyNetDeviceBusyException(Serial)
            };
            await using ViewMultipleDeviceItemViewModel item = CreateItem(
                tracker,
                new FakeSessionFactory(session),
                presentationCoordinator: coordinator,
                retryRequest: (_, _) => Assert.Fail("Dedicated view must not schedule retry."));

            await item.StartAsync(CancellationToken.None);

            Assert.IsTrue(item.IsDedicatedView);
            Assert.AreEqual(0, session.StartCount);
            Assert.AreEqual(0, item.RetryAttempt);
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task OpenViewDeviceCommand_UsesParentCallback()
    {
        FakeTracker tracker = CreateOnlineTracker();
        string? openedSerial = null;
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(),
            openViewDeviceRequest: (requestedItem, _) =>
            {
                openedSerial = requestedItem.Serial;
                return Task.CompletedTask;
            });

        Assert.IsTrue(item.OpenViewDeviceCommand.CanExecute(null));
        await item.OpenViewDeviceCommand.ExecuteAsync(null);

        Assert.AreEqual(Serial, openedSerial);
    }

    [TestMethod]
    public void CtrlV_FirstPress_IsConsumed()
    {
        Assert.IsTrue(ViewMultipleDeviceShortcutPolicy.IsExactHostPaste(
            Key.V,
            ModifierKeys.Control));
    }

    [TestMethod]
    public async Task CtrlV_FirstPress_ExecutesOnePaste()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial);
        FakeClipboardService clipboard = new() { Text = "hello" };
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(session),
            clipboard: clipboard);

        await item.StartAsync(CancellationToken.None);
        await item.PasteHostClipboardCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "hello" }, session.PastedTexts);
    }

    [TestMethod]
    public async Task CtrlV_Repeat_IsConsumedWithoutSecondPaste()
    {
        Assert.IsTrue(ViewMultipleDeviceShortcutPolicy.IsExactHostPaste(
            Key.V,
            ModifierKeys.Control));
        Assert.IsFalse(ViewMultipleDeviceShortcutPolicy.ShouldExecuteHostPaste(
            Key.V,
            ModifierKeys.Control,
            isRepeat: true));

        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial);
        FakeClipboardService clipboard = new() { Text = "hello" };
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(session),
            clipboard: clipboard);
        await item.StartAsync(CancellationToken.None);

        await item.PasteHostClipboardCommand.ExecuteAsync(null);

        Assert.AreEqual(1, session.PastedTexts.Count);
    }

    [TestMethod]
    public void CtrlShiftV_IsNotTreatedAsNativeHostPaste()
    {
        Assert.IsFalse(ViewMultipleDeviceShortcutPolicy.IsExactHostPaste(
            Key.V,
            ModifierKeys.Control | ModifierKeys.Shift));
    }

    [TestMethod]
    public async Task ClipboardEmpty_NoPaste()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(session),
            clipboard: new FakeClipboardService());

        await item.StartAsync(CancellationToken.None);
        await item.PasteHostClipboardCommand.ExecuteAsync(null);

        Assert.IsEmpty(session.PastedTexts);
    }

    [TestMethod]
    public async Task ClipboardReadCompletesAfterSessionReplacement_DoesNotPasteToReplacement()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession first = new(Serial);
        FakeViewDeviceSession second = new(Serial);
        TaskCompletionSource<string?> clipboardRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakeClipboardService clipboard = new() { ReadGate = clipboardRead };
        FakeSessionFactory factory = new(first, second);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            factory,
            clipboard: clipboard);

        await item.StartAsync(CancellationToken.None);
        Task paste = item.PasteHostClipboardCommand.ExecuteAsync(null);
        await WaitForConditionAsync(() => clipboard.ReadCount == 1);
        await item.StopSessionAsync();
        await item.StartAsync(CancellationToken.None);
        clipboardRead.TrySetResult("stale clipboard");
        await paste;

        Assert.IsEmpty(first.PastedTexts);
        Assert.IsEmpty(second.PastedTexts);
    }

    [TestMethod]
    public async Task PasteQueueRejected_IsLoggedHandledWithoutCrashingTile()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeViewDeviceSession session = new(Serial) { RejectPaste = true };
        FakeClipboardService clipboard = new() { Text = "hello" };
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            new FakeSessionFactory(session),
            clipboard: clipboard);

        await item.StartAsync(CancellationToken.None);
        await item.PasteHostClipboardCommand.ExecuteAsync(null);

        Assert.IsEmpty(session.PastedTexts);
        Assert.IsTrue(item.IsRunning);
    }

    [TestMethod]
    public async Task LocalizationRefresh_UpdatesBusyStatusWithoutRestart()
    {
        FakeTracker tracker = CreateOnlineTracker();
        FakeLocalizationService localization = new();
        FakeViewDeviceSession session = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        FakeSessionFactory factory = new(session);
        await using ViewMultipleDeviceItemViewModel item = CreateItem(
            tracker,
            factory,
            localization: localization);

        await item.StartAsync(CancellationToken.None);
        localization.ApplyLanguage("vi");
        await item.RefreshLocalizedTextAsync();

        Assert.AreEqual("Thiết bị đang được mở ở cửa sổ xem khác", item.StatusText);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
    }

    private static ViewMultipleDeviceItemViewModel CreateItem(
        FakeTracker tracker,
        FakeSessionFactory factory,
        FakeLocalizationService? localization = null,
        FakeClipboardService? clipboard = null,
        Action<ViewMultipleDeviceItemViewModel, int>? retryRequest = null,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null,
        IViewDevicePresentationCoordinator? presentationCoordinator = null,
        Func<ViewMultipleDeviceItemViewModel, CancellationToken, Task>? openViewDeviceRequest = null)
    {
        IViewDevicePresentationCoordinator coordinator =
            presentationCoordinator ?? new ViewDevicePresentationCoordinator();
        return new ViewMultipleDeviceItemViewModel(
            Serial,
            "Device",
            factory,
            tracker,
            clipboard ?? new FakeClipboardService { Text = "" },
            localization ?? new FakeLocalizationService(),
            new ImmediateUiDispatcher(),
            NullLogger<ViewMultipleDeviceItemViewModel>.Instance,
            coordinator,
            retryRequest,
            retryDelay,
            openViewDeviceRequest);
    }

    private static FakeTracker CreateOnlineTracker()
    {
        return new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
    }

    private static async Task WaitForConditionAsync(
        Func<bool> condition,
        int timeoutMilliseconds = 2000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10).ConfigureAwait(false);

        Assert.IsTrue(condition(), "The expected asynchronous condition was not reached.");
    }
}
